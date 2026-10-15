using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Types;
using Statement = W.Dm.Internal.Legacy.A.A;

namespace W.Dm;

public partial class DmCommand
{
    private readonly List<Statement> procedureCursorStatements = new();
    private readonly List<DmDataReader> procedureCursorReaders = new();

    private static void RejectCursorCompletion(DmCommandPlan plan)
    {
        if (plan.HasCursorParameters)
            throw new NotSupportedException("Cursor output parameters require ExecuteReader.");
    }

    private static void ValidateProcedureTransaction(DmCommandPlan plan)
    {
        if (plan.CommandType == CommandType.StoredProcedure &&
            (plan.Transaction != null || plan.Connection?.Session?.ActiveTransaction != null))
            throw new NotSupportedException("Stored procedures cannot execute inside an explicit transaction: procedure transaction completion is not reliably observable.");
    }

    // A pure output declaration must never encode or infer from the caller's old Value.
    private static bool BindProcedureOutputDeclaration(DmCommandPlan plan, DmParameter parameter,
        DmParameterInternal descriptor)
    {
        if (plan.CommandType != CommandType.StoredProcedure) return false;
        if (descriptor.GetCType() == 120)
        {
            if (parameter.DmSqlType is not (DmDbType.Cursor or DmDbType.RefCursor) ||
                parameter.Direction is not (ParameterDirection.Output or ParameterDirection.ReturnValue))
                throw new NotSupportedException("Cursor parameters require explicit Cursor/RefCursor and output direction.");
            return true; // Allocate and bind the cursor statement outside the local-only binding scope.
        }
        if (parameter.DmSqlType is DmDbType.Cursor or DmDbType.RefCursor)
            throw new InvalidOperationException("Cursor parameter does not match the server declaration.");
        if (parameter.Direction != ParameterDirection.Input) ValidateProcedureOutputDeclaration(parameter, descriptor);
        if (parameter.Direction is not (ParameterDirection.Output or ParameterDirection.ReturnValue)) return false;
        var resolved = parameter.ResolveType(descriptor);
        SelectBoundCType(descriptor, resolved.Type, resolved.Source);
        if (DmTemporalMetadataSemantics.IsTemporal(descriptor.GetCType()))
            DmTemporalBindingResolver.ValidateRequest(DmParameterMetadata.From(parameter));
        return true;
    }

    private static void ValidateProcedureOutputDeclaration(DmParameter parameter, DmParameterInternal descriptor)
    {
        if (parameter.m_SetSizeFlag && parameter.do_Size < 0)
            throw new ArgumentOutOfRangeException(nameof(parameter), "Output Size cannot be negative.");
        if (descriptor.GetCType() is 9 or 24)
        {
            int precision = parameter.m_SetPrecFlag ? parameter.do_Precision : 0;
            int scale = parameter.m_SetScaleFlag ? parameter.do_Scale : -1;
            if (precision > 38 || scale > 38 || (precision > 0 && scale > precision))
                throw new ArgumentOutOfRangeException(nameof(parameter), "Invalid output DECIMAL precision or scale.");
        }
        else if (DmTemporalMetadataSemantics.IsTemporal(descriptor.GetCType()))
        {
            DmTemporalBindingResolver.ValidateRequest(DmParameterMetadata.From(parameter));
            if (parameter.m_SetScaleFlag &&
                (descriptor.GetCType() == 20 && parameter.do_Scale != 0 ||
                 descriptor.GetCType() == 21 && parameter.do_Scale > 6))
                throw new NotSupportedException("Interval output Scale exceeds the supported precision.");
        }
        else if ((parameter.m_SetPrecFlag && parameter.do_Precision != 0) || parameter.m_SetScaleFlag)
            throw new NotSupportedException("Output Precision and Scale require a DECIMAL or temporal declaration.");
    }

