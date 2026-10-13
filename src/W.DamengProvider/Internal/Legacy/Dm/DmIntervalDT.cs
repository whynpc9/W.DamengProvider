using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using W.Dm.Internal.Types;

namespace W.Dm;

public class DmIntervalDT
{
    public const byte QUA_Y = 0;
    public const byte QUA_YM = 1;
    public const byte QUA_MO = 2;
    public const byte QUA_D = 3;
    public const byte QUA_DH = 4;
    public const byte QUA_DHM = 5;
    public const byte QUA_DHMS = 6;
    public const byte QUA_H = 7;
    public const byte QUA_HM = 8;
    public const byte QUA_HMS = 9;
    public const byte QUA_M = 10;
    public const byte QUA_MS = 11;
    public const byte QUA_S = 12;
    public const int LEADSCALE_MAX = 9;

    private byte type = QUA_D;
    private int leadScale = 2;
    private int secScale = 6;
    private bool negative;
    public int days;
    public int hours;
    public int minutes;
    public int seconds;
    // Public legacy unit is microseconds, including after direct caller mutation.
    public int fraction;
    private int scaleForSvr;

    private readonly record struct State(byte Type, int Leading, int Scale, bool Negative,
        int Days, int Hours, int Minutes, int Seconds, int Microseconds)
    {
        internal int PackedScale => (Type << 8) | (Leading << 4) | Scale;
    }

    public DmIntervalDT(int days, int hours, int minutes, int seconds, int millseconds)
    {
        int microseconds = checked(millseconds * 1000);
        Apply(FromSignedComponents(QUA_DHMS, 2, 6, days, hours, minutes, seconds, microseconds));
    }

    public DmIntervalDT(TimeSpan timeSpan, int scale)
    {
        var metadata = DecodeScale(scale);
        if (metadata.Type != QUA_DHMS)
            throw new NotSupportedException("TimeSpan requires an INTERVAL DAY TO SECOND declaration.");
        var fields = DmTemporalIntervalFields.FromTimeSpanExact(timeSpan, metadata.Scale, metadata.Leading);
        Apply(new State(metadata.Type, metadata.Leading, metadata.Scale, fields.Negative,
            checked((int)fields.Days), fields.Hours, fields.Minutes, fields.Seconds, fields.NanosecondsWithinSecond / 1000));
    }

    public DmIntervalDT(byte[] interval, int leadScale = 2, int secScale = 6)
    {
        var wire = ReadWire(interval);
        // These arguments are the historical construction declaration, not nullable overrides.
        ValidateDeclaration(wire.Type, leadScale, secScale);
        var declared = wire with { Leading = leadScale, Scale = secScale };
        ValidateState(declared);
        Apply(declared);
    }

    public DmIntervalDT(string str)
    {
        Apply(Parse(str, QUA_DHMS, 2, 6, explicitDeclaration: false));
    }

    public DmIntervalDT(string str, byte type, int leadScale, int secScale)
    {
        Apply(Parse(str, type, leadScale, secScale, explicitDeclaration: true));
    }

    private DmIntervalDT(State state)
    {
        ValidateState(state);
        Apply(state);
    }

    private static void ValidateDeclaration(byte subtype, int leading, int scale)
    {
        if (subtype is < QUA_D or > QUA_S)
            throw new NotSupportedException("Only day, hour, minute and second INTERVAL subtypes are supported.");
        if (leading is < 1 or > LEADSCALE_MAX) throw new ArgumentOutOfRangeException(nameof(leading));
        DmTemporalInteger.ValidateUserScale(scale);
        if (scale > 6) throw new NotSupportedException("Legacy INTERVAL fractional precision cannot exceed six digits.");
    }

