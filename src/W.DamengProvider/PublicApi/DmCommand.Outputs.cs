using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Types;
using Statement = W.Dm.Internal.Legacy.A.A;

namespace W.Dm;

public partial class DmCommand
{
    private sealed record ProcedureOutput(DmParameter Parameter, DmParameterInternal Descriptor,
        int Ordinal, byte[] Bytes);

    private sealed record ProcedureOutputResponse(DmCommandPlan Plan, Statement Statement,
        DmField[] Fields, ProcedureOutput[] Values);

    private ProcedureOutputResponse pendingProcedureOutputs;

    internal void ResetProcedureOutputCapture() => pendingProcedureOutputs = null;

    // Called inside response decoding. Never fetch a LOB or cursor here: the parent
    // response must finish validation and release its wire exchange first.
    internal void CaptureProcedureOutputs(b buffer, Statement statement, int outputCount)
    {
        DmCommandPlan plan = ActivePlan;
        try
        {
            if (plan == null || plan.CommandType != CommandType.StoredProcedure)
                throw new NotSupportedException("Output parameters require StoredProcedure command type.");
            if (pendingProcedureOutputs != null)
                throw new InvalidDataException("An output response is already pending materialization.");
            statement.H().GetParamsInfo(out DmParameterInternal[] descriptors);
            int parameterCount = statement.H().GetParameterCount();
            plan.Binding.ValidateServerCount(parameterCount);
            if (descriptors == null || descriptors.Length < parameterCount)
                throw new InvalidDataException("Output parameter metadata is incomplete.");
            var slots = new List<(DmParameter Parameter, DmParameterInternal Descriptor, int Ordinal)>();
            var identities = new HashSet<DmParameter>();
            for (int i = 0; i < parameterCount; i++)
            {
                DmParameterInternal descriptor = descriptors[i];
                DmParameter parameter = plan.Binding.ResolveServerParameter(i, descriptor.GetName(), parameterCount);
                if (parameter.Direction == ParameterDirection.Input) continue;
                if (!identities.Add(parameter))
                    throw new InvalidDataException("Duplicate output parameter identity.");
                bool registeredCursor = descriptor.GetCType() == 120 && parameter.Direction == ParameterDirection.Output && descriptor.GetInOutType() == 2;
                if (!registeredCursor && descriptor.GetInOutType() != (parameter.Direction == ParameterDirection.InputOutput ? 2 : 1))
                    throw new InvalidDataException("Output direction conflicts with server metadata.");
                ValidateProcedureOutputType(descriptor);
                slots.Add((parameter, descriptor, i));
            }
            byte[][] values = ReadProcedureOutputPayloads(buffer, statement.G().ConnProperty.msgVersion,
                outputCount, slots.Count);
            var captured = new ProcedureOutput[slots.Count];
            for (int i = 0; i < captured.Length; i++)
                captured[i] = new(slots[i].Parameter, slots[i].Descriptor, slots[i].Ordinal, values[i]);
            // Parameters are the correct metadata for output temporal and LOB conversion.
            // Result-set columns may be absent or describe an unrelated rowset.
            var fields = new DmField[parameterCount];
            for (int i = 0; i < fields.Length; i++) fields[i] = descriptors[i];
            pendingProcedureOutputs = new(plan, statement, fields, captured);
        }
        catch
        {
            pendingProcedureOutputs = null;
            plan?.FailOutputs();
            throw;
        }
    }

    internal static byte[][] ReadProcedureOutputPayloads(b buffer, int messageVersion,
        int outputCount, int expectedCount)
    {
        if (outputCount < 0 || expectedCount < 0 || outputCount != expectedCount)
            throw new InvalidDataException("Output count conflicts with parameter metadata.");
        if (messageVersion < 12)
            throw new NotSupportedException("Legacy output parameter framing is not verified.");
        var values = new byte[expectedCount][];
        for (int i = 0; i < values.Length; i++)
        {
            // Only one scalar per output slot is established. Do not mask flags or
            // silently select the first element of an unverified array response.
            if (buffer.d() != 1)
                throw new NotSupportedException("Only single scalar output parameter values are supported.");
            int length = buffer.D();
            if (length == 65534) values[i] = null;
            else if (length >= 65533)
                throw new NotSupportedException("Output parameter length marker is not verified.");
            else values[i] = buffer.F(length); // copies bytes and checks the received frame boundary
        }
        return values;
    }

    private static void ValidateProcedureOutputType(DmParameterInternal descriptor)
    {
        if (descriptor.GetCType() is not (0 or 1 or 2 or 3 or 5 or 6 or 7 or 8 or 9 or 10 or 11
            or 12 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 or 26 or 27 or 120)
            || DmSqlType.isComplexType(descriptor.GetCType(), descriptor.GetScale()))
            throw new NotSupportedException("Output parameter type is not supported.");
    }

