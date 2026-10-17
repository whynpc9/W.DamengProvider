using System;
using System.Globalization;

namespace W.Dm.Internal.Types;

internal static class DmTemporalCodec
{
    // Production entry: negotiated identity and independent target constraints are captured per call.
    // The version-only overload below is retained solely for existing internal compatibility consumers.
    internal static object NormalizeForWrite(object value, int cType, int scale,
        DmTemporalCapabilityProfile profile, DmTemporalWriteTarget? target)
    {
        if (target is not DmTemporalWriteTarget temporal)
            return NormalizeForWire(value, cType, scale);
        profile.ValidateWriteTarget(temporal); // NULL never bypasses a closed target.
        if (value == null || value is DBNull) return value;
        DmTemporalFields fields;
        object normalized = value;
        switch (value)
        {
#if !NETSTANDARD2_1
            case DateOnly date:
                if (cType != 14) throw new NotSupportedException("DateOnly requires DATE.");
                fields = DmTemporalInteger.FromDateOnly(date);
                normalized = date.ToDateTime(TimeOnly.MinValue);
                break;
            case TimeOnly time:
                if (cType != 15) throw new NotSupportedException("TimeOnly requires TIME.");
                fields = DmTemporalInteger.FromTimeOnly(time);
                normalized = DateTime.MinValue.AddTicks(time.Ticks);
                break;
#endif
            case DateTime dateTime:
                if (cType is not (14 or 15 or 16 or 26)) throw new NotSupportedException("DateTime requires a non-timezone temporal type.");
                fields = DmTemporalInteger.FromDateTime(dateTime);
                if (cType == 15)
                {
                    if (dateTime.Date != DateTime.MinValue.Date) throw new NotSupportedException("Use TimeOnly for TIME without a date component.");
                    fields = fields with { Year = 0, Month = 0, Day = 0 };
                }
                break;
            case DateTimeOffset offset:
                if (cType is not (22 or 23 or 27)) throw new NotSupportedException("DateTimeOffset requires a timezone-aware temporal type.");
                fields = DmTemporalInteger.FromDateTimeOffset(offset);
                if (cType == 22) fields = fields with { Year = 0, Month = 0, Day = 0 };
                break;
            case TimeSpan:
                throw new NotSupportedException("TimeSpan requires INTERVAL DAY TO SECOND.");
            default:
                // Semantic text is parsed and validated at the same gate in EncodeWithTarget.
                return value;
        }
        profile.ValidateWriteValue(temporal, fields);
        return normalized;
    }

    private static readonly long[] TickQuantumByScale =
        { 10_000_000, 1_000_000, 100_000, 10_000, 1_000, 100, 10, 1 };

    // The legacy 8/10-byte timestamp and TIME encoders store microseconds.
    // Extended 9/11-byte seven-digit types passed the T12 O-seed/W-read profile
    // on 8.1.5.60; other server versions retain the seven-digit gate.
    internal static object NormalizeForWire(object value, int cType, int scale, string serverVersion = null)
    {
        if (value == null || value is DBNull) return value;
        if (IsText(cType)) return value switch
        {
#if !NETSTANDARD2_1
            DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
#endif
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),
            TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
            _ => value
        };

        switch (value)
        {
#if !NETSTANDARD2_1
            case DateOnly date:
                if (cType != 14) throw new NotSupportedException("DateOnly requires a DATE parameter.");
                return date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            case TimeOnly time:
                if (cType != 15) throw new NotSupportedException("TimeOnly requires a TIME parameter.");
                ValidateFraction(time.Ticks, scale, 6);
                return DateTime.MinValue.Date.AddTicks(time.Ticks);
#endif
            case DateTime dateTime:
                ValidateDateTime(dateTime, cType, scale, serverVersion);
                return dateTime;
			case DateTimeOffset offset:
				if (cType is not (22 or 23 or 27))
					throw new NotSupportedException("DateTimeOffset requires a timezone-aware temporal parameter.");
				if (offset.Offset.TotalMinutes <= -780 || offset.Offset.TotalMinutes > 840)
					throw new OverflowException("Timezone offset is outside the server temporal range.");
				ValidateFraction(offset.Ticks, scale, cType == 27 ? 7 : 6, serverVersion);
                return offset;
            case TimeSpan span:
                if (cType != 21) throw new NotSupportedException("TimeSpan requires INTERVAL DAY TO SECOND.");
                int secondsScale = scale & 0xF;
                ValidateFraction(span.Ticks, secondsScale, 6);
                return span;
            default:
                return value;
        }
    }

    private static void ValidateDateTime(DateTime value, int cType, int scale, string serverVersion)
    {
        switch (cType)
        {
            case 14:
                if (value.TimeOfDay != TimeSpan.Zero)
                    throw new OverflowException("DATE cannot represent the supplied time component.");
                return;
            case 15:
                if (value.Date != DateTime.MinValue.Date)
                    throw new NotSupportedException("Use TimeOnly for a TIME parameter without a date component.");
                ValidateFraction(value.Ticks, scale, 6);
                return;
            case 16:
                ValidateFraction(value.Ticks, scale, 6);
                return;
            case 26:
                ValidateFraction(value.Ticks, scale, 7, serverVersion);
                return;
            default:
                throw new NotSupportedException("DateTime requires a non-timezone DATE, TIME, or TIMESTAMP parameter.");
        }
    }

    private static void ValidateFraction(long ticks, int encodedScale, int wireMaxScale, string serverVersion = null)
    {
        int digits = encodedScale > 9 ? encodedScale & 0xF : encodedScale;
        if (digits < 0 || digits > wireMaxScale || digits > 7)
            throw new NotSupportedException("Temporal fractional precision is unsupported by this wire type.");
        if (digits == 7 && wireMaxScale == 7 && !string.Equals(serverVersion, "8.1.5.60", StringComparison.Ordinal))
            throw new NotSupportedException("Seven-digit timestamp precision is not verified for this server profile.");
        if (ticks % TickQuantumByScale[digits] != 0)
            throw new OverflowException("Temporal value cannot be represented at the declared scale.");
    }

    internal static void ValidateWireNanoseconds(int nanoseconds, int encodedScale)
    {
        int digits = encodedScale > 9 ? encodedScale & 0xF : encodedScale;
        if (digits < 0 || digits > 9 || nanoseconds < 0 || nanoseconds >= 1_000_000_000)
            throw new OverflowException("Server temporal fraction is outside the declared range.");
        int quantum = 1;
        for (int index = digits; index < 9; index++) quantum = checked(quantum * 10);
        if (nanoseconds % quantum != 0)
            throw new OverflowException("Server temporal fraction exceeds the declared scale.");
    }

    private static bool IsText(int cType) => cType is 0 or 1 or 2 or 19 or 54;
}