    private static State DecodeScale(int packed)
    {
        if (packed < 0 || (packed & ~0xFFF) != 0)
            throw new NotSupportedException("INTERVAL scale metadata contains unsupported bits.");
        byte subtype = (byte)((packed >> 8) & 0xF);
        int leading = (packed >> 4) & 0xF, scale = packed & 0xF;
        ValidateDeclaration(subtype, leading, scale);
        return new State(subtype, leading, scale, false, 0, 0, 0, 0, 0);
    }

    private static State FromSignedComponents(byte subtype, int leading, int scale,
        int day, int hour, int minute, int second, int microsecond)
    {
        int sign = 0;
        foreach (int value in new[] { day, hour, minute, second, microsecond })
        {
            int componentSign = Math.Sign(value);
            if (componentSign != 0 && sign != 0 && componentSign != sign)
                throw new OverflowException("INTERVAL components cannot have mixed signs.");
            if (componentSign != 0) sign = componentSign;
        }
        // Widen before negation: int.MinValue must not wrap or silently become a valid magnitude.
        int Magnitude(int value) => checked((int)(value < 0 ? -(long)value : value));
        var state = new State(subtype, leading, scale, sign < 0,
            Magnitude(day), Magnitude(hour), Magnitude(minute), Magnitude(second), Magnitude(microsecond));
        ValidateState(state);
        return state;
    }

    private State Snapshot()
    {
        // No cached fields: public mutations are the authoritative value on every operation.
        var state = new State(type, leadScale, secScale, negative, days, hours, minutes, seconds, fraction);
        ValidateState(state);
        return state;
    }

    private static void ValidateState(State state)
    {
        ValidateDeclaration(state.Type, state.Leading, state.Scale);
        if (state.Days < 0 || state.Hours < 0 || state.Minutes < 0 || state.Seconds < 0
            || state.Microseconds is < 0 or > 999999)
            throw new OverflowException("INTERVAL component magnitudes are outside their declared range.");
        bool day = state.Type is >= QUA_D and <= QUA_DHMS;
        bool hour = state.Type is >= QUA_H and <= QUA_HMS;
        bool minute = state.Type is QUA_M or QUA_MS;
        bool second = state.Type == QUA_S;
        int leading = day ? state.Days : hour ? state.Hours : minute ? state.Minutes : state.Seconds;
        if (leading >= DmTemporalInteger.PowerOfTen(state.Leading))
            throw new OverflowException("INTERVAL leading magnitude exceeds its declared precision.");
        if (!day && state.Days != 0 || !day && !hour && state.Hours != 0 || second && state.Minutes != 0)
            throw new OverflowException("INTERVAL contains a component outside its subtype.");
        bool hasHours = state.Type is QUA_DH or QUA_DHM or QUA_DHMS || hour;
        bool hasMinutes = state.Type is QUA_DHM or QUA_DHMS or QUA_HM or QUA_HMS || minute;
        bool hasSeconds = state.Type is QUA_DHMS or QUA_HMS or QUA_MS or QUA_S;
        if (!hasHours && state.Hours != 0 || !hasMinutes && state.Minutes != 0
            || !hasSeconds && (state.Seconds != 0 || state.Microseconds != 0))
            throw new OverflowException("INTERVAL contains an unrepresented lower component.");
        if (day && state.Hours > 23 || (day || hour) && state.Minutes > 59
            || !second && state.Seconds > 59)
            throw new OverflowException("INTERVAL lower component exceeds its range.");
        DmTemporalInteger.ValidateWireFraction(checked(state.Microseconds * 1000), state.Scale);
        NormalizedUnchecked(state).Validate(state.Scale, state.Leading);
    }

    private static BigInteger MagnitudeMicroseconds(State state) =>
        ((((BigInteger)state.Days * 24 + state.Hours) * 60 + state.Minutes) * 60 + state.Seconds)
        * 1_000_000 + state.Microseconds;

