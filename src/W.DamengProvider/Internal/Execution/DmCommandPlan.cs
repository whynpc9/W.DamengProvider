using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Threading;
using W.Dm.Internal.Types;
using W.Dm.Internal.Sessions;

namespace W.Dm.Internal.Execution;

/// <summary>Separates short command state transitions from execution and user callbacks.</summary>
internal sealed class DmCommandPlanGate
{
    private readonly object state = new();
    private bool executing;
    private bool disposed;
    private int mutationOwnerThread;
    private int mutationDepth;

    internal bool IsExecuting { get { lock (state) return executing; } }
    internal bool IsDisposed { get { lock (state) return disposed; } }

    internal void MarkDisposed()
    {
        lock (state) disposed = true;
    }

    internal IDisposable BeginMutation()
    {
        lock (state)
        {
            if (disposed) throw new ObjectDisposedException("DmCommand");
            if (executing) throw new InvalidOperationException("Command settings cannot change during execution.");
            int thread = Environment.CurrentManagedThreadId;
            if (mutationDepth != 0 && mutationOwnerThread != thread)
                throw new InvalidOperationException("Another command mutation is active.");
            mutationOwnerThread = thread;
            mutationDepth++;
        }
        return new Mutation(this);
    }

    internal DmCommandPlan Enter(Func<DmCommandPlan> capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        lock (state)
        {
            if (disposed) throw new ObjectDisposedException("DmCommand");
            if (executing || mutationDepth != 0)
                throw new InvalidOperationException("Command is executing or being modified.");
            executing = true;
        }
        try
        {
            DmCommandPlan plan = capture() ?? throw new InvalidOperationException("Command plan capture returned null.");
            plan.Attach(this);
            return plan;
        }
        catch
        {
            Exit();
            throw;
        }
    }

    /// <summary>Marks a batch or batch command as executing without a command plan.</summary>
    internal void EnterExecution(string owner)
    {
        lock (state)
        {
            if (disposed) throw new ObjectDisposedException(owner);
            if (executing || mutationDepth != 0)
                throw new InvalidOperationException($"{owner} is executing or being modified.");
            executing = true;
        }
    }

    private void EndMutation()
    {
        lock (state)
        {
            if (mutationDepth <= 0 || mutationOwnerThread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Command mutation identity is stale.");
            if (--mutationDepth == 0) mutationOwnerThread = 0;
        }
    }

    internal void Exit()
    {
        lock (state) executing = false;
    }

    private sealed class Mutation : IDisposable
    {
        private DmCommandPlanGate owner;
        internal Mutation(DmCommandPlanGate owner) => this.owner = owner;
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.EndMutation();
    }
}

/// <summary>A frozen command contract for one execution, including an isolated parameter set.</summary>
internal sealed class DmCommandPlan : IDisposable
{
    private readonly object cancellationGate = new();
    private DmExecutionLease execution;
    private bool cancellationRequested;
    private DmCommandPlanGate gate;
    private int disposed;
    private readonly object outputGate = new();
    private readonly Dictionary<DmParameter, DmParameter> originalOutputs = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DmParameter, object> previousOutputs = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DmParameter, object> stagedOutputs = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<DmParameter> publishedCursors = new(ReferenceEqualityComparer.Instance);
    private bool outputsFailed;
    private bool outputsPublished;

    internal string Sql { get; }
    internal string WireSql { get; }
    internal CommandType CommandType { get; }
    internal int TimeoutSeconds { get; }
    internal DmTransaction Transaction { get; }
    internal DmConnection Connection { get; }
    internal TimeSpan CleanupTimeout { get; }
    internal DmParameterCollection Parameters { get; }
    internal DmParameterBinding Binding { get; }
    internal DmParameterMetadata[] ParameterMetadata { get; }
    internal DmTemporalParameterBinding[] TemporalBindings { get; private set; }
    internal bool HasOutputParameters => originalOutputs.Count != 0;
    internal bool HasCursorParameters { get; }
    /// <summary>Zero for a scalar execution; otherwise the number of array-bound rows.</summary>
    internal int ArrayBindCount { get; }
    private readonly DmParameter[][] arrayRows;

    internal void ClearTemporalBindings() => TemporalBindings = null;

    internal void PublishTemporalBindings(DmTemporalParameterBinding[] bindings) =>
        TemporalBindings = (DmTemporalParameterBinding[])bindings.Clone();

    /// <summary>Maps a resolved template parameter to its frozen values, one per row.</summary>
    internal Func<int, DmParameter> RowParameters(DmParameter template)
    {
        if (arrayRows == null)
            return row => row == 0 ? template : throw new ArgumentOutOfRangeException(nameof(row));
        int index = Parameters.do_IndexOf(template);
        if (index < 0) throw new InvalidOperationException("Parameter does not belong to this command plan.");
        return row => arrayRows[row][index];
    }

