using System;
using System.Globalization;

namespace W.Dm.Internal.Types;

internal static class DmTemporalInteger
{
    internal static void ValidateUserScale(int scale)
    {
        if (scale is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(scale));
    }

    internal static void ValidateUserPrecision(int precision)
    {
        if (precision != 0)
            throw new NotSupportedException("Temporal parameter Precision must be unset or zero; use Scale.");
    }

    internal static void ValidateFractionalTicks(long ticks, int scale)
    {
        ValidateUserScale(scale);
        if (scale < 7 && ticks % PowerOfTen(7 - scale) != 0)
            throw new OverflowException("Temporal value exceeds the declared fractional precision.");
    }

    // scale here is an already decoded semantic server scale, never raw packed metadata.
    internal static void ValidateWireFraction(int nanoseconds, int scale)
    {
        if (scale is < 0 or > 9)
            throw new OverflowException("Server temporal scale is invalid.");
        if (nanoseconds is < 0 or >= 1_000_000_000 || nanoseconds % PowerOfTen(9 - scale) != 0)
            throw new OverflowException("Server temporal fraction exceeds the declared range or precision.");
    }

    internal static int PowerOfTen(int exponent)
    {
        int value = 1;
        for (int index = 0; index < exponent; index++) value = checked(value * 10);
        return value;
    }

    internal static void ValidateDate(DmTemporalFields fields)
    {
        // Gregorian fields without the historical 1582-10-05..14 rewrite. Year zero is not a date.
        if (fields.Year == 0 || fields.Month is < 1 or > 12 || fields.Day < 1)
            throw new OverflowException("Temporal calendar fields are invalid.");
        bool leap = fields.Year % 4 == 0 && (fields.Year % 100 != 0 || fields.Year % 400 == 0);
        int days = fields.Month switch { 2 => leap ? 29 : 28, 4 or 6 or 9 or 11 => 30, _ => 31 };
        if (fields.Day > days) throw new OverflowException("Temporal calendar fields are invalid.");
    }

    internal static void ValidateTime(DmTemporalFields fields)
    {
        if (fields.Hour is < 0 or > 24 || fields.Minute is < 0 or > 59 || fields.Second is < 0 or > 59
            || fields.NanosecondsWithinSecond is < 0 or >= 1_000_000_000
            || (fields.Hour == 24 && (fields.Minute != 0 || fields.Second != 0 || fields.NanosecondsWithinSecond != 0)))
            throw new OverflowException("Temporal clock fields are invalid.");
    }