    private static DmTemporalIntervalFields NormalizedUnchecked(State state)
    {
        BigInteger remainder = MagnitudeMicroseconds(state);
        long day = checked((long)(remainder / 86_400_000_000L)); remainder %= 86_400_000_000L;
        int hour = (int)(remainder / 3_600_000_000L); remainder %= 3_600_000_000L;
        int minute = (int)(remainder / 60_000_000); remainder %= 60_000_000;
        int second = (int)(remainder / 1_000_000);
        int ns = checked((int)(remainder % 1_000_000) * 1000);
        return new DmTemporalIntervalFields(state.Negative, day, hour, minute, second, ns);
    }

    private void Apply(State state)
    {
        type = state.Type; leadScale = state.Leading; secScale = state.Scale; negative = state.Negative;
        days = state.Days; hours = state.Hours; minutes = state.Minutes; seconds = state.Seconds; fraction = state.Microseconds;
        scaleForSvr = state.PackedScale;
    }

    private static State ReadWire(byte[] interval)
    {
        if (interval is null) throw new ArgumentNullException(nameof(interval));
        if (interval.Length != 24) throw new ArgumentException("INTERVAL wire value must contain exactly 24 bytes.", nameof(interval));
        var declaration = DecodeScale(BinaryPrimitives.ReadInt32LittleEndian(interval.AsSpan(20, 4)));
        int day = BinaryPrimitives.ReadInt32LittleEndian(interval.AsSpan(0, 4));
        int hour = BinaryPrimitives.ReadInt32LittleEndian(interval.AsSpan(4, 4));
        int minute = BinaryPrimitives.ReadInt32LittleEndian(interval.AsSpan(8, 4));
        int second = BinaryPrimitives.ReadInt32LittleEndian(interval.AsSpan(12, 4));
        int microsecond = BinaryPrimitives.ReadInt32LittleEndian(interval.AsSpan(16, 4));
        return FromSignedComponents(declaration.Type, declaration.Leading, declaration.Scale, day, hour, minute, second, microsecond);
    }

    public void reset(byte[] interval)
    {
        // Parse/validate all fields before replacing any public state.
        Apply(ReadWire(interval));
    }

    public int getScaleForSvr() { _ = Snapshot(); return scaleForSvr; }
    public int getDay() { _ = Snapshot(); return days; }
    public int getHour() { _ = Snapshot(); return hours; }
    public int getMinute() { _ = Snapshot(); return minutes; }
    public int getSecond() { _ = Snapshot(); return seconds; }
    public int getMsec() { _ = Snapshot(); return fraction; }
    public int GetDTType() { _ = Snapshot(); return type; }

    private static State ConvertExact(State source, int packed)
    {
        var target = DecodeScale(packed);
        BigInteger total = MagnitudeMicroseconds(source);
        long quantum = target.Type switch
        {
            QUA_D => 86_400_000_000L, QUA_DH or QUA_H => 3_600_000_000L,
            QUA_DHM or QUA_HM or QUA_M => 60_000_000L, _ => 1L
        };
        if (total % quantum != 0)
            throw new OverflowException("INTERVAL narrowing cannot discard a lower component.");
        int day = 0, hour = 0, minute = 0, second = 0, microsecond = 0;
        if (target.Type is >= QUA_D and <= QUA_DHMS)
        {
            day = checked((int)(total / 86_400_000_000L)); total %= 86_400_000_000L;
        }
        if (target.Type is >= QUA_DH and <= QUA_HMS)
        {
            hour = checked((int)(total / 3_600_000_000L)); total %= 3_600_000_000L;
        }
        if (target.Type is QUA_DHM or QUA_DHMS or QUA_HM or QUA_HMS or QUA_M or QUA_MS)
        {
            minute = checked((int)(total / 60_000_000L)); total %= 60_000_000L;
        }
        if (target.Type is QUA_DHMS or QUA_HMS or QUA_MS or QUA_S)
        {
            second = checked((int)(total / 1_000_000)); microsecond = (int)(total % 1_000_000);
        }
        var converted = new State(target.Type, target.Leading, target.Scale, source.Negative, day, hour, minute, second, microsecond);
        ValidateState(converted);
        return converted;
    }