    private DmCommandPlan(string sql, string wireSql, CommandType commandType, int timeoutSeconds,
        DmTransaction transaction, DmConnection connection, DmParameterCollection parameters,
        DmParameterBinding binding, DmParameterMetadata[] parameterMetadata, DmParameterCollection originals,
        DmParameter[][] arrayRows = null)
    {
        this.arrayRows = arrayRows;
        ArrayBindCount = arrayRows?.Length ?? 0;
        Sql = sql;
        WireSql = wireSql;
        CommandType = commandType;
        TimeoutSeconds = timeoutSeconds;
        Transaction = transaction;
        Connection = connection;
        CleanupTimeout = connection?.Settings?.CleanupTimeout ?? TimeSpan.FromSeconds(5);
        Parameters = parameters;
        Binding = binding;
        ParameterMetadata = parameterMetadata;
        for (int index = 0; index < parameters.Count; index++)
        {
            DmParameter snapshot = parameters.do_GetParameter(index);
            if (snapshot.do_Direction == ParameterDirection.Input) continue;
            DmParameter original = originals.do_GetParameter(index);
            originalOutputs.Add(snapshot, original);
            previousOutputs.Add(snapshot, original.do_Value);
            HasCursorParameters |= snapshot.DmSqlType is DmDbType.Cursor or DmDbType.RefCursor;
        }
    }

    internal static DmCommandPlan Capture(string sql, CommandType commandType, int timeoutSeconds,
        DmTransaction transaction, DmConnection connection, DmParameterCollection parameters, int arrayBindCount = 0)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (arrayBindCount != 0 && commandType != CommandType.Text)
            throw new NotSupportedException("Array binding supports only CommandType.Text.");
        DmParameter[][] rows = arrayBindCount == 0 ? null : CaptureArrayRows(parameters, arrayBindCount);
        DmParameterCollection copy = rows == null ? parameters.CloneDetached(forPlan: true) : new DmParameterCollection();
        // Row zero is the template used for marker resolution and prepared-metadata identity.
        if (rows != null) foreach (DmParameter parameter in rows[0]) copy.do_Add(parameter);
        foreach (DmParameter parameter in copy) parameter.ValidateTypeConfiguration();
        string wireSql = commandType == CommandType.StoredProcedure ? DmProcedureCall.Create(sql, copy) : sql;
        DmParameterBinding binding = commandType == CommandType.StoredProcedure
            ? DmParameterBinding.CreateProcedure(copy) : DmParameterBinding.Create(sql, copy);
        var metadata = new DmParameterMetadata[copy.Count];
        for (int index = 0; index < metadata.Length; index++)
        {
            metadata[index] = DmParameterMetadata.From((DmParameter)copy[index]);
            if (DmTemporalMetadataSemantics.IsTemporal(DmSqlType.DmDbTypeToDmSqlType(metadata[index].DmSqlType)))
                DmTemporalBindingResolver.ValidateRequest(metadata[index]);
        }
        return new DmCommandPlan(sql, wireSql, commandType, timeoutSeconds, transaction, connection, copy, binding, metadata, parameters, rows);
    }

    internal void StageOutput(DmParameter snapshot, object value)
    {
        lock (outputGate)
        {
            if (outputsFailed || outputsPublished || Volatile.Read(ref disposed) != 0)
                throw new InvalidOperationException("Output staging is no longer active.");
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            bool cursor = snapshot.DmSqlType is DmDbType.Cursor or DmDbType.RefCursor;
            if (value is DbDataReader && !cursor || cursor && value is not (null or DBNull or DbDataReader))
                throw new InvalidOperationException("Cursor output value does not match its parameter type.");
            if (!originalOutputs.ContainsKey(snapshot) || !stagedOutputs.TryAdd(snapshot, value ?? DBNull.Value))
                throw new InvalidOperationException("Output parameter identity is unknown or was already staged.");
        }
    }

    internal void PublishCursorOutputs()
    {
        lock (outputGate)
        {
            if (outputsFailed || Volatile.Read(ref disposed) != 0)
                throw new InvalidOperationException("Output publication is no longer active.");
            foreach (var output in stagedOutputs)
                if (output.Value is DbDataReader && publishedCursors.Add(output.Key))
                    originalOutputs[output.Key].PublishOutputValue(output.Value);
        }
    }

