using System;
using System.Buffers.Binary;

namespace W.Dm.Internal.Types;

// Ordinary packed wire only. Legacy helper buffers and BDTA have different contracts.
internal static class DmTemporalWireCodec
{
    internal static int GetLength(DmTemporalWireLayout layout) => layout switch
    {
        DmTemporalWireLayout.Date3 => 3,
        DmTemporalWireLayout.Time5 => 5,
        DmTemporalWireLayout.TimeTz7 => 7,
        DmTemporalWireLayout.Timestamp8 => 8,
        DmTemporalWireLayout.Timestamp9 => 9,
        DmTemporalWireLayout.TimestampTz10 => 10,
        DmTemporalWireLayout.TimestampTz11 => 11,
        _ => throw new NotSupportedException("Temporal wire layout is unknown.")
    };

    internal static DmTemporalWireLayout ForCType(int cType) => cType switch
    {
        14 => DmTemporalWireLayout.Date3, 15 => DmTemporalWireLayout.Time5,
        22 => DmTemporalWireLayout.TimeTz7, 16 => DmTemporalWireLayout.Timestamp8,
        23 => DmTemporalWireLayout.TimestampTz10, 26 => DmTemporalWireLayout.Timestamp9,
        27 => DmTemporalWireLayout.TimestampTz11,
        _ => throw new NotSupportedException("Temporal type has no ordinary packed wire layout.")
    };

    private static bool IsTime(DmTemporalWireLayout layout) => layout is DmTemporalWireLayout.Time5 or DmTemporalWireLayout.TimeTz7;
    private static bool HasOffset(DmTemporalWireLayout layout) => layout is DmTemporalWireLayout.TimeTz7
        or DmTemporalWireLayout.TimestampTz10 or DmTemporalWireLayout.TimestampTz11;
    private static bool IsExtended(DmTemporalWireLayout layout) => layout is DmTemporalWireLayout.Timestamp9 or DmTemporalWireLayout.TimestampTz11;

    internal static void ValidateFields(DmTemporalFields fields, DmTemporalWireLayout layout, int scale)
    {
        GetLength(layout);
        DmTemporalInteger.ValidateWireFraction(fields.NanosecondsWithinSecond, scale);
        if (IsTime(layout))
        {
            if (fields.Year != 0 || fields.Month != 0 || fields.Day != 0)
                throw new OverflowException("TIME cannot carry calendar fields.");
        }
        else
        {
            DmTemporalInteger.ValidateDate(fields);
            // Above 9999, the 15-bit packed year is the signed negative-year representation.
            if (fields.Year is < -22768 or > 9999)
                throw new OverflowException("Temporal year is outside the packed wire range.");
        }
        DmTemporalInteger.ValidateTime(fields);
        if (layout == DmTemporalWireLayout.Date3)
        {
            if (scale != 0 || fields.Hour != 0 || fields.Minute != 0 || fields.Second != 0 || fields.NanosecondsWithinSecond != 0)
                throw new OverflowException("DATE cannot carry a time component or fractional scale.");
        }
        else if (!IsExtended(layout) && scale > 6)
            throw new NotSupportedException("Legacy temporal wire has microsecond precision.");
        if (HasOffset(layout) != fields.OffsetMinutes.HasValue)
            throw new InvalidCastException("Temporal offset does not match the wire layout.");
        if (fields.OffsetMinutes is int offset && offset is < short.MinValue or > short.MaxValue)
            throw new OverflowException("Temporal offset is outside the packed wire range.");
    }

    internal static DmTemporalFields DecodeRaw(ReadOnlySpan<byte> bytes, DmTemporalWireLayout layout, int scale)
    {
        int length = GetLength(layout);
        if (bytes.Length != length) throw new ArgumentException("Temporal wire length does not match its layout.", nameof(bytes));
        bool time = IsTime(layout);
        int year = 0, month = 0, day = 0;
        if (!time)
        {
            year = BinaryPrimitives.ReadUInt16LittleEndian(bytes) & 0x7fff;
            if (year > 9999) year -= 32768;
            month = (bytes[1] >> 7) | ((bytes[2] & 7) << 1);
            day = bytes[2] >> 3;
        }
        int hour = 0, minute = 0, second = 0, nanoseconds = 0;
        if (layout != DmTemporalWireLayout.Date3)
        {
            int start = time ? 0 : 3;
            hour = bytes[start] & 31;
            minute = (bytes[start] >> 5) | ((bytes[start + 1] & 7) << 3);
            second = (bytes[start + 1] >> 3) | ((bytes[start + 2] & 1) << 5);
            int fraction = (bytes[start + 2] >> 1) | (bytes[start + 3] << 7);
            if (IsExtended(layout))
            {
                if ((bytes[start + 5] & 0x80) != 0) throw new OverflowException("Temporal wire contains reserved fraction bits.");
                fraction |= (bytes[start + 4] << 15) | (bytes[start + 5] << 23);
                nanoseconds = fraction;
            }
            else
            {
                if ((bytes[start + 4] & 0xe0) != 0) throw new OverflowException("Temporal wire contains reserved fraction bits.");
                fraction |= bytes[start + 4] << 15;
                if (fraction > 999999) throw new OverflowException("Legacy temporal fraction is outside the microsecond range.");
                nanoseconds = checked(fraction * 1000);
            }
        }
        int? offset = HasOffset(layout) ? BinaryPrimitives.ReadInt16LittleEndian(bytes[(length - 2)..]) : null;
        var fields = new DmTemporalFields(year, month, day, hour, minute, second, nanoseconds, offset);
        ValidateFields(fields, layout, scale);
        return fields;
    }