    public DmIntervalDT convertTo(int scale) => new(ConvertExact(Snapshot(), scale));

    public byte[] encode(int scale)
    {
        var current = Snapshot();
        var encoded = ConvertExact(current, scale == 0 ? current.PackedScale : scale);
        var bytes = new byte[24];
        int Signed(int magnitude) => encoded.Negative ? checked(-magnitude) : magnitude;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), Signed(encoded.Days));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), Signed(encoded.Hours));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), Signed(encoded.Minutes));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), Signed(encoded.Seconds));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), Signed(encoded.Microseconds));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20, 4), encoded.PackedScale);
        Apply(encoded); // Historical successful encode mutates; metadata and components now commit together.
        return bytes;
    }

    private State MutationSnapshot()
    {
        var state = new State(type, leadScale, secScale, negative, days, hours, minutes, seconds, fraction);
        ValidateDeclaration(state.Type, state.Leading, state.Scale);
        if (state.Days < 0 || state.Hours < 0 || state.Minutes < 0 || state.Seconds < 0 || state.Microseconds is < 0 or > 999999)
            throw new OverflowException("INTERVAL mutator requires nonnegative magnitudes and a microsecond fraction.");
        DmTemporalInteger.ValidateWireFraction(checked(state.Microseconds * 1000), state.Scale);
        // An earlier public fold can intentionally be outside the unchanged subtype's shape.
        return state;
    }

    public void ToHours()
    {
        var state = MutationSnapshot();
        int folded = checked((int)((BigInteger)state.Days * 24 + state.Hours));
        hours = folded; days = 0;
    }

    public void ToMinutes()
    {
        var state = MutationSnapshot();
        int folded = checked((int)((BigInteger)state.Hours * 60 + state.Minutes));
        minutes = folded; hours = 0;
    }

    public void ToSeconds()
    {
        var state = MutationSnapshot();
        int folded = checked((int)((BigInteger)state.Minutes * 60 + state.Seconds));
        seconds = folded; minutes = 0;
    }

    public void Clear()
    {
        days = hours = minutes = seconds = fraction = 0;
        // Preserve the declared subtype, precision and established sign as this historical mutator did.
    }

    public string GetDTString()
    {
        var value = Snapshot();
        string Leading(int number) => number.ToString("D" + value.Leading, CultureInfo.InvariantCulture);
        string Pair(int number) => number.ToString("D2", CultureInfo.InvariantCulture);
        string Fraction() => value.Scale == 0 ? string.Empty : "." + value.Microseconds.ToString("D6", CultureInfo.InvariantCulture).Substring(0, value.Scale);
        string body = value.Type switch
        {
            QUA_D => Leading(value.Days),
            QUA_DH => Leading(value.Days) + " " + Pair(value.Hours),
            QUA_DHM => Leading(value.Days) + " " + Pair(value.Hours) + ":" + Pair(value.Minutes),
            QUA_DHMS => Leading(value.Days) + " " + Pair(value.Hours) + ":" + Pair(value.Minutes) + ":" + Pair(value.Seconds) + Fraction(),
            QUA_H => Leading(value.Hours),
            QUA_HM => Leading(value.Hours) + ":" + Pair(value.Minutes),
            QUA_HMS => Leading(value.Hours) + ":" + Pair(value.Minutes) + ":" + Pair(value.Seconds) + Fraction(),
            QUA_M => Leading(value.Minutes),
            QUA_MS => Leading(value.Minutes) + ":" + Pair(value.Seconds) + Fraction(),
            QUA_S => Leading(value.Seconds) + Fraction(),
            _ => throw new NotSupportedException("INTERVAL subtype is unsupported.")
        };
        string suffix = value.Type switch
        {
            QUA_D => $"DAY({value.Leading})", QUA_DH => $"DAY({value.Leading}) TO HOUR",
            QUA_DHM => $"DAY({value.Leading}) TO MINUTE", QUA_DHMS => $"DAY({value.Leading}) TO SECOND({value.Scale})",
            QUA_H => $"HOUR({value.Leading})", QUA_HM => $"HOUR({value.Leading}) TO MINUTE",
            QUA_HMS => $"HOUR({value.Leading}) TO SECOND({value.Scale})", QUA_M => $"MINUTE({value.Leading})",
            QUA_MS => $"MINUTE({value.Leading}) TO SECOND({value.Scale})", QUA_S => $"SECOND({value.Leading},{value.Scale})",
            _ => throw new NotSupportedException("INTERVAL subtype is unsupported.")
        };
        return "INTERVAL '" + (value.Negative ? "-" : string.Empty) + body + "' " + suffix;
    }

    public override string ToString() => GetDTString();
    public string GetTimeSpanFormatString() => ToTimeSpanExact().ToString("c", CultureInfo.InvariantCulture);
    internal TimeSpan ToTimeSpanExact()
    {
        var state = Snapshot();
        return NormalizedUnchecked(state).ToTimeSpanExact(state.Scale, state.Leading);
    }

    private static State Parse(string text, byte subtype, int leading, int scale, bool explicitDeclaration)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        string value = text.Trim();
        if (value.Length == 0) throw new FormatException("INTERVAL text is empty.");
        if (value.StartsWith("INTERVAL", StringComparison.OrdinalIgnoreCase))
        {
            var literal = Regex.Match(value,
                @"^INTERVAL\s+(?<value>-?'[^']*')\s+(?<first>DAY|HOUR|MINUTE|SECOND)(?:\(\s*(?<lead>[0-9]+)\s*(?:,\s*(?<fraction>[0-9]+)\s*)?\))?(?:\s+TO\s+(?<last>HOUR|MINUTE|SECOND)(?:\(\s*(?<tail>[0-9]+)\s*\))?)?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!literal.Success) throw new FormatException("INTERVAL literal syntax is unsupported.");
            string first = literal.Groups["first"].Value.ToUpperInvariant();
            string last = literal.Groups["last"].Value.ToUpperInvariant();
            byte declaredType = (first, last) switch
            {
                ("DAY", "") => QUA_D, ("DAY", "HOUR") => QUA_DH, ("DAY", "MINUTE") => QUA_DHM, ("DAY", "SECOND") => QUA_DHMS,
                ("HOUR", "") => QUA_H, ("HOUR", "MINUTE") => QUA_HM, ("HOUR", "SECOND") => QUA_HMS,
                ("MINUTE", "") => QUA_M, ("MINUTE", "SECOND") => QUA_MS, ("SECOND", "") => QUA_S,
                _ => throw new NotSupportedException("INTERVAL literal subtype is unsupported.")
            };
            if (literal.Groups["fraction"].Success && first != "SECOND" || literal.Groups["tail"].Success && last != "SECOND")
                throw new FormatException("INTERVAL precision qualifier is invalid.");
            int declaredLeading = literal.Groups["lead"].Success ? ParseMagnitude(literal.Groups["lead"].Value) : leading;
            int declaredScale = literal.Groups["fraction"].Success ? ParseMagnitude(literal.Groups["fraction"].Value)
                : literal.Groups["tail"].Success ? ParseMagnitude(literal.Groups["tail"].Value) : scale;
            if (explicitDeclaration && (declaredType != subtype || declaredLeading != leading || declaredScale != scale))
                throw new InvalidOperationException("INTERVAL literal conflicts with its declared subtype or precision.");
            subtype = declaredType; leading = declaredLeading; scale = declaredScale;
            value = literal.Groups["value"].Value.Trim();
        }
        ValidateDeclaration(subtype, leading, scale);
        bool sign = false, signSeen = false;
        if (value.Length > 0 && (value[0] == '-' || value[0] == '+'))
        {
            sign = value[0] == '-'; signSeen = true; value = value.Substring(1).Trim();
        }
        if (value.StartsWith("'", StringComparison.Ordinal))
        {
            if (value.Length < 2 || !value.EndsWith("'", StringComparison.Ordinal)) throw new FormatException("INTERVAL quote is unmatched.");
            value = value.Substring(1, value.Length - 2).Trim();
            if (value.Length > 0 && (value[0] == '-' || value[0] == '+'))
            {
                if (signSeen) throw new FormatException("INTERVAL has multiple signs.");
                sign = value[0] == '-'; signSeen = true; value = value.Substring(1).Trim();
            }
        }
        string pattern = subtype switch
        {
            QUA_D or QUA_H or QUA_M => @"^(?<a>[0-9]+)$",
            // Missing trailing components are zero; nested groups prohibit holes or an early fraction.
            QUA_DH => @"^(?<a>[0-9]+)(?:\s+(?<b>[0-9]+))?$",
            QUA_DHM => @"^(?<a>[0-9]+)(?:\s+(?<b>[0-9]+)(?::(?<c>[0-9]+))?)?$",
            QUA_DHMS => @"^(?<a>[0-9]+)(?:\s+(?<b>[0-9]+)(?::(?<c>[0-9]+)(?::(?<d>[0-9]+)(?:\.(?<f>[0-9]+))?)?)?)?$",
            QUA_HM => @"^(?<a>[0-9]+)(?::(?<b>[0-9]+))?$",
            QUA_HMS => @"^(?<a>[0-9]+)(?::(?<b>[0-9]+)(?::(?<c>[0-9]+)(?:\.(?<f>[0-9]+))?)?)?$",
            QUA_MS => @"^(?<a>[0-9]+)(?::(?<b>[0-9]+)(?:\.(?<f>[0-9]+))?)?$",
            QUA_S => @"^(?<a>[0-9]+)(?:\.(?<f>[0-9]+))?$",
            _ => throw new NotSupportedException("INTERVAL subtype is unsupported.")
        };
        var match = Regex.Match(value, pattern, RegexOptions.CultureInvariant);
        if (!match.Success) throw new FormatException("INTERVAL component syntax is invalid.");
        int Number(string name) => match.Groups[name].Success ? ParseMagnitude(match.Groups[name].Value) : 0;
        int day = 0, hour = 0, minute = 0, second = 0;
        if (subtype is >= QUA_D and <= QUA_DHMS) day = Number("a");
        if (subtype is >= QUA_DH and <= QUA_DHMS) hour = Number("b");
        if (subtype is QUA_DHM or QUA_DHMS) minute = Number("c");
        if (subtype == QUA_DHMS) second = Number("d");
        if (subtype is >= QUA_H and <= QUA_HMS) hour = Number("a");
        if (subtype is QUA_HM or QUA_HMS) minute = Number("b");
        if (subtype == QUA_HMS) second = Number("c");
        if (subtype is QUA_M or QUA_MS) minute = Number("a");
        if (subtype == QUA_MS) second = Number("b");
        if (subtype == QUA_S) second = Number("a");
        int microsecond = ParseFraction(match.Groups["f"].Success ? match.Groups["f"].Value : null);
        var state = new State(subtype, leading, scale, sign, day, hour, minute, second, microsecond);
        ValidateState(state);
        return state;
    }

    private static int ParseMagnitude(string digits)
    {
        // Only ASCII digit groups reach this helper; no floating parsing or current culture.
        return checked((int)BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture));
    }

    private static int ParseFraction(string digits)
    {
        if (digits is null) return 0;
        if (digits.Length > 6)
        {
            for (int i = 6; i < digits.Length; i++)
                if (digits[i] != '0') throw new OverflowException("INTERVAL text has unsupported sub-microsecond precision.");
            digits = digits.Substring(0, 6);
        }
        return int.Parse(digits.PadRight(6, '0'), NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
