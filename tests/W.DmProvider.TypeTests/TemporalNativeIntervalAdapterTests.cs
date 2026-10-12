using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Linq;
using System.Reflection;
using W.Dm;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class TemporalNativeIntervalAdapterTests
{
    private static int Scale(byte type, int leading = 9, int fraction = 6) => (type << 8) | (leading << 4) | fraction;
    private static int WireInt(byte[] bytes, int index) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(index * 4, 4));
    private static byte[] Wire(int day, int hour, int minute, int second, int microsecond, int scale)
    {
        byte[] bytes = new byte[24];
        int[] values = { day, hour, minute, second, microsecond, scale };
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4, 4), values[i]);
        return bytes;
    }
    private static int[] Components(DmIntervalDT value) => new[] { value.days, value.hours, value.minutes, value.seconds, value.fraction };

    [Fact]
    public void ComponentConstructorKeepsMillisecondsAndDefaultDeclaredScale()
    {
        var value = new DmIntervalDT(1, 2, 3, 4, 567);
        Assert.Equal(567000, value.getMsec());
        Assert.Equal(1574, value.getScaleForSvr());
        Assert.Equal("INTERVAL '01 02:03:04.567000' DAY(2) TO SECOND(6)", value.GetDTString());
        Assert.Equal((1 * TimeSpan.TicksPerDay) + (2 * TimeSpan.TicksPerHour) + (3 * TimeSpan.TicksPerMinute)
            + (4 * TimeSpan.TicksPerSecond) + 5_670_000, value.ToTimeSpanExact().Ticks);
        Assert.Throws<OverflowException>(() => new DmIntervalDT(100, 0, 0, 0, 0));
        Assert.Throws<OverflowException>(() => new DmIntervalDT(0, 0, 0, 0, int.MaxValue));
        Assert.Throws<OverflowException>(() => new DmIntervalDT(int.MinValue, 0, 0, 0, 0));
        Assert.Throws<OverflowException>(() => new DmIntervalDT(1, -2, 0, 0, 0));
    }

    [Fact]
    public void NegativeWireGoldenAndSignedMagnitudeAreExact()
    {
        // Independent six little-endian Int32 words: -1/-2/-3/-4/-567890, DHMS(9,6)=0x696.
        byte[] golden = Convert.FromHexString("FFFFFFFFFEFFFFFFFDFFFFFFFCFFFFFFAE55F7FF96060000");
        var value = new DmIntervalDT("-1 02:03:04.567890", DmIntervalDT.QUA_DHMS, 9, 6);
        Assert.Equal(golden, value.encode(0));
        var decoded = new DmIntervalDT(golden, 9, 6);
        Assert.Equal(new[] { 1, 2, 3, 4, 567890 }, Components(decoded));
        Assert.Equal(-(((1 * TimeSpan.TicksPerDay) + (2 * TimeSpan.TicksPerHour) + (3 * TimeSpan.TicksPerMinute)
            + (4 * TimeSpan.TicksPerSecond)) + 5_678_900), decoded.ToTimeSpanExact().Ticks);
        Assert.StartsWith("INTERVAL '-", decoded.ToString());
    }

    [Theory]
    [InlineData(DmIntervalDT.QUA_D, "999", 999, 0, 0, 0)]
    [InlineData(DmIntervalDT.QUA_DH, "999 23", 999, 23, 0, 0)]
    [InlineData(DmIntervalDT.QUA_DHM, "999 23:59", 999, 23, 59, 0)]
    [InlineData(DmIntervalDT.QUA_DHMS, "999 23:59:59.123456", 999, 23, 59, 59)]
    [InlineData(DmIntervalDT.QUA_H, "999", 0, 999, 0, 0)]
    [InlineData(DmIntervalDT.QUA_HM, "999:59", 0, 999, 59, 0)]
    [InlineData(DmIntervalDT.QUA_HMS, "999:59:59.123456", 0, 999, 59, 59)]
    [InlineData(DmIntervalDT.QUA_M, "999", 0, 0, 999, 0)]
    [InlineData(DmIntervalDT.QUA_MS, "999:59.123456", 0, 0, 999, 59)]
    [InlineData(DmIntervalDT.QUA_S, "999.123456", 0, 0, 0, 999)]
    public void EveryDayHourMinuteSecondSubtypePreservesItsOwnLeadingUnit(byte subtype, string text, int days, int hours, int minutes, int seconds)
    {
        var value = new DmIntervalDT(text, subtype, 3, 6);
        Assert.Equal(subtype, value.GetDTType());
        Assert.Equal(new[] { days, hours, minutes, seconds }, Components(value).Take(4));
        long expected = ((((long)days * 24 + hours) * 60 + minutes) * 60 + seconds) * TimeSpan.TicksPerSecond
            + (text.Contains('.') ? 1_234_560 : 0);
        Assert.Equal(expected, value.ToTimeSpanExact().Ticks);
        byte[] bytes = value.encode(0);
        Assert.Equal(24, bytes.Length);
        Assert.Equal(Scale(subtype, 3), WireInt(bytes, 5));
        var restored = new DmIntervalDT(bytes, 3, 6);
        Assert.Equal(value.GetDTString(), restored.GetDTString());
        Assert.Equal(expected, restored.ToTimeSpanExact().Ticks);
    }

    [Theory]
    [InlineData(DmIntervalDT.QUA_H, "100000000", 100000000L * TimeSpan.TicksPerHour)]
    [InlineData(DmIntervalDT.QUA_HM, "100000000:59", 100000000L * TimeSpan.TicksPerHour + 59L * TimeSpan.TicksPerMinute)]
    [InlineData(DmIntervalDT.QUA_HMS, "100000000:59:59.000001", 100000000L * TimeSpan.TicksPerHour + 59L * TimeSpan.TicksPerMinute + 59L * TimeSpan.TicksPerSecond + 10)]
    [InlineData(DmIntervalDT.QUA_M, "999999999", 999999999L * TimeSpan.TicksPerMinute)]
    [InlineData(DmIntervalDT.QUA_MS, "999999999:59.000001", 999999999L * TimeSpan.TicksPerMinute + 59L * TimeSpan.TicksPerSecond + 10)]
    [InlineData(DmIntervalDT.QUA_S, "999999999.000001", 999999999L * TimeSpan.TicksPerSecond + 10)]
    public void LargeLeadingValuesNormalizeThroughWideFieldsRatherThanDayClockValidation(byte type, string text, long expected)
    {
        var value = new DmIntervalDT(text, type, 9, 6);
        Assert.Equal(expected, value.ToTimeSpanExact().Ticks);
        Assert.Equal(type, value.GetDTType());
        Assert.Equal(Scale(type), WireInt(value.encode(0), 5));
    }

    [Fact]
    public void ConversionAndEncodeRecomposeTargetTypeAndScaleAtomically()
    {
        var source = new DmIntervalDT("2 03:04:05.123456", DmIntervalDT.QUA_DHMS, 2, 6);
        var converted = source.convertTo(Scale(DmIntervalDT.QUA_HMS, 3));
        Assert.Equal(new[] { 0, 51, 4, 5, 123456 }, Components(converted));
        Assert.Equal(DmIntervalDT.QUA_DHMS, source.GetDTType()); // convertTo leaves source intact.
        byte[] bytes = source.encode(Scale(DmIntervalDT.QUA_HMS, 3));
        Assert.Equal(Scale(DmIntervalDT.QUA_HMS, 3), source.getScaleForSvr());
        Assert.Equal(DmIntervalDT.QUA_HMS, source.GetDTType());
        Assert.Equal(new[] { 0, 51, 4, 5, 123456, Scale(DmIntervalDT.QUA_HMS, 3) }, Enumerable.Range(0, 6).Select(i => WireInt(bytes, i)));
        Assert.Equal("INTERVAL '051:04:05.123456' HOUR(3) TO SECOND(6)", source.ToString());
        var roundTrip = new DmIntervalDT(bytes, 3, 6);
        Assert.Equal(converted.ToTimeSpanExact(), roundTrip.ToTimeSpanExact());
    }

    [Theory]
    [InlineData(DmIntervalDT.QUA_D, "0 12:00:00")]
    [InlineData(DmIntervalDT.QUA_DH, "0 00:30:00")]
    [InlineData(DmIntervalDT.QUA_DHM, "0 00:00:30")]
    [InlineData(DmIntervalDT.QUA_H, "0 00:30:00")]
    [InlineData(DmIntervalDT.QUA_HM, "0 00:00:30")]
    [InlineData(DmIntervalDT.QUA_M, "0 00:00:30")]
    [InlineData(DmIntervalDT.QUA_S, "0 00:00:00.000001")]
    public void NarrowingNeverRoundsHalfUnitsOrDropsFraction(byte target, string text)
    {
        var value = new DmIntervalDT(text);
        int[] before = Components(value);
        int oldScale = value.getScaleForSvr();
        int targetScale = Scale(target, 9, target == DmIntervalDT.QUA_S ? 0 : 6);
        Assert.Throws<OverflowException>(() => value.convertTo(targetScale));
        Assert.Throws<OverflowException>(() => value.encode(targetScale));
        Assert.Equal(before, Components(value));
        Assert.Equal(oldScale, value.getScaleForSvr());
    }

    [Fact]
    public void ExactLowerUnitConversionsAndLeadingChecksDoNotOverflowIntermediateInts()
    {
        var value = new DmIntervalDT("1 00:00:00");
        Assert.Equal(24, value.convertTo(Scale(DmIntervalDT.QUA_H, 2)).hours);
        Assert.Equal(1440, value.convertTo(Scale(DmIntervalDT.QUA_M, 4)).minutes);
        Assert.Equal(86400, value.convertTo(Scale(DmIntervalDT.QUA_S, 5)).seconds);
        Assert.Throws<OverflowException>(() => value.convertTo(Scale(DmIntervalDT.QUA_M, 3)));
        var tooLarge = new DmIntervalDT("999999999", DmIntervalDT.QUA_D, 9, 0);
        Assert.Throws<OverflowException>(() => tooLarge.convertTo(Scale(DmIntervalDT.QUA_H, 9, 0)));
        Assert.Throws<OverflowException>(() => tooLarge.ToTimeSpanExact());
        Assert.Equal("INTERVAL '999999999' DAY(9)", tooLarge.GetDTString());
        var largeHour = new DmIntervalDT("999999999", DmIntervalDT.QUA_H, 9, 0);
        Assert.Equal("INTERVAL '999999999' HOUR(9)", largeHour.GetDTString());
        Assert.Equal(999999999, WireInt(largeHour.encode(0), 1));
        Assert.Throws<OverflowException>(() => largeHour.ToTimeSpanExact());
    }

    [Fact]
    public void ResetReadsEveryWireWordAndClearsPriorSubtypeFieldsOnlyOnSuccess()
    {
        var value = new DmIntervalDT("1 02:03:04.123456");
        value.reset(Wire(0, 123, 0, 0, 0, Scale(DmIntervalDT.QUA_H, 3, 0)));
        Assert.Equal(new[] { 0, 123, 0, 0, 0 }, Components(value));
        Assert.Equal(DmIntervalDT.QUA_H, value.GetDTType());
        byte[] bad = Wire(0, -123, 1, 0, 0, Scale(DmIntervalDT.QUA_HM, 3, 0));
        Assert.Throws<OverflowException>(() => value.reset(bad));
        Assert.Equal(new[] { 0, 123, 0, 0, 0 }, Components(value));
        Assert.Equal(Scale(DmIntervalDT.QUA_H, 3, 0), value.getScaleForSvr());
        Assert.Throws<ArgumentException>(() => value.reset(new byte[23]));
        Assert.Throws<NotSupportedException>(() => value.reset(Wire(0, 0, 0, 0, 0, Scale(DmIntervalDT.QUA_H) | 0x10000)));
    }

    [Fact]
    public void ByteConstructorArgumentsAreConstraintsAndDefaultCannotSilentlyTruncateLargeLeading()
    {
        byte[] wire = Wire(123, 0, 0, 0, 123000, Scale(DmIntervalDT.QUA_DHMS, 3));
        Assert.Throws<OverflowException>(() => new DmIntervalDT(wire));
        var value = new DmIntervalDT(wire, 3, 3);
        Assert.Equal(Scale(DmIntervalDT.QUA_DHMS, 3, 3), value.getScaleForSvr());
        Assert.EndsWith("SECOND(3)", value.GetDTString());
        wire[0] = 99; // DmIntervalDT's public fields, rather than caller raw buffer, are authoritative after construction.
        Assert.Equal(123, value.days);
        Assert.Throws<OverflowException>(() => new DmIntervalDT(Wire(1, 0, 0, 0, 123456, Scale(DmIntervalDT.QUA_DHMS)), 2, 3));
    }

    [Theory]
    [InlineData("INTERVAL '-123:04:05.123456' HOUR(3) TO SECOND(6)", DmIntervalDT.QUA_HMS)]
    [InlineData("INTERVAL '123:04.123' MINUTE(3) TO SECOND(3)", DmIntervalDT.QUA_MS)]
    [InlineData("INTERVAL '123.123456' SECOND(3,6)", DmIntervalDT.QUA_S)]
    [InlineData("INTERVAL '01 02' DAY(2) TO HOUR", DmIntervalDT.QUA_DH)]
    public void DefaultStringConstructorHonorsLiteralDeclarationInsteadOfForcingDayToSecond(string literal, byte subtype)
    {
        var value = new DmIntervalDT(literal);
        Assert.Equal(subtype, value.GetDTType());
        Assert.Equal(literal, value.GetDTString());
    }

    [Theory]
    [InlineData("1", 0, 0, "INTERVAL '01 00:00:00.000000' DAY(2) TO SECOND(6)")]
    [InlineData("1 02", 2, 0, "INTERVAL '01 02:00:00.000000' DAY(2) TO SECOND(6)")]
    [InlineData("1 02:03", 2, 3, "INTERVAL '01 02:03:00.000000' DAY(2) TO SECOND(6)")]
    public void DefaultBareDayToSecondAcceptsContinuousPrefixes(string text, int hours, int minutes, string formatted)
    {
        var value = new DmIntervalDT(text);
        Assert.Equal(new[] { 1, hours, minutes, 0, 0 }, Components(value));
        Assert.Equal(1574, value.getScaleForSvr());
        Assert.Equal(TimeSpan.TicksPerDay + hours * TimeSpan.TicksPerHour + minutes * TimeSpan.TicksPerMinute,
            value.ToTimeSpanExact().Ticks);
        Assert.Equal(formatted, value.GetDTString());
    }

    [Theory]
    [InlineData(DmIntervalDT.QUA_DH, "1", 1, 0, 0, "INTERVAL '001 00' DAY(3) TO HOUR")]
    [InlineData(DmIntervalDT.QUA_DHM, "1", 1, 0, 0, "INTERVAL '001 00:00' DAY(3) TO MINUTE")]
    [InlineData(DmIntervalDT.QUA_DHM, "1 02", 1, 2, 0, "INTERVAL '001 02:00' DAY(3) TO MINUTE")]
    [InlineData(DmIntervalDT.QUA_DHMS, "1", 1, 0, 0, "INTERVAL '001 00:00:00.000000' DAY(3) TO SECOND(6)")]
    [InlineData(DmIntervalDT.QUA_DHMS, "1 02", 1, 2, 0, "INTERVAL '001 02:00:00.000000' DAY(3) TO SECOND(6)")]
    [InlineData(DmIntervalDT.QUA_DHMS, "1 02:03", 1, 2, 3, "INTERVAL '001 02:03:00.000000' DAY(3) TO SECOND(6)")]
    [InlineData(DmIntervalDT.QUA_HM, "123", 0, 123, 0, "INTERVAL '123:00' HOUR(3) TO MINUTE")]
    [InlineData(DmIntervalDT.QUA_HMS, "123", 0, 123, 0, "INTERVAL '123:00:00.000000' HOUR(3) TO SECOND(6)")]
    [InlineData(DmIntervalDT.QUA_HMS, "123:04", 0, 123, 4, "INTERVAL '123:04:00.000000' HOUR(3) TO SECOND(6)")]
    [InlineData(DmIntervalDT.QUA_MS, "123", 0, 0, 123, "INTERVAL '123:00.000000' MINUTE(3) TO SECOND(6)")]
    public void ExplicitCompositeSubtypeAcceptsContinuousPrefixes(byte subtype, string text, int days, int hours, int minutes, string formatted)
    {
        var value = new DmIntervalDT(text, subtype, 3, 6);
        Assert.Equal(new[] { days, hours, minutes, 0, 0 }, Components(value));
        Assert.Equal(Scale(subtype, 3), value.getScaleForSvr());
        Assert.Equal(days * TimeSpan.TicksPerDay + hours * TimeSpan.TicksPerHour + minutes * TimeSpan.TicksPerMinute,
            value.ToTimeSpanExact().Ticks);
        Assert.Equal(formatted, value.GetDTString());
    }

    [Theory]
    [InlineData("INTERVAL '1' DAY(2) TO HOUR", DmIntervalDT.QUA_DH, 2, 1, 0, 0, false,
        "INTERVAL '01 00' DAY(2) TO HOUR")]
    [InlineData("INTERVAL '1 02' DAY(2) TO MINUTE", DmIntervalDT.QUA_DHM, 2, 1, 2, 0, false,
        "INTERVAL '01 02:00' DAY(2) TO MINUTE")]
    [InlineData("INTERVAL '1 02:03' DAY(2) TO SECOND(6)", DmIntervalDT.QUA_DHMS, 2, 1, 2, 3, false,
        "INTERVAL '01 02:03:00.000000' DAY(2) TO SECOND(6)")]
    [InlineData("INTERVAL '123:04' HOUR(3) TO SECOND(6)", DmIntervalDT.QUA_HMS, 3, 0, 123, 4, false,
        "INTERVAL '123:04:00.000000' HOUR(3) TO SECOND(6)")]
    [InlineData("INTERVAL -'1' DAY(2)", DmIntervalDT.QUA_D, 2, 1, 0, 0, true,
        "INTERVAL '-01' DAY(2)")]
    [InlineData("INTERVAL '-1' DAY(2)", DmIntervalDT.QUA_D, 2, 1, 0, 0, true,
        "INTERVAL '-01' DAY(2)")]
    [InlineData("INTERVAL -'123:04' HOUR(3) TO SECOND(6)", DmIntervalDT.QUA_HMS, 3, 0, 123, 4, true,
        "INTERVAL '-123:04:00.000000' HOUR(3) TO SECOND(6)")]
    public void LiteralPrefixesAndSingleExternalNegativeSignWorkInBothConstructors(string literal, byte subtype,
        int leading, int days, int hours, int minutes, bool negative, string formatted)
    {
        long ticks = days * TimeSpan.TicksPerDay + hours * TimeSpan.TicksPerHour + minutes * TimeSpan.TicksPerMinute;
        if (negative) ticks = -ticks;
        foreach (var value in new[] { new DmIntervalDT(literal), new DmIntervalDT(literal, subtype, leading, 6) })
        {
            Assert.Equal(new[] { days, hours, minutes, 0, 0 }, Components(value));
            Assert.Equal(Scale(subtype, leading), value.getScaleForSvr());
            Assert.Equal(ticks, value.ToTimeSpanExact().Ticks);
            Assert.Equal(formatted, value.GetDTString());
        }
    }

    [Fact]
    public void ExplicitLiteralPrefixUsesConstructorPrecisionWhenQualifiersAreOmitted()
    {
        var value = new DmIntervalDT("INTERVAL -'123:04' HOUR TO SECOND", DmIntervalDT.QUA_HMS, 3, 3);
        Assert.Equal(new[] { 0, 123, 4, 0, 0 }, Components(value));
        Assert.Equal(Scale(DmIntervalDT.QUA_HMS, 3, 3), value.getScaleForSvr());
        Assert.Equal(-(123 * TimeSpan.TicksPerHour + 4 * TimeSpan.TicksPerMinute), value.ToTimeSpanExact().Ticks);
        Assert.Equal("INTERVAL '-123:04:00.000' HOUR(3) TO SECOND(3)", value.GetDTString());
        Assert.Throws<InvalidOperationException>(() => new DmIntervalDT("INTERVAL '123:04' HOUR(4) TO SECOND", DmIntervalDT.QUA_HMS, 3, 3));
        Assert.Throws<InvalidOperationException>(() => new DmIntervalDT("INTERVAL '123:04' HOUR TO SECOND(6)", DmIntervalDT.QUA_HMS, 3, 3));
        Assert.Throws<OverflowException>(() => new DmIntervalDT("100", DmIntervalDT.QUA_HMS, 2, 6));
    }

    [Theory]
    [InlineData(DmIntervalDT.QUA_DH, "1 :02")]
    [InlineData(DmIntervalDT.QUA_DH, "1 02:03")]
    [InlineData(DmIntervalDT.QUA_DHM, "1 02:")]
    [InlineData(DmIntervalDT.QUA_DHM, "1 02::03")]
    [InlineData(DmIntervalDT.QUA_DHMS, "1 :03")]
    [InlineData(DmIntervalDT.QUA_DHMS, "1 02::04")]
    [InlineData(DmIntervalDT.QUA_DHMS, "1 02:03:")]
    [InlineData(DmIntervalDT.QUA_DHMS, "1 02:03:04:05")]
    [InlineData(DmIntervalDT.QUA_DHMS, "1.5")]
    [InlineData(DmIntervalDT.QUA_DHMS, "1 02.5")]
    [InlineData(DmIntervalDT.QUA_DHMS, "1 02:03.5")]
    [InlineData(DmIntervalDT.QUA_HM, "123:")]
    [InlineData(DmIntervalDT.QUA_HM, "123::04")]
    [InlineData(DmIntervalDT.QUA_HM, "123:04:05")]
    [InlineData(DmIntervalDT.QUA_HMS, "123::05")]
    [InlineData(DmIntervalDT.QUA_HMS, "123:04:")]
    [InlineData(DmIntervalDT.QUA_HMS, "123:04:05:06")]
    [InlineData(DmIntervalDT.QUA_HMS, "123.5")]
    [InlineData(DmIntervalDT.QUA_HMS, "123:04.5")]
    [InlineData(DmIntervalDT.QUA_MS, "123:")]
    [InlineData(DmIntervalDT.QUA_MS, "123::04")]
    [InlineData(DmIntervalDT.QUA_MS, "123:04:05")]
    [InlineData(DmIntervalDT.QUA_MS, "123.5")]
    public void PrefixCompatibilityStillRejectsHolesExtraSeparatorsAndEarlyFraction(byte subtype, string text)
    {
        Assert.Throws<FormatException>(() => new DmIntervalDT(text, subtype, 3, 6));
    }

    [Theory]
    [InlineData("INTERVAL -'-1' DAY(2)")]
    [InlineData("INTERVAL -'+1' DAY(2)")]
    [InlineData("INTERVAL --'1' DAY(2)")]
    [InlineData("INTERVAL '+-1' DAY(2)")]
    [InlineData("INTERVAL '--1' DAY(2)")]
    [InlineData("INTERVAL '1::02' HOUR(3) TO SECOND(6)")]
    [InlineData("INTERVAL '1:02:' HOUR(3) TO SECOND(6)")]
    [InlineData("INTERVAL '1 02:03.5' DAY(2) TO SECOND(6)")]
    public void LiteralPrefixCompatibilityStillRejectsMultipleSignsAndMalformedComponents(string literal)
    {
        Assert.Throws<FormatException>(() => new DmIntervalDT(literal));
    }

    [Fact]
    public void ExplicitLiteralDeclarationMustMatchAndTextUsesIntegerMicroseconds()
    {
        var value = new DmIntervalDT("INTERVAL '123.123000' SECOND(3,6)", DmIntervalDT.QUA_S, 3, 6);
        Assert.Equal(123000, value.getMsec());
        Assert.Throws<InvalidOperationException>(() => new DmIntervalDT("INTERVAL '123' HOUR(3)", DmIntervalDT.QUA_DHMS, 2, 6));
        Assert.Throws<InvalidOperationException>(() => new DmIntervalDT("INTERVAL '123.123000' SECOND(3,6)", DmIntervalDT.QUA_S, 4, 6));
        Assert.Equal(1, new DmIntervalDT("0.000001000", DmIntervalDT.QUA_S, 1, 6).fraction);
        Assert.Throws<OverflowException>(() => new DmIntervalDT("0.000001001", DmIntervalDT.QUA_S, 1, 6));
        Assert.Throws<OverflowException>(() => new DmIntervalDT("0.000001", DmIntervalDT.QUA_S, 1, 5));
        Assert.Throws<FormatException>(() => new DmIntervalDT("+-'1'", DmIntervalDT.QUA_S, 1, 6));
        Assert.Throws<FormatException>(() => new DmIntervalDT("١.١", DmIntervalDT.QUA_S, 1, 6));
        Assert.Throws<NotSupportedException>(() => new DmIntervalDT("0", DmIntervalDT.QUA_S, 1, 7));
    }

    [Fact]
    public void PublicMutationIsReReadForGettersConversionFormattingAndWire()
    {
        var value = new DmIntervalDT("0 00:00:00.001000");
        value.fraction = 123456;
        Assert.Equal(1234560, value.ToTimeSpanExact().Ticks);
        Assert.Equal(123456, WireInt(value.encode(0), 4));
        Assert.Contains(".123456", value.GetDTString());
        value.fraction = -1;
        Assert.Throws<OverflowException>(() => value.getMsec());
        Assert.Throws<OverflowException>(() => value.GetDTType());
        Assert.Throws<OverflowException>(() => value.convertTo(Scale(DmIntervalDT.QUA_S)));
        Assert.Throws<OverflowException>(() => value.encode(0));
        Assert.Throws<OverflowException>(() => value.GetTimeSpanFormatString());
        value.Clear();
        Assert.Equal(TimeSpan.Zero, value.ToTimeSpanExact());
        value.hours = 24;
        Assert.Throws<OverflowException>(() => value.GetDTString());
        Assert.Throws<OverflowException>(() => value.ToTimeSpanExact());
        Assert.Throws<OverflowException>(() => value.encode(0));
    }

    [Fact]
    public void PublicLeadingUnusedFieldsAndFractionScaleMutationsNeverGetIgnored()
    {
        var hour = new DmIntervalDT("99", DmIntervalDT.QUA_H, 2, 0);
        hour.hours = 100;
        Assert.Throws<OverflowException>(() => hour.getHour());
        Assert.Throws<OverflowException>(() => hour.encode(0));
        hour.hours = 99; hour.days = 1;
        Assert.Throws<OverflowException>(() => hour.ToString());
        Assert.Throws<OverflowException>(() => hour.convertTo(Scale(DmIntervalDT.QUA_DHMS)));
        var scaled = new DmIntervalDT("1.123", DmIntervalDT.QUA_S, 2, 3);
        scaled.fraction = 123456;
        Assert.Throws<OverflowException>(() => scaled.getMsec());
        Assert.Throws<OverflowException>(() => scaled.encode(0));
        var negative = new DmIntervalDT("-1", DmIntervalDT.QUA_H, 2, 0);
        negative.hours = 2;
        Assert.Equal(-2L * TimeSpan.TicksPerHour, negative.ToTimeSpanExact().Ticks);
        Assert.Equal(-2, WireInt(negative.encode(0), 1));
        negative.hours = -2;
        Assert.Throws<OverflowException>(() => negative.GetDTString());
    }

    [Fact]
    public void PublicFoldingMutatorsPreservePrivateDeclarationAndHaveAtomicOverflow()
    {
        var value = new DmIntervalDT(1, 2, 3, 4, 0);
        value.ToHours(); Assert.Equal(new[] { 0, 26, 3, 4, 0 }, Components(value));
        value.ToMinutes(); Assert.Equal(new[] { 0, 0, 1563, 4, 0 }, Components(value));
        value.ToSeconds(); Assert.Equal(new[] { 0, 0, 0, 93784, 0 }, Components(value));
        Assert.Equal(DmIntervalDT.QUA_DHMS, typeof(DmIntervalDT).GetField("type", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value));
        Assert.Equal(1574, typeof(DmIntervalDT).GetField("scaleForSvr", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value));
        Assert.Throws<OverflowException>(() => value.ToTimeSpanExact());
        Assert.Throws<OverflowException>(() => value.encode(0));
        value.Clear(); Assert.Equal(TimeSpan.Zero, value.ToTimeSpanExact());
        value.hours = int.MaxValue; value.days = 1;
        int[] before = Components(value);
        Assert.Throws<OverflowException>(() => value.ToHours()); Assert.Equal(before, Components(value));
        value.Clear(); value.minutes = int.MaxValue; value.hours = 1;
        before = Components(value);
        Assert.Throws<OverflowException>(() => value.ToMinutes()); Assert.Equal(before, Components(value));
        value.Clear(); value.seconds = int.MaxValue; value.minutes = 1;
        before = Components(value);
        Assert.Throws<OverflowException>(() => value.ToSeconds()); Assert.Equal(before, Components(value));
    }

    [Fact]
    public void TimeSpanPrecisionBoundaryUsesAcceptedCoreAndRetainsLegacyCapability()
    {
        foreach (long ticks in new[] { -10L, 0L, 10L, long.MinValue + 8, long.MaxValue - 7 })
        {
            var value = new DmIntervalDT(TimeSpan.FromTicks(ticks), Scale(DmIntervalDT.QUA_DHMS));
            Assert.Equal(ticks, value.ToTimeSpanExact().Ticks);
            Assert.Equal(ticks, new DmIntervalDT(value.encode(0), 9, 6).ToTimeSpanExact().Ticks);
        }
        Assert.Throws<OverflowException>(() => new DmIntervalDT(TimeSpan.MinValue, Scale(DmIntervalDT.QUA_DHMS)));
        Assert.Throws<OverflowException>(() => new DmIntervalDT(TimeSpan.MaxValue, Scale(DmIntervalDT.QUA_DHMS)));
        Assert.Throws<NotSupportedException>(() => new DmIntervalDT(TimeSpan.FromTicks(1), Scale(DmIntervalDT.QUA_DHMS, 9, 7)));
        Assert.Throws<NotSupportedException>(() => new DmIntervalDT(TimeSpan.Zero, Scale(DmIntervalDT.QUA_HMS)));
        Assert.Throws<OverflowException>(() => new DmIntervalDT(TimeSpan.FromDays(100), Scale(DmIntervalDT.QUA_DHMS, 2)));
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public void IntervalTextAndFormatDoNotUseMachineCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var value = new DmIntervalDT("12.123456", DmIntervalDT.QUA_S, 2, 6);
            Assert.Equal("INTERVAL '12.123456' SECOND(2,6)", value.ToString());
            Assert.Equal("00:00:12.1234560", value.GetTimeSpanFormatString());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