    internal static byte[] EncodeExact(DmTemporalFields fields, DmTemporalWireLayout layout, int scale)
    {
        ValidateFields(fields, layout, scale);
        var bytes = new byte[GetLength(layout)];
        bool time = IsTime(layout);
        if (!time)
        {
            int year = fields.Year & 0x7fff;
            bytes[0] = (byte)year;
            bytes[1] = (byte)((year >> 8) | ((fields.Month & 1) << 7));
            bytes[2] = (byte)((fields.Month >> 1) | (fields.Day << 3));
        }
        if (layout != DmTemporalWireLayout.Date3)
        {
            int start = time ? 0 : 3;
            int fraction = fields.NanosecondsWithinSecond;
            if (!IsExtended(layout)) fraction /= 1000; // Declared scale <=6 was checked above.
            bytes[start] = (byte)(fields.Hour | ((fields.Minute & 7) << 5));
            bytes[start + 1] = (byte)((fields.Minute >> 3) | ((fields.Second & 31) << 3));
            bytes[start + 2] = (byte)((fields.Second >> 5) | ((fraction & 127) << 1));
            bytes[start + 3] = (byte)(fraction >> 7);
            bytes[start + 4] = (byte)(fraction >> 15);
            if (IsExtended(layout)) bytes[start + 5] = (byte)(fraction >> 23);
        }
        if (HasOffset(layout)) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(bytes.Length - 2), checked((short)fields.OffsetMinutes.Value));
        return bytes;
    }

    // Explicit public-helper adapter: [7..9] are microseconds and [10..11] offset minutes.
    // TIME helper buffers retain their historical 1900-01-01 date anchor; typed TIME fields have no date.
    internal static DmTemporalFields FromLegacyMicroseconds(ReadOnlySpan<byte> bytes, DmTemporalTextKind kind, bool hasOffset)
    {
        if (bytes.Length != 12) throw new ArgumentException("Legacy temporal helper buffer must contain 12 bytes.", nameof(bytes));
        int microseconds = bytes[7] | (bytes[8] << 8) | (bytes[9] << 16);
        if (microseconds > 999999) throw new OverflowException("Legacy temporal fraction is outside the microsecond range.");
        if (!hasOffset && BinaryPrimitives.ReadInt16LittleEndian(bytes[10..]) != 0)
            throw new OverflowException("Non-timezone temporal helper buffer cannot carry an offset.");
        var fields = new DmTemporalFields(BinaryPrimitives.ReadInt16LittleEndian(bytes), bytes[2], bytes[3],
            bytes[4], bytes[5], bytes[6], checked(microseconds * 1000),
            hasOffset ? BinaryPrimitives.ReadInt16LittleEndian(bytes[10..]) : null);
        ValidateLegacyKind(fields, kind);
        return kind == DmTemporalTextKind.Time ? fields with { Year = 0, Month = 0, Day = 0 } : fields;
    }

    internal static byte[] ToLegacyMicroseconds(DmTemporalFields fields, DmTemporalTextKind kind)
    {
        ValidateLegacyKind(fields, kind);
        DmTemporalInteger.ValidateWireFraction(fields.NanosecondsWithinSecond, 6);
        if (fields.Year is < short.MinValue or > short.MaxValue)
            throw new OverflowException("Temporal year is outside the legacy helper range.");
        if (fields.OffsetMinutes is int offset && offset is < short.MinValue or > short.MaxValue)
            throw new OverflowException("Temporal offset is outside the legacy helper range.");
        var bytes = new byte[12];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, (short)(kind == DmTemporalTextKind.Time ? 1900 : fields.Year));
        bytes[2] = (byte)(kind == DmTemporalTextKind.Time ? 1 : fields.Month);
        bytes[3] = (byte)(kind == DmTemporalTextKind.Time ? 1 : fields.Day);
        bytes[4] = (byte)fields.Hour; bytes[5] = (byte)fields.Minute; bytes[6] = (byte)fields.Second;
        int microseconds = fields.NanosecondsWithinSecond / 1000;
        bytes[7] = (byte)microseconds; bytes[8] = (byte)(microseconds >> 8); bytes[9] = (byte)(microseconds >> 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(10), checked((short)(fields.OffsetMinutes ?? 0)));
        return bytes;
    }

    private static void ValidateLegacyKind(DmTemporalFields fields, DmTemporalTextKind kind)
    {
        if (kind is not (DmTemporalTextKind.Date or DmTemporalTextKind.Time or DmTemporalTextKind.Timestamp))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind != DmTemporalTextKind.Time || fields.Year != 0 || fields.Month != 0 || fields.Day != 0)
            DmTemporalInteger.ValidateDate(fields);
        DmTemporalInteger.ValidateTime(fields);
        if (kind == DmTemporalTextKind.Date && (fields.Hour != 0 || fields.Minute != 0 || fields.Second != 0 || fields.NanosecondsWithinSecond != 0 || fields.OffsetMinutes.HasValue))
            throw new OverflowException("DATE cannot carry time or offset components.");
    }
}
