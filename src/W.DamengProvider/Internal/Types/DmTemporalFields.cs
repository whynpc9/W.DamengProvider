namespace W.Dm.Internal.Types;

// Protocol/text fields. Fractions always have nanosecond units, never ticks or microseconds.
// TIME uses zero date fields; a timezone is present only for timezone-aware layouts.
internal readonly record struct DmTemporalFields(
    int Year, int Month, int Day, int Hour, int Minute, int Second,
    int NanosecondsWithinSecond, int? OffsetMinutes = null);

internal enum DmTemporalWireLayout
{
    Date3,
    Time5,
    TimeTz7,
    Timestamp8,
    Timestamp9,
    TimestampTz10,
    TimestampTz11
}

internal enum DmTemporalTextKind { Date, Time, Timestamp }