    internal void MaterializeProcedureOutputs(Statement statement)
    {
        ProcedureOutputResponse response = TakeProcedureOutputs(statement);
        if (response == null) return;
        try
        {
            DmGetValue getter = CreateProcedureOutputGetter(response);
            var converted = new object[response.Values.Length];
            for (int i = 0; i < converted.Length; i++)
            {
                ProcedureOutput output = response.Values[i];
                converted[i] = output.Descriptor.GetCType() == 120
                    ? MaterializeProcedureCursor(output.Parameter, statement, output.Descriptor, output.Bytes)
                    : ConvertProcedureOutput(getter, output.Parameter, output.Descriptor, output.Ordinal, output.Bytes);
            }
            for (int i = 0; i < converted.Length; i++)
                converted[i] = ValidateProcedureOutputValue(response.Values[i].Parameter, converted[i]);
            for (int i = 0; i < converted.Length; i++) response.Plan.StageOutput(response.Values[i].Parameter, converted[i]);
        }
        catch { response.Plan.FailOutputs(); throw; }
    }

    internal async Task MaterializeProcedureOutputsAsync(Statement statement, CancellationToken token)
    {
        ProcedureOutputResponse response = TakeProcedureOutputs(statement);
        if (response == null) return;
        try
        {
            DmGetValue getter = CreateProcedureOutputGetter(response);
            var converted = new object[response.Values.Length];
            for (int i = 0; i < converted.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                ProcedureOutput output = response.Values[i];
                int type = output.Descriptor.GetCType();
                converted[i] = type == 120
                    ? await MaterializeProcedureCursorAsync(output.Parameter, statement, output.Descriptor, output.Bytes, token).ConfigureAwait(false)
                    : output.Bytes == null ? DBNull.Value
                    : type == 12 ? await getter.GetBytesAsync(output.Ordinal, output.Bytes, type,
                        output.Descriptor.GetPrecision(), output.Descriptor.GetScale(), token).ConfigureAwait(false)
                    : type == 19 ? await getter.GetStringAsync(output.Ordinal, output.Bytes, type,
                        output.Descriptor.GetPrecision(), output.Descriptor.GetScale(), token).ConfigureAwait(false)
                    : ConvertProcedureOutput(getter, output.Parameter, output.Descriptor, output.Ordinal, output.Bytes);
            }
            for (int i = 0; i < converted.Length; i++)
                converted[i] = ValidateProcedureOutputValue(response.Values[i].Parameter, converted[i]);
            for (int i = 0; i < converted.Length; i++) response.Plan.StageOutput(response.Values[i].Parameter, converted[i]);
        }
        catch { response.Plan.FailOutputs(); throw; }
    }

    private ProcedureOutputResponse TakeProcedureOutputs(Statement statement)
    {
        ProcedureOutputResponse response = pendingProcedureOutputs;
        pendingProcedureOutputs = null;
        if (response == null) return null;
        if (!ReferenceEquals(response.Plan, ActivePlan) || !ReferenceEquals(response.Statement, statement))
        {
            response.Plan.FailOutputs();
            ActivePlan?.FailOutputs();
            throw new InvalidOperationException("Output response belongs to another execution.");
        }
        return response;
    }

    private static DmGetValue CreateProcedureOutputGetter(ProcedureOutputResponse response) =>
        new(response.Statement.G().ConnProperty.ServerEncoding, response.Statement,
            response.Statement.G().ConnProperty.NewLobFlag, response.Fields, DmTemporalResultFormat.OrdinaryPacked);

    internal static object ValidateProcedureOutputValue(DmParameter parameter, object value)
    {
        if (parameter is null) throw new ArgumentNullException(nameof(parameter));
        if (value is null or DBNull) return DBNull.Value;
        if (parameter.m_SetSizeFlag && parameter.do_Size > 0)
        {
            int length = value switch { string text => text.Length, byte[] bytes => bytes.Length, _ => 0 };
            if (length > parameter.do_Size)
                throw new OverflowException("Output parameter value exceeds its declared Size.");
        }
        if (value is decimal or DmDecimal && (parameter.m_SetPrecFlag || parameter.m_SetScaleFlag))
        {
            DmDecimal exact = value is DmDecimal large ? large : DmDecimal.FromDecimal((decimal)value);
            int precision = parameter.m_SetPrecFlag ? parameter.do_Precision : 0;
            int scale = parameter.m_SetScaleFlag ? parameter.do_Scale : -1;
            // Reuse the existing exact declaration checks, including precision,
            // scale and range. This does not send or round the encoded value.
            _ = DmNumericCodec.EncodeDecimal(exact, precision, scale);
            if (value is DmDecimal && scale >= 0) return exact.RescaleExact(scale);
        }
        if (parameter.m_SetScaleFlag && value is DmIntervalDT intervalValue)
        {
            if (parameter.do_Scale > 6)
                throw new NotSupportedException("Interval output fractional scale exceeds the verified microsecond precision.");
            // Preserve the described subtype and leading precision; only the
            // requested fractional scale changes. convertTo is exact and checks
            // both precision loss and the supported interval scale range.
            int packed = (intervalValue.getScaleForSvr() & ~0xF) | parameter.do_Scale;
            return intervalValue.convertTo(packed);
        }
        if (parameter.m_SetScaleFlag && value is DateTime or DateTimeOffset or TimeSpan)
        {
            int scale = parameter.do_Scale;
            if (scale > 9) throw new ArgumentOutOfRangeException(nameof(parameter.Scale));
            if (scale < 7)
            {
                long ticks = value switch
                {
                    DateTime timestamp => timestamp.Ticks,
                    DateTimeOffset timestamp => timestamp.Ticks,
                    TimeSpan interval => interval.Ticks,
                    _ => 0
                };
                long quantum = TimeSpan.TicksPerSecond;
                for (int digit = 0; digit < scale; digit++) quantum /= 10;
                if (ticks % quantum != 0)
                    throw new OverflowException("Output parameter Scale would discard nonzero fractional digits.");
            }
        }
        return value;
    }