    private void PrepareProcedureCursors()
    {
        if (ActivePlan?.HasCursorParameters != true) return;
        m_Stmt.H().GetParamsInfo(out DmParameterInternal[] descriptors);
        for (int index = 0; index < descriptors.Length; index++)
        {
            if (descriptors[index].GetCType() != 120) continue;
            var parameter = ActivePlan.Binding.ResolveServerParameter(index, descriptors[index].GetName(), descriptors.Length);
            var cursor = new Statement(m_Stmt.G(), this);
            BindProcedureCursor(parameter, descriptors[index], cursor);
        }
    }

    private async Task PrepareProcedureCursorsAsync(CancellationToken token)
    {
        if (ActivePlan?.HasCursorParameters != true) return;
        m_Stmt.H().GetParamsInfo(out DmParameterInternal[] descriptors);
        for (int index = 0; index < descriptors.Length; index++)
        {
            if (descriptors[index].GetCType() != 120) continue;
            var parameter = ActivePlan.Binding.ResolveServerParameter(index, descriptors[index].GetName(), descriptors.Length);
            var cursor = await Statement.CreateAsync(m_Stmt.G(), this, token).ConfigureAwait(false);
            BindProcedureCursor(parameter, descriptors[index], cursor);
        }
    }

    private void BindProcedureCursor(DmParameter parameter, DmParameterInternal descriptor, Statement cursor)
    {
        procedureCursorStatements.Add(cursor);
        parameter.refCursorStmt = cursor;
        byte[] id = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(id, cursor.g());
        descriptor.SetInValue(id, 0);
    }

    internal object MaterializeProcedureCursor(DmParameter parameter, Statement statement,
        DmParameterInternal descriptor, byte[] raw)
    {
        var cursor = RequireProcedureCursor(parameter, descriptor, raw);
        cursor.h().A(cursor, (short)1);
        return CreateProcedureCursorReader(cursor);
    }

    internal async Task<object> MaterializeProcedureCursorAsync(DmParameter parameter, Statement statement,
        DmParameterInternal descriptor, byte[] raw, CancellationToken token)
    {
        var cursor = RequireProcedureCursor(parameter, descriptor, raw);
        await cursor.h().AAsync(cursor, (short)1, token).ConfigureAwait(false);
        return CreateProcedureCursorReader(cursor);
    }

    private static Statement RequireProcedureCursor(DmParameter parameter, DmParameterInternal descriptor, byte[] raw)
    {
        // The characterized OUT cursor response is the NULL marker; its already registered
        // statement id is the input payload. It is not a locator in the response bytes.
        if (raw != null || descriptor.GetCType() != 120 || parameter.refCursorStmt == null)
            throw new InvalidDataException("Unrecognized cursor output response.");
        return parameter.refCursorStmt;
    }

    private DmDataReader CreateProcedureCursorReader(Statement cursor)
    {
        if (cursor.m()?.GetHasResultSet() != true || cursor.l() == null)
            throw new InvalidDataException("Cursor output did not describe a rowset.");
        var reader = new DmDataReader(cursor.l(), cursor.m(), CommandBehavior.Default);
        reader.AttachExecutionLease(DmInvocation.Current.Lease, ownsLease: false);
        procedureCursorReaders.Add(reader);
        return reader;
    }

    private void AttachProcedureReader(DmDataReader reader)
    {
        var plan = ActivePlan;
        if (plan?.CommandType != CommandType.StoredProcedure || reader == null) return;
        reader.AttachProcedureExecution(plan, procedureCursorReaders.ToArray());
    }

    private void CloseUnpublishedProcedureCursors()
    {
        foreach (var cursor in procedureCursorStatements)
            if (!cursor.P()) cursor.__t02_method_060008BE(true, false);
        procedureCursorStatements.Clear();
        procedureCursorReaders.Clear();
    }
    private async Task CloseUnpublishedProcedureCursorsAsync()
    {
        foreach (var cursor in procedureCursorStatements)
            if (!cursor.P()) await cursor.CloseAsync(true, false, CancellationToken.None).ConfigureAwait(false);
        procedureCursorStatements.Clear();
        procedureCursorReaders.Clear();
    }

}
