using System.Buffers.Binary;
using System.Globalization;
using W.Dm;
using W.Dm.Internal.Types;
using W.Dm.util;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class TemporalNativeHelperAdapterTests
{
    [Fact]
    public void LegacyConstructorAndDecoderUseMicrosecondsAndKeepAliases()
    {
        DateTime expected = new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(1234560);
        var constructed = new DmDateTime(expected, 8, 6, 2, 0);
        byte[] raw = constructed.GetByteArrayValue();
        Assert.Equal(123456, raw[7] | (raw[8] << 8) | (raw[9] << 16));
        Assert.Equal(expected.Ticks, constructed.GetTimestamp().Ticks);
        byte[] packed = DmDateTime.DmdtEncodeFast(raw);
        Assert.Equal(Convert.FromHexString("EA87BBEE8180C403"), packed);
        byte[] decoded = DmDateTime.DmDtDecodeFast(packed);
        Assert.Equal(raw, decoded);
        var alias = new DmDateTime(decoded, 8);
        Assert.Same(decoded, alias.GetByteArrayValue());
        byte[] returned = null!; alias.GetByteArrayValue(ref returned);
        Assert.Same(decoded, returned);
        decoded[7] = 1; decoded[8] = 0; decoded[9] = 0;
        Assert.Equal(new DateTime(2026, 7, 23, 14, 15, 16).Ticks + 10, alias.GetTimestamp().Ticks);
        decoded[7] = 0x40; decoded[8] = 0x42; decoded[9] = 0x0f;
        Assert.Throws<OverflowException>(() => alias.GetTimestamp());
    }

    [Fact]
    public void PackedArraysRetainTheirLayoutAndReReadEveryMutation()
    {
        byte[] packed = Convert.FromHexString("EA87BBEE81C8000000");
        var value = new DmDateTime(packed, 9);
        long whole = new DateTime(2026, 7, 23, 14, 15, 16).Ticks;
        Assert.Same(packed, value.GetByteArrayValue());
        byte[] returned = null!; value.GetByteArrayValue(ref returned);
        Assert.Same(packed, returned); Assert.Equal(9, returned.Length);
        Assert.Equal(whole + 1, value.GetTimestamp().Ticks);
        packed[5] = 0x90; packed[6] = 1; // 200ns, independently shifted at bit 41.
        Assert.Equal(whole + 2, value.GetTimestamp().Ticks);
        packed[3] = 0xf9; // hour25: numeric consumption sees the current invalid array.
        Assert.Throws<OverflowException>(() => value.GetTimestamp());
        Assert.Same(packed, value.GetByteArrayValue());
    }

    [Fact]
    public void WholeMicrosecondExtendedPublicProjectionKeepsOffsetAndDoesNotMutateInput()
    {
        byte[] packed = DmTemporalWireCodec.EncodeExact(new(2026, 7, 23, 14, 15, 16, 123456000, -300), DmTemporalWireLayout.TimestampTz11, 7);
        byte[] snapshot = (byte[])packed.Clone();
        byte[] projected = DmDateTime.DmTimeFromRec4(packed, 27);
        Assert.Equal(snapshot, packed); Assert.Equal(12, projected.Length);
        Assert.Equal(123456, projected[7] | projected[8] << 8 | projected[9] << 16);
        Assert.Equal(-300, BinaryPrimitives.ReadInt16LittleEndian(projected.AsSpan(10)));
        Assert.Equal(new DateTimeOffset(2026, 7, 23, 14, 15, 16, TimeSpan.FromMinutes(-300)).AddTicks(1234560), new DmDateTime(projected, 11).GetTimestampTZ());
    }

    [Theory]
    [InlineData("EA87BB")] [InlineData("EE8180C403")] [InlineData("EE8180C403D4FE")]
    [InlineData("EA87BBEE8180C403")] [InlineData("EA87BBEE81C8000000")]
    [InlineData("EA87BBEE8180C403D4FE")] [InlineData("EA87BBEE81C8000000D4FE")]
    public void AllPackedConstructorLayoutsKeepCallerArrayIdentity(string golden)
    {
        byte[] packed = Convert.FromHexString(golden);
        var value = new DmDateTime(packed, packed.Length);
        Assert.Same(packed, value.GetByteArrayValue());
        byte[] returned = null!; value.GetByteArrayValue(ref returned);
        Assert.Same(packed, returned); Assert.Equal(golden.Length / 2, returned.Length);
    }

    [Fact]
    public void ByteAndSemanticRepresentationsNeverMirrorOrSubstitute()
    {
        var semantic = DmDateTime.valueOf("2026-07-23 14:15:16.0000001");
        var column = new DmField("gb18030"); column.SetCType(26); column.SetScale(7);
        Assert.Equal(new DateTime(2026, 7, 23, 14, 15, 16).Ticks + 1, semantic.toTimestamp(column, null!).Ticks);
        Assert.Throws<InvalidOperationException>(() => semantic.GetTimestamp());
        Assert.Throws<InvalidOperationException>(() => semantic.GetByteArrayValue());
        semantic.dt[6] = 200;
        Assert.Equal(new DateTime(2026, 7, 23, 14, 15, 16).Ticks + 2, semantic.toTimestamp(column, null!).Ticks);
        semantic.dt = [2024, 2, 29, 12, 34, 56, 300, int.MinValue];
        Assert.Equal(new DateTime(2024, 2, 29, 12, 34, 56).Ticks + 3, semantic.toTimestamp(column, null!).Ticks);
        semantic.scale = 6;
        Assert.Throws<OverflowException>(() => semantic.toTimestamp(column, null!));
        semantic.scale = 7; semantic.dtype = 999;
        Assert.Throws<InvalidCastException>(() => semantic.toTimestamp(column, null!));
        semantic.dtype = 15;
        Assert.Throws<OverflowException>(() => semantic.toTime(column, null!));
        semantic.dtype = 14; semantic.dt[6] = 0;
        Assert.Throws<OverflowException>(() => semantic.toDate(column, null!));
        semantic.dtype = 26; semantic.dt = [2024];
        Assert.Throws<InvalidOperationException>(() => semantic.toTimestamp(column, null!));
        byte[] bytes = Convert.FromHexString("EA87BBEE81C8000000");
        var both = new DmDateTime(bytes, 9) { dt = [2024, 2, 29, 0, 0, 0, 0, int.MinValue], dtype = 26, scale = 7 };
        Assert.Equal(new DateTime(2024, 2, 29), both.toTimestamp(column, null!));
        Assert.Equal(new DateTime(2026, 7, 23, 14, 15, 16).Ticks + 1, both.GetTimestamp().Ticks);
        both.parseString("2000-01-01 00:00:00.0000002");
        Assert.Same(bytes, both.GetByteArrayValue());
        Assert.Equal(new DateTime(2000, 1, 1).Ticks + 2, both.toTimestamp(column, null!).Ticks);
    }

    [Fact]
    public void TimeFiveAndSevenByteHelpersKeepOffsetAndZeroDateContracts()
    {
        byte[] plain = Convert.FromHexString("EE8180C403");
        var time = new DmTime(plain);
        Assert.Equal(14, time.GetHour()); Assert.Equal(15, time.GetMinute()); Assert.Equal(16, time.GetSecond());
        Assert.Equal(123456, time.GetNano());
        Assert.Throws<InvalidCastException>(() => time.GetTZ());
        Assert.Throws<InvalidCastException>(() => time.GetTzByteArrayValue());
        Assert.Equal(new byte[4], time.GetByteArrayValue()[..4]);
        plain[0] = 0xef; Assert.Equal(15, time.GetHour());
        byte[] zoned = Convert.FromHexString("EE8180C403D4FE");
        var timezone = new DmTime(zoned);
        Assert.Equal(-300, timezone.GetTZ());
        BinaryPrimitives.WriteInt16LittleEndian(zoned.AsSpan(5), 330);
        Assert.Equal(330, timezone.GetTZ());
        Assert.Equal(330, BinaryPrimitives.ReadInt16LittleEndian(timezone.GetTzByteArrayValue().AsSpan(10)));
        Assert.Equal(new byte[4], timezone.GetTzByteArrayValue()[..4]);
        byte[] anchored = DmDateTime.DmTimeDecodeFast(Convert.FromHexString("EE8180C403"));
        Assert.Equal(1900, BinaryPrimitives.ReadInt16LittleEndian(anchored));
        Assert.Equal(new DateTime(1900, 1, 1, 14, 15, 16).Ticks + 1234560, new DmDateTime(anchored, 5).GetTime().Ticks);
        zoned[4] = 0xff;
        Assert.Throws<OverflowException>(() => timezone.GetNano());
    }

    [Fact]
    public void ConstructorsAndFastHelpersRejectLossAndInvalidShapes()
    {
        var tick = new DateTime(9999, 12, 31, 23, 59, 59).AddTicks(9999999);
        Assert.Throws<OverflowException>(() => new DmDateTime(tick, 8, 6, 2, 0));
        Assert.Throws<NotSupportedException>(() => new DmDateTime(tick, 8, 7, 2, 0));
        Assert.Throws<OverflowException>(() => new DmTime(tick, 5, 6, 0));
        Assert.Throws<OverflowException>(() => DmDateTime.GetMicroSecond(tick));
        Assert.Throws<OverflowException>(() => DmTime.TimeEncodeFast(tick));
        Assert.Throws<ArgumentException>(() => new DmDateTime(new byte[4], 0));
        Assert.Throws<ArgumentException>(() => new DmTime(new byte[12]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DmDateTime(DateTime.MinValue, 8, 23, 2, 0));
        Assert.Throws<NotSupportedException>(() => new DmDateTime(DateTime.MinValue, 8, 6, 1, 0));
        Assert.Throws<OverflowException>(() => new DmDateTime(new DateTime(2026, 7, 23, 1, 0, 0), 3, 0, 0, 0));
        Assert.Throws<OverflowException>(() => DmDateTime.DmdtEncodeFast([2026, 7, 23, 14, 15, 16, 1000000, 0, 0]));
        Assert.Throws<ArgumentException>(() => DmDateTime.DmdttzEncodeFast([2026, 7, 23, 14, 15, 16, 0, 0]));
        Assert.Throws<OverflowException>(() => new DmDateTime("2026-07-23 14:15:16.1234567", 8, 2));
        Assert.Throws<InvalidCastException>(() => new DmDateTime("14:15:16.123456", 8, 2));
        Assert.Throws<InvalidCastException>(() => new DmDateTime("2026-07-23 14:15:16.123456 +00:00", 7, 4));
        var exact = new DmDateTime("2026-07-23 14:15:16.1234560", 8, 2);
        Assert.Equal(1234560, exact.GetTimestamp().Ticks % TimeSpan.TicksPerSecond);
    }

    [Fact]
    public void SemanticNsArithmeticAndTemplateFormattingPreserveIntegerUnits()
    {
        int[] fields = [2024, 2, 29, 23, 59, 59, 999999900, int.MinValue];
        Assert.Same(fields, DmDateTime.AddNanoSecond(fields, 100));
        Assert.Equal(new[] { 2024, 3, 1, 0, 0, 0, 0, int.MinValue }, fields);
        DmDateTime.AddNanoSecond(fields, -1);
        Assert.Equal(new[] { 2024, 2, 29, 23, 59, 59, 999999999, int.MinValue }, fields);
        DmDateTime.AddNanoSecond(fields, int.MaxValue);
        Assert.Equal(2, fields[5]); Assert.Equal(147483646, fields[6]);
        int[] source = [2026, 7, 23, 0, 0, 0, 100, 480];
        int[] shifted = DmDateTime.transformTZ(source, 0, -300);
        Assert.NotSame(source, shifted); Assert.Equal(23, source[2]); Assert.Equal(100, shifted[6]);
        Assert.Equal(new[] { 2026, 7, 22, 11, 0, 0, 100, -300 }, shifted);
        Assert.Equal(new DateTime(2026, 7, 23).Ticks + 1, DmDateTime.DmTimeEncodeTz(9, 2026, 7, 23, 0, 0, 0, 1, 0).Ticks);
        Assert.Throws<OverflowException>(() => DmDateTime.DmTimeEncodeTz(9, 0, 0, 0, 0, 0, 0, 0, 0));
        Assert.Equal("000001", OracleDateFormat.Format([2026, 7, 23, 0, 0, 0, 1000, 0, 0, 0, 0], "FF6", 0, ".,"));
        Assert.Equal("000000100", OracleDateFormat.Format([2026, 7, 23, 0, 0, 0, 100, 0, 0, 0, 0], "FF9", 0, ".,"));
        Assert.Throws<OverflowException>(() => N2DB.adjustTime([2026, 7, 23, 0, 0, 0, 100, int.MinValue], 6, "safe"));
        Assert.Throws<OverflowException>(() => DmDateTime.transformTZ([2026, 2, 30, 0, 0, 0, 0, 0], 0, 0));
    }

    [Fact]
    public void FractionTokensOneThroughNineReachIntegerParsingAndFormatting()
    {
        string[] fractions = ["1", "12", "123", "1234", "12345", "123456", "1234567", "12345678", "123456789"];
        int[] expectedNs = [100000000, 120000000, 123000000, 123400000, 123450000, 123456000, 123456700, 123456780, 123456789];
        for (int width = 1; width <= 9; width++)
        {
            string token = "FF" + width.ToString(CultureInfo.InvariantCulture);
            int[] parsed = OracleDateFormat.ParseTemporal("2026-07-23 14:15:16." + fractions[width - 1],
                "YYYY-MM-DD HH24:MI:SS." + token, 0, 16);
            Assert.Equal(expectedNs[width - 1], parsed[6]);
            Assert.Equal(fractions[width - 1], OracleDateFormat.Format(parsed, token, 0, ".,"));
            int[] shorter = OracleDateFormat.ParseTemporal("2026-07-23 14:15:16.1",
                "YYYY-MM-DD HH24:MI:SS." + token, 0, 16);
            Assert.Equal(100000000, shorter[6]);
            Assert.Equal("1" + new string('0', width - 1), OracleDateFormat.Format(shorter, token, 0, ".,"));
        }
        int[] bare = OracleDateFormat.ParseTemporal("2026-07-23 14:15:16.000001", "YYYY-MM-DD HH24:MI:SS.FF", 0, 16);
        Assert.Equal(1000, bare[6]);
        Assert.Equal("000001", OracleDateFormat.Format(bare, "FF", 0, ".,"));
    }

    [Theory]
    [InlineData("FF7", "0000001", 100)]
    [InlineData("ff8", "00000001", 10)]
    [InlineData("Ff9", "000000001", 1)]
    public void ExtendedFractionTokensKeepLeadingZeroAndRawSubTickUnits(string token, string fraction, int ns)
    {
        int[] parsed = OracleDateFormat.ParseTemporal("2026-07-23 14:15:16." + fraction, "YYYY-MM-DD HH24:MI:SS." + token, 0, 16);
        Assert.Equal(ns, parsed[6]);
        Assert.Equal(fraction, OracleDateFormat.Format(parsed, token, 0, ".,"));
    }

    [Theory]
    [InlineData("FF0")] [InlineData("FF10")] [InlineData("FF11")] [InlineData("FF99")]
    [InlineData("FF７")] [InlineData("FF٧")]
    public void InvalidFractionTokenWidthsRemainRejectedByTheLexer(string token)
    {
        Assert.Equal(6015, Assert.Throws<DmException>(() => OracleDateFormat.Format(
            [2026, 7, 23, 14, 15, 16, 0, int.MinValue], token, 0, ".,")).Number);
        Assert.Equal(6015, Assert.Throws<DmException>(() => OracleDateFormat.ParseTemporal(
            "2026-07-23 14:15:16.1", "YYYY-MM-DD HH24:MI:SS." + token, 0, 16)).Number);
    }

    [Fact]
    public void StrictFractionParsingRejectsDigitsBeyondItsExplicitWidth()
    {
        Assert.Throws<FormatException>(() => OracleDateFormat.ParseTemporal("2026-07-23 14:15:16.1234", "YYYY-MM-DD HH24:MI:SS.FF3", 0, 16));
        Assert.Throws<FormatException>(() => OracleDateFormat.ParseTemporal("2026-07-23 14:15:16.1234567890", "YYYY-MM-DD HH24:MI:SS.FF9", 0, 16));
    }

    [Fact]
    public void LargeSignedCalendarIncrementsUseDayQuotientsBeforeNanoseconds()
    {
        foreach (var vector in new[]
        {
            (Minutes: 200000000, Year: 2406, Month: 10, Day: 27, Hour: 21, Minute: 20),
            (Minutes: int.MaxValue, Year: 6109, Month: 8, Day: 15, Hour: 2, Minute: 7),
            (Minutes: -200000000, Year: 1646, Month: 4, Day: 17, Hour: 2, Minute: 40)
        })
        {
            int[] values = [2026, 7, 23, 0, 0, 0, 123456789, -300];
            Assert.Same(values, DmDateTime.AddMinute(values, vector.Minutes));
            Assert.Equal(new[] { vector.Year, vector.Month, vector.Day, vector.Hour, vector.Minute, 0, 123456789, -300 }, values);
        }
        int year = 2026, month = 7, day = 23;
        DmDateTime.DmTimeDdateAdd(ref year, ref month, ref day, 200000);
        Assert.Equal((2574, 2, 20), (year, month, day));
        year = 2574; month = 2; day = 20;
        DmDateTime.DmTimeDdateAdd(ref year, ref month, ref day, -200000);
        Assert.Equal((2026, 7, 23), (year, month, day));
        int[] negativeDay = [2574, 2, 20, 12, 34, 56, 987654321, 330];
        Assert.Same(negativeDay, DmTemporalLegacyAdapter.AddClock(negativeDay, -200000, 1));
        Assert.Equal(new[] { 2026, 7, 23, 12, 34, 56, 987654321, 330 }, negativeDay);
    }

    [Fact]
    public void CalendarRangeFailuresPreserveAllCallerFieldsAndRefDates()
    {
        int[] maximum = [int.MaxValue, 12, 31, 23, 59, 59, 999999999, 840];
        int[] before = (int[])maximum.Clone();
        Assert.Throws<OverflowException>(() => DmDateTime.AddNanoSecond(maximum, 1));
        Assert.Equal(before, maximum);
        int[] minimum = [int.MinValue, 1, 1, 0, 0, 0, 0, -300];
        before = (int[])minimum.Clone();
        Assert.Throws<OverflowException>(() => DmDateTime.AddMinute(minimum, -1));
        Assert.Equal(before, minimum);
        int year = int.MaxValue, month = 12, day = 31;
        Assert.Throws<OverflowException>(() => DmDateTime.DmTimeDdateAdd(ref year, ref month, ref day, 1));
        Assert.Equal((int.MaxValue, 12, 31), (year, month, day));
        int[] valid = [2026, 7, 23, 0, 0, 0, 1, 480];
        before = (int[])valid.Clone();
        Assert.Throws<OverflowException>(() => DmTemporalLegacyAdapter.AddClock(valid, long.MaxValue, 1));
        Assert.Equal(before, valid);
        int[] boundary = [-1, 12, 31, 23, 59, 59, 999999999, 480];
        DmDateTime.AddNanoSecond(boundary, 1);
        Assert.Equal(new[] { 1, 1, 1, 0, 0, 0, 0, 480 }, boundary);
        int[] leap = [-4, 2, 28, 23, 59, 59, 999999999, 0];
        DmDateTime.AddNanoSecond(leap, 1);
        Assert.Equal(new[] { -4, 2, 29, 0, 0, 0, 0, 0 }, leap);
    }

    [Fact]
    public void ExplicitDisplayTemplatesStillProjectOnlyTheirRequestedFields()
    {
        int[] values = [2026, 7, 23, 14, 15, 16, 123456700, 330, 0, 0, 0];
        Assert.Equal("14:15:16", OracleDateFormat.Format(values, "HH24:MI:SS", 0, ".,"));
        Assert.Equal("2026-07-23 14:15:16.123456", OracleDateFormat.Format(values, "YYYY-MM-DD HH24:MI:SS.FF6", 0, ".,"));
        Assert.Equal(330, values[7]); Assert.Equal(123456700, values[6]);
    }

    [Fact]
    public void AsciiTextAndCultureCannotBypassFractionOrInventMissingFields()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "zh-CN", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var value = DmDateTime.valueOf("2026/07/23 14:15:16.1234567 -05:00");
                Assert.Equal(123456700, value.dt[6]); Assert.Equal(-300, value.dt[7]);
                Assert.Throws<FormatException>(() => DmDateTime.valueOf("2026-07-23 14:15:16.１２３"));
                Assert.Throws<FormatException>(() => DmDateTime.valueOf("14:15"));
                Assert.Throws<NotSupportedException>(() => OracleDateFormat.ParseTemporal("14:15:16", "HH24:MI:SS", 0, 16));
                Assert.Throws<FormatException>(() => OracleDateFormat.ParseTemporal("2026-07-23 14:15", "YYYY-MM-DD HH24:MI:SS", 0, 16));
            }
        }
        finally { CultureInfo.CurrentCulture = saved; }
        var error = Assert.Throws<FormatException>(() => DmDateTime.valueOf("private-temporal-input"));
        Assert.DoesNotContain("private-temporal-input", error.Message);
    }
}