    internal static object ConvertProcedureOutput(DmGetValue getter, DmParameter parameter,
        DmParameterInternal descriptor, int ordinal, byte[] bytes)
    {
        ValidateProcedureOutputType(descriptor);
        if (bytes == null) return DBNull.Value;
        int type = descriptor.GetCType(), precision = descriptor.GetPrecision(), scale = descriptor.GetScale();
        int fixedWidth = type switch { 3 or 5 => 1, 6 => 2, 7 or 10 => 4, 8 or 11 or 24 => 8, _ => 0 };
        if (fixedWidth != 0 && bytes.Length != fixedWidth)
            throw new InvalidDataException("Output scalar length conflicts with its server type.");
        // LOB outputs are always materialized, never published as stale locators.
        if (type == 12) return getter.GetBytes(ordinal, bytes, type, precision, scale);
        if (type == 19) return getter.GetString(ordinal, bytes, type, precision, scale);
        DmDbType requested = parameter.HasExplicitDmSqlType ? parameter.DeclaredDmSqlType
            : parameter.HasExplicitDbType ? DmSqlType.DbTypeToDmSqlType(parameter.DeclaredDbType)
            : parameter.ResolveType(descriptor).Type;
        return requested switch
        {
            DmDbType.Bit => getter.GetBoolean(ordinal, bytes, type, precision, scale),
            DmDbType.Byte => getter.GetByte(ordinal, bytes, type, precision, scale),
            DmDbType.SByte => getter.GetSByte(ordinal, bytes, type, precision, scale),
            DmDbType.Int16 => getter.GetShort(ordinal, bytes, type, precision, scale),
            DmDbType.Int32 => getter.GetInt(ordinal, bytes, type, precision, scale),
            DmDbType.Int64 => getter.GetLong(ordinal, bytes, type, precision, scale),
            DmDbType.UInt16 => getter.GetUshort(ordinal, bytes, type, precision, scale),
            DmDbType.UInt32 => getter.GetUint(ordinal, bytes, type, precision, scale),
            DmDbType.UInt64 => getter.GetUlong(ordinal, bytes, type, precision, scale),
            DmDbType.Decimal => getter.GetBigDecimal(ordinal, bytes, type, precision, scale),
            DmDbType.XDEC => type switch
            {
                9 => DmNumericCodec.DecodeDecimal(bytes, precision > 0 && scale >= 0 ? scale : null),
                24 => DmNumericCodec.DecodeScaledInt64(bytes, scale),
                _ => throw new InvalidCastException("Exact output requires a DECIMAL server type.")
            },
            DmDbType.Float => getter.GetFloat(ordinal, bytes, type, precision, scale),
            DmDbType.Double => getter.GetDouble(ordinal, bytes, type, precision, scale),
            DmDbType.Char or DmDbType.VarChar or DmDbType.Text => getter.GetString(ordinal, bytes, type, precision, scale),
            DmDbType.Binary or DmDbType.VarBinary => getter.GetBytes(ordinal, bytes, type, precision, scale),
            DmDbType.Date => getter.GetDate(ordinal, bytes, type, precision, scale),
            DmDbType.Time => type == 22 ? (object)getter.GetTimeTZ(ordinal, bytes, type, precision, scale)
                : getter.GetTime(ordinal, bytes, type, precision, scale),
            DmDbType.DateTime => getter.GetTimestamp(ordinal, bytes, type, precision, scale),
            DmDbType.DateTimeOffset => getter.GetTimestampTZ(ordinal, bytes, type, precision, scale),
            DmDbType.TimeOffset => getter.GetTimeTZ(ordinal, bytes, type, precision, scale),
            DmDbType.IntervalDayToSecond => getter.GetINTERVALDT(ordinal, bytes, type, precision, scale),
            DmDbType.IntervalYearToMonth => getter.GetINTERVALYM(ordinal, bytes, type, precision, scale),
            _ => throw new NotSupportedException("Output parameter conversion is not supported.")
        };
    }
}
