using System;
using System.Numerics;

namespace W.Dm.Internal.Types;

// Normalized DAY TO SECOND magnitudes plus a separate sign; months never enter this type.
internal readonly record struct DmTemporalIntervalFields(bool Negative, long Days, int Hours, int Minutes,
    int Seconds, int NanosecondsWithinSecond)
{
    internal static DmTemporalIntervalFields FromTimeSpanExact(TimeSpan value, int scale, int leadingPrecision)
    {
        DmTemporalInteger.ValidateFractionalTicks(value.Ticks, scale);
        BigInteger magnitude = new BigInteger(value.Ticks);
        bool negative = magnitude.Sign < 0;
        if (negative) magnitude = -magnitude;
        long days = checked((long)(magnitude / TimeSpan.TicksPerDay));
        magnitude %= TimeSpan.TicksPerDay;
        int hours = (int)(magnitude / TimeSpan.TicksPerHour);
        magnitude %= TimeSpan.TicksPerHour;
        int minutes = (int)(magnitude / TimeSpan.TicksPerMinute);
        magnitude %= TimeSpan.TicksPerMinute;
        int seconds = (int)(magnitude / TimeSpan.TicksPerSecond);
        int ns = checked((int)(magnitude % TimeSpan.TicksPerSecond) * 100);
        var fields = new DmTemporalIntervalFields(negative, days, hours, minutes, seconds, ns);
        fields.Validate(scale, leadingPrecision);
        return fields;
    }

    internal void Validate(int scale, int leadingPrecision)
    {
        if (leadingPrecision is < 1 or > 9) throw new ArgumentOutOfRangeException(nameof(leadingPrecision));
        if (Days < 0 || Days >= DmTemporalInteger.PowerOfTen(leadingPrecision) || Hours is < 0 or > 23
            || Minutes is < 0 or > 59 || Seconds is < 0 or > 59)
            throw new OverflowException("Temporal interval exceeds its component or leading precision range.");
        DmTemporalInteger.ValidateWireFraction(NanosecondsWithinSecond, scale);
    }

    internal TimeSpan ToTimeSpanExact(int scale, int leadingPrecision)
    {
        Validate(scale, leadingPrecision);
        int fractionalTicks = DmTemporalInteger.FractionalTicksExact(NanosecondsWithinSecond);
        BigInteger ticks = new BigInteger(Days) * TimeSpan.TicksPerDay + Hours * TimeSpan.TicksPerHour
            + Minutes * TimeSpan.TicksPerMinute + Seconds * TimeSpan.TicksPerSecond + fractionalTicks;
        if (Negative) ticks = -ticks;
        if (ticks < long.MinValue || ticks > long.MaxValue)
            throw new OverflowException("Temporal interval is outside the .NET TimeSpan range.");
        return TimeSpan.FromTicks((long)ticks);
    }
}