    internal static DmTemporalFields FromDateTime(DateTime value, int? offsetMinutes = null) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second,
            checked((int)(value.Ticks % TimeSpan.TicksPerSecond) * 100), offsetMinutes);

    internal static DmTemporalFields FromDateTimeOffset(DateTimeOffset value) =>
        FromDateTime(value.DateTime, checked((int)(value.Offset.Ticks / TimeSpan.TicksPerMinute)));

    internal static DmTemporalFields FromTimeOnly(TimeOnly value) =>
        new(0, 0, 0, value.Hour, value.Minute, value.Second,
            checked((int)(value.Ticks % TimeSpan.TicksPerSecond) * 100));

    internal static DmTemporalFields FromDateOnly(DateOnly value) => new(value.Year, value.Month, value.Day, 0, 0, 0, 0);

    internal static int FractionalTicksExact(int nanoseconds)
    {
        ValidateWireFraction(nanoseconds, 9);
        if (nanoseconds % 100 != 0) throw new OverflowException("Temporal fraction is outside .NET tick precision.");
        return nanoseconds / 100;
    }

    internal static DateTime ProjectDateTime(DmTemporalFields fields)
    {
        ValidateDate(fields);
        ValidateTime(fields);
        if (fields.Year is < 1 or > 9999 || fields.Hour == 24)
            throw new OverflowException("Temporal value is outside the .NET calendar or clock range.");
        return new DateTime(fields.Year, fields.Month, fields.Day, fields.Hour, fields.Minute, fields.Second,
            DateTimeKind.Unspecified).AddTicks(FractionalTicksExact(fields.NanosecondsWithinSecond));
    }

    internal static DateOnly ProjectDateOnly(DmTemporalFields fields)
    {
        ValidateDate(fields);
        if (fields.Year is < 1 or > 9999) throw new OverflowException("Temporal value is outside the .NET calendar range.");
        return new DateOnly(fields.Year, fields.Month, fields.Day);
    }

    internal static TimeSpan ProjectTimeSpanExact(DmTemporalFields fields)
    {
        ValidateTime(fields);
        if (fields.Hour == 24) throw new OverflowException("TIME hour 24 is outside the .NET clock range.");
        long ticks = fields.Hour * TimeSpan.TicksPerHour + fields.Minute * TimeSpan.TicksPerMinute
            + fields.Second * TimeSpan.TicksPerSecond + FractionalTicksExact(fields.NanosecondsWithinSecond);
        return TimeSpan.FromTicks(ticks);
    }

    internal static TimeOnly ProjectTimeOnly(DmTemporalFields fields) => new(ProjectTimeSpanExact(fields).Ticks);

    internal static DateTime ProjectTimeDateTime(DmTemporalFields fields) =>
        new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddTicks(ProjectTimeSpanExact(fields).Ticks);

    internal static DateTimeOffset ProjectDateTimeOffset(DmTemporalFields fields)
    {
        if (fields.OffsetMinutes is not int offset)
            throw new InvalidCastException("Temporal value has no timezone offset.");
        if (offset is < -840 or > 840) throw new OverflowException("Temporal offset is outside the .NET range.");
        DateTime local = ProjectDateTime(fields);
        long offsetTicks = offset * TimeSpan.TicksPerMinute;
        long utcTicks = checked(local.Ticks - offsetTicks);
        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
            throw new OverflowException("Temporal UTC value is outside the .NET calendar range.");
        return new DateTimeOffset(local, TimeSpan.FromTicks(offsetTicks));
    }

    internal static DateTimeOffset ProjectTimeOffset(DmTemporalFields fields) =>
        ProjectDateTimeOffset(fields with { Year = 1900, Month = 1, Day = 1 });

    internal static string FormatExact(DmTemporalFields fields, int scale, DmTemporalTextKind kind)
    {
        ValidateWireFraction(fields.NanosecondsWithinSecond, scale);
        if (kind is not (DmTemporalTextKind.Date or DmTemporalTextKind.Time or DmTemporalTextKind.Timestamp))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind != DmTemporalTextKind.Time) ValidateDate(fields);
        if (kind != DmTemporalTextKind.Date) ValidateTime(fields);
        string date = kind == DmTemporalTextKind.Time ? "" :
            fields.Year.ToString("D4", CultureInfo.InvariantCulture) + "-" + fields.Month.ToString("D2", CultureInfo.InvariantCulture)
            + "-" + fields.Day.ToString("D2", CultureInfo.InvariantCulture);
        if (kind == DmTemporalTextKind.Date)
        {
            if (fields.Hour != 0 || fields.Minute != 0 || fields.Second != 0 || fields.NanosecondsWithinSecond != 0 || fields.OffsetMinutes.HasValue)
                throw new OverflowException("DATE cannot carry time or offset components.");
            return date;
        }
        string time = fields.Hour.ToString("D2", CultureInfo.InvariantCulture) + ":" + fields.Minute.ToString("D2", CultureInfo.InvariantCulture)
            + ":" + fields.Second.ToString("D2", CultureInfo.InvariantCulture);
        if (scale != 0) time += "." + fields.NanosecondsWithinSecond.ToString("D9", CultureInfo.InvariantCulture)[..scale];
        string text = kind == DmTemporalTextKind.Time ? time : date + " " + time;
        if (fields.OffsetMinutes is int offset)
        {
            if (offset is < short.MinValue or > short.MaxValue) throw new OverflowException("Temporal offset is outside the wire range.");
            int magnitude = offset < 0 ? -offset : offset;
            text += (offset < 0 ? " -" : " +") + (magnitude / 60).ToString("D2", CultureInfo.InvariantCulture)
                + ":" + (magnitude % 60).ToString("D2", CultureInfo.InvariantCulture);
        }
        return text;
    }
}