    internal void PublishOutputs()
    {
        lock (outputGate)
        {
            if (outputsPublished) return;
            if (outputsFailed || Volatile.Read(ref disposed) != 0)
                throw new InvalidOperationException("Output publication is no longer active.");
            if (stagedOutputs.Count != originalOutputs.Count)
                throw new InvalidOperationException("Execution did not supply every output parameter.");
            foreach (var output in stagedOutputs)
                originalOutputs[output.Key].PublishOutputValue(output.Value);
            outputsPublished = true;
        }
    }

    internal void FailOutputs()
    {
        lock (outputGate)
        {
            if (outputsPublished) return;
            outputsFailed = true;
            foreach (DmParameter snapshot in publishedCursors)
                originalOutputs[snapshot].PublishOutputValue(previousOutputs[snapshot]);
            publishedCursors.Clear();
            stagedOutputs.Clear();
        }
    }

    private static DmParameter[][] CaptureArrayRows(DmParameterCollection parameters, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "ArrayBindCount cannot be negative.");
        if (parameters.do_Count == 0)
            throw new InvalidOperationException("Array binding requires at least one parameter.");
        var rows = new DmParameter[count][];
        for (int row = 0; row < count; row++) rows[row] = new DmParameter[parameters.do_Count];
        for (int index = 0; index < parameters.do_Count; index++)
        {
            DmParameter parameter = parameters.do_GetParameter(index);
            if (parameter.do_Value is not Array values || values.Rank != 1 || values.Length != count)
                throw new InvalidOperationException(
                    $"Array-bound parameter '{parameter.do_ParameterName}' requires a one-dimensional array of ArrayBindCount values.");
            DmParameter typed = null;
            for (int row = 0; row < count; row++)
            {
                object element = values.GetValue(values.GetLowerBound(0) + row);
                DmParameter clone = parameter.CloneForArrayRow(element);
                if (clone.HasInferredType)
                {
                    if (typed == null) typed = clone;
                    else if (!clone.SameInferredType(typed))
                        throw new InvalidOperationException(
                            $"Array-bound parameter '{parameter.do_ParameterName}' mixes values with different CLR type mappings; set DbType or DmSqlType.");
                }
                rows[row][index] = clone;
            }
            if (typed != null)
                for (int row = 0; row < count; row++) rows[row][index].AdoptInferredType(typed);
        }
        return rows;
    }

    internal void Attach(DmCommandPlanGate owner)
    {
        if (Interlocked.CompareExchange(ref gate, owner, null) != null)
            throw new InvalidOperationException("Command plan already has an owner.");
    }

    internal void AttachExecution(DmExecutionLease lease)
    {
        bool cancel;
        lock (cancellationGate)
        {
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(DmCommandPlan));
            if (execution != null) throw new InvalidOperationException("Command plan already has an execution.");
            execution = lease ?? throw new ArgumentNullException(nameof(lease));
            cancel = cancellationRequested;
        }
        if (cancel) lease.Cancel();
    }

    internal void RequestCancellation()
    {
        DmExecutionLease captured;
        lock (cancellationGate)
        {
            if (Volatile.Read(ref disposed) != 0) return;
            cancellationRequested = true;
            captured = execution;
        }
        // Callbacks and transport disposal run outside the plan's short state lock.
        FailOutputs();
        captured?.Cancel();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        FailOutputs();
        lock (cancellationGate) execution = null;
        Interlocked.Exchange(ref gate, null)?.Exit();
    }
}

internal readonly record struct DmParameterMetadata(string Name, System.Data.DbType DbType, DmDbType DmSqlType,
    W.Dm.Internal.Types.DmParameterTypeSource Source, int Size, byte Precision, byte Scale,
    bool ExplicitSize, bool ExplicitPrecision, bool ExplicitScale, System.Data.ParameterDirection Direction,
    string TypeName, bool ExplicitDbType, System.Data.DbType DeclaredDbType,
    bool ExplicitDmSqlType, DmDbType DeclaredDmSqlType, Type ValueType)
{
    internal static DmParameterMetadata From(DmParameter parameter) => new(
        parameter.do_ParameterName, parameter.do_DbType, parameter.DmSqlType, parameter.TypeSource,
        parameter.m_SetSizeFlag ? parameter.do_Size : 0,
        parameter.m_SetPrecFlag ? parameter.do_Precision : (byte)0,
        parameter.m_SetScaleFlag ? parameter.do_Scale : (byte)0,
        parameter.m_SetSizeFlag, parameter.m_SetPrecFlag, parameter.m_SetScaleFlag,
        parameter.do_Direction, parameter.DmSqlTypeName,
        parameter.HasExplicitDbType, parameter.DeclaredDbType,
        parameter.HasExplicitDmSqlType, parameter.DeclaredDmSqlType, parameter.do_Value?.GetType());
}
