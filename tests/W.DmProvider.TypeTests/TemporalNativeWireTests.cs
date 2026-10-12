using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class TemporalNativeWireTests
{
    [Theory]
    [InlineData(14, "EA87BB")]
    [InlineData(15, "EE8180C403")]
    [InlineData(22, "EE8180C403E001")]
    [InlineData(16, "EA87BBEE8180C403")]
    [InlineData(23, "EA87BBEE8180C403E001")]
    public void IndependentLegacyBitVectorsUseMicroseconds(int cType, string hex)
    {
        DmTemporalWireLayout layout = DmTemporalWireCodec.ForCType(cType);
        // Independent positional integers: T=14+15*2^5+16*2^11+123456*2^17 => EE 81 80 C4 03.
        // 123456=0x1E240: its low seven bits are 0x40, contributing 0x80 in packed byte 2.
        // D=2026+7*2^15+23*2^19 => EA 87 BB; offset 480 => E0 01. These are manual vectors.
        bool time = layout is DmTemporalWireLayout.Time5 or DmTemporalWireLayout.TimeTz7;
        bool tz = layout is DmTemporalWireLayout.TimeTz7 or DmTemporalWireLayout.TimestampTz10;
        bool date = layout == DmTemporalWireLayout.Date3;
        var fields = new DmTemporalFields(time ? 0 : 2026, time ? 0 : 7, time ? 0 : 23,
            date ? 0 : 14, date ? 0 : 15, date ? 0 : 16, date ? 0 : 123456000, tz ? 480 : null);
        Assert.Equal(Convert.FromHexString(hex), DmTemporalWireCodec.EncodeExact(fields, layout, date ? 0 : 6));
        Assert.Equal(fields, DmTemporalWireCodec.DecodeRaw(Convert.FromHexString(hex), layout, date ? 0 : 6));
    }

    [Fact]
    public void PublicHelperAdapterHasOneExplicitMicrosecondUnit()
    {
        var fields = new DmTemporalFields(2026, 7, 23, 14, 15, 16, 123456000, 480);
        byte[] raw = Convert.FromHexString("EA0707170E0F1040E201E001");
        Assert.Equal(raw, DmTemporalWireCodec.ToLegacyMicroseconds(fields, DmTemporalTextKind.Timestamp));
        Assert.Equal(fields, DmTemporalWireCodec.FromLegacyMicroseconds(raw, DmTemporalTextKind.Timestamp, true));
        Assert.Equal(1234560, DmTemporalInteger.FractionalTicksExact(fields.NanosecondsWithinSecond));
        var time = fields with { Year = 0, Month = 0, Day = 0 };
        byte[] timeBuffer = DmTemporalWireCodec.ToLegacyMicroseconds(time, DmTemporalTextKind.Time);
        Assert.Equal(0x6c, timeBuffer[0]); // 1900 = 0x076C historical TIME anchor.
        Assert.Equal(time, DmTemporalWireCodec.FromLegacyMicroseconds(timeBuffer, DmTemporalTextKind.Time, true));
        // Raw 1 means one microsecond regardless of whether 1 could also mean one tick.
        raw[7] = 1; raw[8] = 0; raw[9] = 0;
        Assert.Equal(1000, DmTemporalWireCodec.FromLegacyMicroseconds(raw, DmTemporalTextKind.Timestamp, true).NanosecondsWithinSecond);
        raw[7] = 0x40; raw[8] = 0x42; raw[9] = 0x0f; // 1,000,000 microseconds
        Assert.Throws<OverflowException>(() => DmTemporalWireCodec.FromLegacyMicroseconds(raw, DmTemporalTextKind.Timestamp, true));
        Assert.Throws<OverflowException>(() => DmTemporalWireCodec.ToLegacyMicroseconds(fields with { NanosecondsWithinSecond = 100 }, DmTemporalTextKind.Timestamp));
        Assert.Throws<ArgumentException>(() => DmTemporalWireCodec.FromLegacyMicroseconds(new byte[11], DmTemporalTextKind.Timestamp, true));
        raw = Convert.FromHexString("EA0707170E0F1040E201E001");
        Assert.Throws<OverflowException>(() => DmTemporalWireCodec.FromLegacyMicroseconds(raw, DmTemporalTextKind.Timestamp, false));
        raw[2] = 13;
        Assert.Throws<OverflowException>(() => DmTemporalWireCodec.FromLegacyMicroseconds(raw, DmTemporalTextKind.Time, true));
    }

    [Fact]
    public void EveryLayoutChecksExactLengthBeforeReadingAnyField()
    {
        foreach (DmTemporalWireLayout layout in Enum.GetValues<DmTemporalWireLayout>())
        {
            int length = DmTemporalWireCodec.GetLength(layout);
            foreach (int badLength in new[] { 0, length - 1, length + 1, 12, 13 })
                Assert.Throws<ArgumentException>(() => DmTemporalWireCodec.DecodeRaw(new byte[badLength], layout, 6));
        }
        Assert.Throws<NotSupportedException>(() => DmTemporalWireCodec.GetLength((DmTemporalWireLayout)100));
        Assert.Throws<NotSupportedException>(() => DmTemporalWireCodec.ForCType(21));
    }

    [Fact]
    public void ReservedBitsAndMalformedFractionAreRejectedRatherThanMasked()
    {
        byte[] legacy = Convert.FromHexString("EA87BBEE81000000");
        legacy[7] = 0x20;
        Assert.Throws<OverflowException>(() => DmTemporalWireCodec.DecodeRaw(legacy, DmTemporalWireLayout.Timestamp8, 6));
        byte[] extended = Convert.FromHexString("EA87BBEE81C8000000");
        extended[8] = 0x80;
        Assert.Throws<OverflowException>(() => DmTemporalWireCodec.DecodeRaw(extended, DmTemporalWireLayout.Timestamp9, 9));
        // 20-bit legacy fraction = 1,000,000, independently packed as 80 84 1E after second low bit.
        legacy = Convert.FromHexString("EA87BBEE8180841E");
        Assert.Throws<OverflowException>(() => DmTemporalWireCodec.DecodeRaw(legacy, DmTemporalWireLayout.Timestamp8, 6));
        extended = Convert.FromHexString("EA87BBEE8100943577"); // 1,000,000,000ns
        Assert.Throws<OverflowException>(() => DmTemporalWireCodec.DecodeRaw(extended, DmTemporalWireLayout.Timestamp9, 9));
    }

    [Fact]
    public void InvalidCalendarClockAndOffsetShapesAreRejected()
    {
        var fields = new DmTemporalFields(2026, 7, 23, 14, 15, 16, 0);
        foreach (var bad in new[] { fields with { Year = 0 }, fields with { Month = 0 }, fields with { Month = 13 },
            fields with { Day = 0 }, fields with { Day = 32 }, fields with { Hour = 25 }, fields with { Minute = 60 },
            fields with { Second = 60 }, fields with { NanosecondsWithinSecond = -1 }, fields with { Year = -22769 } })
            Assert.Throws<OverflowException>(() => DmTemporalWireCodec.EncodeExact(bad, DmTemporalWireLayout.Timestamp9, 9));
        Assert.Throws<InvalidCastException>(() => DmTemporalWireCodec.EncodeExact(fields, DmTemporalWireLayout.TimestampTz11, 9));
        Assert.Throws<InvalidCastException>(() => DmTemporalWireCodec.EncodeExact(fields with { OffsetMinutes = 0 }, DmTemporalWireLayout.Timestamp9, 9));
        Assert.Throws<OverflowException>(() => DmTemporalWireCodec.EncodeExact(fields with { OffsetMinutes = 32768 }, DmTemporalWireLayout.TimestampTz11, 9));
        Assert.Throws<OverflowException>(() => DmTemporalWireCodec.EncodeExact(fields, DmTemporalWireLayout.Time5, 6));
        Assert.Throws<NotSupportedException>(() => DmTemporalWireCodec.EncodeExact(fields, DmTemporalWireLayout.Timestamp8, 7));
        Assert.Throws<OverflowException>(() => DmTemporalWireCodec.EncodeExact(fields, DmTemporalWireLayout.Date3, 0));
        byte[] badClock = Convert.FromHexString("EA87BBF981000000"); // hour 25
        Assert.Throws<OverflowException>(() => DmTemporalWireCodec.DecodeRaw(badClock, DmTemporalWireLayout.Timestamp8, 6));
        byte[] badMonth = Convert.FromHexString("EA07B8EE81000000"); // month 0
        Assert.Throws<OverflowException>(() => DmTemporalWireCodec.DecodeRaw(badMonth, DmTemporalWireLayout.Timestamp8, 6));
    }
}
