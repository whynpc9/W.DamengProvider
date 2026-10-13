using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using W.Dm.util;

namespace W.Dm.Internal.Types;

// Per-call representation adapters. Never cache values from a publicly mutable array.
internal static class DmTemporalLegacyAdapter
{
    internal static bool IsTemporal(int type) => type is 14 or 15 or 16 or 22 or 23 or 26 or 27;
    internal static DmTemporalTextKind Kind(int type) => type switch
    {
        14 => DmTemporalTextKind.Date, 15 or 22 => DmTemporalTextKind.Time,
        16 or 23 or 26 or 27 => DmTemporalTextKind.Timestamp,
        _ => throw new InvalidCastException("Type has no temporal fields.")
    };
    internal static DmTemporalWireLayout PackedLayout(int length) => length switch
    {
        3 => DmTemporalWireLayout.Date3, 5 => DmTemporalWireLayout.Time5, 7 => DmTemporalWireLayout.TimeTz7,
        8 => DmTemporalWireLayout.Timestamp8, 9 => DmTemporalWireLayout.Timestamp9,
        10 => DmTemporalWireLayout.TimestampTz10, 11 => DmTemporalWireLayout.TimestampTz11,
        _ => throw new ArgumentException("Temporal packed length is unsupported.")
    };
    internal static int SemanticScale(int type, int scale) => type == 14 ? 0 : scale & ~0x3000;
    internal static int Mask(int scale, int mask)
    {
        if ((scale & 0x3000) == 0x3000) throw new NotSupportedException("Temporal metadata contains conflicting flags.");
        return (scale & 0x1000) != 0 ? 4 : (scale & 0x2000) != 0 ? 1 : mask;
    }
    internal static DmTemporalCapabilityProfile Profile(DmConnection connection)
    {
        var property = connection?.GetConnInstance()?.ConnProperty;
        bool encodingOverrideEmpty = property?.property != null
            && property.property.TryGetValue(DmConst.PROP_KEY_ENCODING, out object encodingOverride)
            && encodingOverride is string text && text.Length == 0;
        // Only an explicitly empty override authorizes reading the negotiated encoding getter.
        // Missing/non-string keys must not reach its dictionary indexer or ToString conversion.
        string encoding = encodingOverrideEmpty ? property.ServerEncoding : null;
        return DmTemporalCapabilityProfile.Resolve(DmTemporalCapabilityProfile.CaptureIdentity(
            property?.ServerVersion, property?.msgVersion ?? 0, encoding, encodingOverrideEmpty));
    }
    internal static DmTemporalWriteTarget Target(DmField field, int type, int encodedScale, int? requestedScale = null)
    {
        int mask = Mask(field?.scale ?? encodedScale, field?.mask ?? 0);
        int columnScale = SemanticScale(type, field?.scale ?? encodedScale);
        return new(type, DmTemporalWireCodec.ForCType(type), field?.isBdta == true ? DmTemporalResultFormat.Bdta : DmTemporalResultFormat.OrdinaryPacked,
            columnScale, SemanticScale(type, encodedScale), requestedScale, field?.GetTypeFlag() == 1, mask);
    }
    internal static DmTemporalFields ReadHelper(byte[] bytes, DmTemporalTextKind? legacyKind = null)
    {
        if (bytes == null) throw new InvalidOperationException("This temporal operation requires byte representation.");
        if (bytes.Length == 12)
        {
            var kind = legacyKind ?? (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0
                ? DmTemporalTextKind.Time : DmTemporalTextKind.Timestamp);
            return DmTemporalWireCodec.FromLegacyMicroseconds(bytes, kind, kind != DmTemporalTextKind.Date);
        }
        var layout = PackedLayout(bytes.Length);
        return DmTemporalWireCodec.DecodeRaw(bytes, layout, bytes.Length == 3 ? 0 : bytes.Length is 9 or 11 ? 9 : 6);
    }
    internal static DmTemporalFields ReadNetwork(byte[] bytes, DmField field)
    {
        if (field == null) throw new InvalidOperationException("Temporal decoding requires column metadata.");
        if (Mask(field.scale, field.mask) == 4) throw new NotSupportedException("Local timezone temporal values are not verified.");
        if (field.isBdta) return ReadBdtaFixture(bytes, field);
        // Ordinary12 has no accepted unit/layout evidence. It is not a public helper buffer.
        if (bytes == null || bytes.Length == 12) throw new NotSupportedException("Temporal network layout is unverified.");
        var layout = DmTemporalWireCodec.ForCType(field.type);
        try { return DmTemporalWireCodec.DecodeRaw(bytes, layout, SemanticScale(field.type, field.scale)); }
        catch (ArgumentException ex) { throw new InvalidDataException("Temporal network value has invalid length.", ex); }
        catch (OverflowException ex) { throw new InvalidDataException("Temporal network value has invalid fields or declared precision.", ex); }
    }
    internal static DmTemporalFields ReadNetwork(byte[] bytes, DmTemporalReadDescriptor descriptor)
    {
        if (descriptor.Mask == 4) throw new NotSupportedException("Local timezone temporal values are not verified.");
        if (descriptor.ActualFormat == DmTemporalResultFormat.Bdta)
            return ReadBdtaFixture(bytes, descriptor);
        if (descriptor.ActualFormat != DmTemporalResultFormat.OrdinaryPacked || bytes == null || bytes.Length == 12)
            throw new NotSupportedException("Temporal network layout is unverified.");
        var layout = DmTemporalWireCodec.ForCType(descriptor.CType);
        try { return DmTemporalWireCodec.DecodeRaw(bytes, layout, descriptor.FractionalScale); }
        catch (ArgumentException ex) { throw new InvalidDataException("Temporal network value has invalid length.", ex); }
        catch (OverflowException ex) { throw new InvalidDataException("Temporal network value has invalid fields or declared precision.", ex); }
    }
    private static DmTemporalFields ReadBdtaFixture(byte[] bytes, DmTemporalReadDescriptor descriptor)
    {
        if (bytes == null || bytes.Length is not (12 or 13)) throw new InvalidDataException("Invalid compact temporal value length.");
        int ns = bytes[7] | (bytes[8] << 8) | (bytes[9] << 16) | (bytes.Length == 13 ? bytes[12] << 24 : 0);
        bool time = descriptor.CType is 15 or 22;
        var fields = new DmTemporalFields(time ? 0 : BinaryPrimitives.ReadInt16LittleEndian(bytes), time ? 0 : bytes[2], time ? 0 : bytes[3],
            bytes[4], bytes[5], bytes[6], ns, descriptor.CType is 22 or 23 or 27 ? BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(10)) : null);
        try
        {
            DmTemporalInteger.ValidateWireFraction(ns, descriptor.FractionalScale);
            DmTemporalInteger.ValidateTime(fields);
            if (!time) DmTemporalInteger.ValidateDate(fields);
        }
        catch (OverflowException ex) { throw new InvalidDataException("Invalid compact temporal fields.", ex); }
        return fields;
    }
    private static DmTemporalFields ReadBdtaFixture(byte[] bytes, DmField field)
    {
        // Existing internal compact layout explicitly stores ns, unlike the legacy12 helper.
        if (bytes == null || bytes.Length is not (12 or 13)) throw new InvalidDataException("Invalid compact temporal value length.");
        int ns = bytes[7] | (bytes[8] << 8) | (bytes[9] << 16) | (bytes.Length == 13 ? bytes[12] << 24 : 0);
        bool time = field.type is 15 or 22;
        var fields = new DmTemporalFields(time ? 0 : BinaryPrimitives.ReadInt16LittleEndian(bytes), time ? 0 : bytes[2], time ? 0 : bytes[3],
            bytes[4], bytes[5], bytes[6], ns, field.type is 22 or 23 or 27 ? BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(10)) : null);
        try
        {
            DmTemporalInteger.ValidateWireFraction(ns, SemanticScale(field.type, field.scale));
            DmTemporalInteger.ValidateTime(fields);
            if (!time) DmTemporalInteger.ValidateDate(fields);
        }
        catch (OverflowException ex) { throw new InvalidDataException("Invalid compact temporal fields.", ex); }
        return fields;
    }
    internal static DmTemporalFields FromSemantic(int[] values, int type, int scale)
    {
        if (values == null || values.Length < 8) throw new InvalidOperationException("Temporal semantic representation requires eight fields.");
        if (!IsTemporal(type)) throw new InvalidCastException("Semantic temporal kind is unsupported.");
        bool time = type is 15 or 22;
        if (time && !(values[0] == 0 && values[1] == 0 && values[2] == 0)
            && !(values[0] == 1900 && values[1] == 1 && values[2] == 1))
            throw new OverflowException("TIME semantic representation has unexpected calendar fields.");
        int? offset = values[7] == int.MinValue ? null : values[7];
        if (values.Length > OracleDateFormat.OFFSET_TIMEZONE_SIGN && values[OracleDateFormat.OFFSET_TIMEZONE_SIGN] > 0 && offset.HasValue)
            offset = checked(-offset.Value);
        if (type is not (22 or 23 or 27)) offset = null;
        var fields = new DmTemporalFields(time ? 0 : values[0], time ? 0 : values[1], time ? 0 : values[2], values[3], values[4], values[5], values[6], offset);
        DmTemporalInteger.ValidateWireFraction(fields.NanosecondsWithinSecond, SemanticScale(type, scale));
        DmTemporalInteger.ValidateTime(fields);
        if (!time) DmTemporalInteger.ValidateDate(fields);
        if (type == 14 && (fields.Hour != 0 || fields.Minute != 0 || fields.Second != 0 || fields.NanosecondsWithinSecond != 0))
            throw new OverflowException("DATE semantic representation cannot carry clock fields.");
        if (type is 22 or 23 or 27 && !offset.HasValue) throw new InvalidCastException("Temporal timezone representation has no offset.");
        return fields;
    }
    internal static int[] ToSemantic(DmTemporalFields fields) => new[] { fields.Year, fields.Month, fields.Day, fields.Hour,
        fields.Minute, fields.Second, fields.NanosecondsWithinSecond, fields.OffsetMinutes ?? int.MinValue };
    internal static DmTemporalFields FromMicroseconds(int[] values, int type)
    {
        if (values == null || values.Length < 7) throw new ArgumentException("Temporal helper requires seven fields.");
        int microseconds = values[6];
        if (microseconds is < 0 or > 999999) throw new OverflowException("Temporal helper fraction exceeds microsecond range.");
        int? offset = null;
        if (type is 22 or 23)
        {
            if (values.Length <= OracleDateFormat.OFFSET_TIMEZONE_SIGN) throw new ArgumentException("Temporal helper requires explicit offset and sign fields.");
            offset = values[OracleDateFormat.OFFSET_TIMEZONE_SIGN] > 0 ? checked(-values[7]) : values[7];
        }
        bool time = type is 15 or 22;
        return new(time ? 0 : values[0], time ? 0 : values[1], time ? 0 : values[2], values[3], values[4], values[5], checked(microseconds * 1000), offset);
    }
    internal static int ParseDigits(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) throw new FormatException("Temporal numeric field is missing.");
        int value = 0;
        foreach (char digit in text)
        {
            if (digit is < '0' or > '9') throw new FormatException("Temporal numeric field is invalid.");
            value = checked(value * 10 + digit - '0');
        }
        return value;
    }
    internal static (DmTemporalFields Fields, int Type, int Scale) Parse(string text)
    {
        if (text == null) throw new FormatException("Temporal text is missing.");
        string[] parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 3) throw new FormatException("Temporal text has invalid shape.");
        string date = null, time = null, zone = null;
        if (parts[0].Contains(':')) { time = parts[0]; if (parts.Length == 2) zone = parts[1]; else if (parts.Length != 1) throw new FormatException("Temporal text has invalid shape."); }
        else { date = parts[0]; if (parts.Length >= 2) time = parts[1]; if (parts.Length == 3) zone = parts[2]; }
        int year = 1900, month = 1, day = 1, hour = 0, minute = 0, second = 0, ns = 0, scale = 0;
        if (date != null)
        {
            bool negative = date.StartsWith("-", StringComparison.Ordinal);
            string[] components = (negative ? date[1..] : date).Split(new[] { '-', '.', '/' });
            if (components.Length != 3) throw new FormatException("Temporal date has invalid shape.");
            year = ParseDigits(components[0]); if (negative) year = checked(-year);
            month = ParseDigits(components[1]); day = ParseDigits(components[2]);
        }
        if (time != null)
        {
            string[] components = time.Split(':');
            if (components.Length != 3) throw new FormatException("Temporal clock has invalid shape.");
            hour = ParseDigits(components[0]); minute = ParseDigits(components[1]);
            string[] seconds = components[2].Split('.');
            if (seconds.Length is < 1 or > 2) throw new FormatException("Temporal fraction has invalid shape.");
            second = ParseDigits(seconds[0]);
            if (seconds.Length == 2)
            {
                string fraction = seconds[1];
                if (fraction.Length is < 1 or > 9) throw new FormatException("Temporal fraction has unsupported precision.");
                scale = fraction.Length; ns = checked(ParseDigits(fraction) * DmTemporalInteger.PowerOfTen(9 - scale));
            }
        }
        int? offset = null;
        if (zone != null)
        {
            if (zone.Length < 2 || zone[0] is not ('+' or '-')) throw new FormatException("Temporal offset requires a sign.");
            string[] components = zone[1..].Split(':');
            if (components.Length != 2) throw new FormatException("Temporal offset has invalid shape.");
            int hours = ParseDigits(components[0]), minutes = ParseDigits(components[1]);
            if (minutes > 59) throw new OverflowException("Temporal offset minutes are invalid.");
            offset = checked(hours * 60 + minutes); if (zone[0] == '-') offset = checked(-offset.Value);
            if (offset is < short.MinValue or > short.MaxValue) throw new OverflowException("Temporal offset exceeds wire range.");
        }
        int type = time == null ? 14 : date == null ? offset.HasValue ? 22 : 15 : offset.HasValue ? 23 : 16;
        var fields = new DmTemporalFields(year, month, day, hour, minute, second, ns, offset);
        DmTemporalInteger.ValidateDate(fields); DmTemporalInteger.ValidateTime(fields);
        return (fields, type, scale);
    }
    internal static int[] AddClock(int[] values, long amount, long unitsPerDay)
    {
        if (values == null || values.Length < 8) throw new InvalidOperationException("Temporal arithmetic requires eight fields.");
        const long nanosecondsPerDay = 86_400_000_000_000;
        if (unitsPerDay <= 0 || nanosecondsPerDay % unitsPerDay != 0)
            throw new ArgumentOutOfRangeException(nameof(unitsPerDay));
        var fields = new DmTemporalFields(values[0], values[1], values[2], values[3], values[4], values[5], values[6]);
        DmTemporalInteger.ValidateDate(fields); DmTemporalInteger.ValidateTime(fields);
        long nsOfDay = ((long)fields.Hour * 3600 + fields.Minute * 60 + fields.Second) * 1_000_000_000 + fields.NanosecondsWithinSecond;
        // Split the unbounded increment before multiplying; the remainder is less than one day.
        long days = Math.DivRem(amount, unitsPerDay, out long dayUnits);
        long total = checked(nsOfDay + dayUnits * (nanosecondsPerDay / unitsPerDay));
        long carry = Math.DivRem(total, nanosecondsPerDay, out long rest);
        if (rest < 0) { rest += nanosecondsPerDay; carry--; }
        long ordinal = StartOfYear(fields.Year);
        for (int current = 1; current < fields.Month; current++) ordinal += DaysInMonth(fields.Year, current);
        ordinal = checked(ordinal + fields.Day - 1 + checked(days + carry));
        long maximum = StartOfYear(int.MaxValue) + (DaysInMonth(int.MaxValue, 2) == 29 ? 366 : 365) - 1;
        if (ordinal < StartOfYear(int.MinValue) || ordinal > maximum)
            throw new OverflowException("Temporal arithmetic exceeds the semantic calendar range.");

        // Locate the representable signed year in bounded time, without a CLR projection or a year zero.
        long low = ordinal < 0 ? int.MinValue : 1, high = ordinal < 0 ? -1 : int.MaxValue;
        int year = (int)low;
        while (low <= high)
        {
            long candidate = low + (high - low) / 2;
            if (StartOfYear((int)candidate) <= ordinal) { year = (int)candidate; low = candidate + 1; }
            else high = candidate - 1;
        }
        int dayOfYear = checked((int)(ordinal - StartOfYear(year))), month = 1;
        while (dayOfYear >= DaysInMonth(year, month))
        {
            dayOfYear -= DaysInMonth(year, month);
            month++;
        }
        int day = dayOfYear + 1;
        // Commit only after all validation/arithmetic; failures preserve the caller's whole array.
        values[0] = year; values[1] = month; values[2] = day;
        values[3] = (int)(rest / 3_600_000_000_000); rest %= 3_600_000_000_000;
        values[4] = (int)(rest / 60_000_000_000); rest %= 60_000_000_000;
        values[5] = (int)(rest / 1_000_000_000); values[6] = (int)(rest % 1_000_000_000);
        return values;
    }
    private static long StartOfYear(int year)
    {
        // Negative years use the same divisibility rule as ValidateDate and omit year zero.
        long prior = year > 0 ? (long)year - 1 : year;
        return 365 * prior + prior / 4 - prior / 100 + prior / 400;
    }
    private static int DaysInMonth(int year, int month) => month switch
    { 2 => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0) ? 29 : 28, 4 or 6 or 9 or 11 => 30, _ => 31 };
}
