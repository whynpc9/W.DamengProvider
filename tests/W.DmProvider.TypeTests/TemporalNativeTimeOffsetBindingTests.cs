using System.Buffers.Binary;
using W.Dm;
using W.Dm.Internal.Types;
using Xunit;
using BindingFixture = W.DmProvider.TypeTests.TemporalNativeBindingContractTests.BindingFixture;
using WriterFixture = W.DmProvider.TypeTests.TemporalNativeWriterAdapterTests.WriterFixture;

namespace W.DmProvider.TypeTests;

public sealed class TemporalNativeTimeOffsetBindingTests
{
    public static IEnumerable<object[]> TimeOffsetCases()
    {
        foreach (bool fixedDescribe in new[] { false, true })
        foreach (int year in new[] { 1, 1582, 9999 })
        foreach (int minutes in new[] { -779, -30, 0, 330, 840 })
            yield return [fixedDescribe, year, minutes];
    }

    [Theory]
    [MemberData(nameof(TimeOffsetCases))]
    public async Task ExplicitTimeOffsetBindsLocalClockAndOffsetWithoutItsCalendar(bool fixedDescribe, int year, int minutes)
    {
        var fixture = new BindingFixture();
        await fixture.Describe(fixedDescribe ? [new(22, 7, 6, 1)] : [new(2, 64, 0, 2)]);
        DateTime local = year == 1582 ? new(1582, 10, 10, 14, 15, 16) : new(year, 12, 30, 14, 15, 16);
        DateTimeOffset value = new DateTimeOffset(local, TimeSpan.FromMinutes(minutes)).AddTicks(1234560);
        using var plan = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", value) { DmSqlType = DmDbType.TimeOffset });

        var binding = Assert.Single(plan.TemporalBindings);
        Assert.Equal(22, binding.CType); Assert.Equal(6, binding.EncodedScale);
        Assert.Equal(fixedDescribe, binding.ReliableFixed);
        byte[] packet = fixture.Packet();
        Assert.Equal(22, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(65)));
        Assert.Equal(7, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(69)));
        Assert.Equal(6, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(73)));
        Assert.Equal(7, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(77)));
        if (minutes == -30) Assert.Equal(Convert.FromHexString("EE8180C403E2FF"), packet[79..]);
        var fields = DmTemporalWireCodec.DecodeRaw(packet[79..], DmTemporalWireLayout.TimeTz7, 6);
        Assert.Equal(new DmTemporalFields(0, 0, 0, 14, 15, 16, 123456000, minutes), fields);
        Assert.Equal(value.TimeOfDay, DmTemporalInteger.ProjectTimeOffset(fields).TimeOfDay);
        Assert.Equal(value.Offset, DmTemporalInteger.ProjectTimeOffset(fields).Offset);

        // The compatibility entry and old fixed SetTimeTZ route preserve the original DTO.
        Assert.Equal(value, Assert.IsType<DateTimeOffset>(DmTemporalCodec.NormalizeForWire(value, 22, 6)));
        var writer = new WriterFixture("8.1.5.60", 21, "UTF-8");
        Assert.Equal(packet[79..], writer.Write(value, writer.Parameter(22, 6, fixedSource: true)));
    }

    [Theory]
    [InlineData(0L)] [InlineData(863999999990L)]
    public async Task MidnightAndLastMicrosecondStayExact(long clockTicks)
    {
        var fixture = new BindingFixture(); await fixture.Describe([new(22, 7, 6, 1)]);
        var value = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.FromHours(14)).AddTicks(clockTicks);
        using var plan = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", value) { DmSqlType = DmDbType.TimeOffset, Scale = 6 });
        var fields = DmTemporalWireCodec.DecodeRaw(fixture.Fields[0].GetInValue(0), DmTemporalWireLayout.TimeTz7, 6);
        Assert.Equal(clockTicks, DmTemporalInteger.ProjectTimeOffset(fields).TimeOfDay.Ticks);
        Assert.Equal(840, fields.OffsetMinutes);
    }

    [Theory]
    [InlineData(3, 3, 1234560L, 0, false)]
    [InlineData(6, 6, 1234561L, 0, false)]
    [InlineData(6, 6, 0L, -780, false)]
    [InlineData(6, 6, 0L, -840, false)]
    [InlineData(7, 7, 0L, 0, true)]
    [InlineData(6, 7, 0L, 0, true)]
    public async Task TimeOffsetStillRejectsUnrepresentablePrecisionAndServerOffsets(int columnScale, int scale, long fractionTicks, int minutes, bool unsupportedScale)
    {
        var fixture = new BindingFixture(); await fixture.Describe([new(22, 7, columnScale, 1)]);
        var value = new DateTimeOffset(2026, 7, 23, 14, 15, 16, TimeSpan.FromMinutes(minutes)).AddTicks(fractionTicks);
        var parameter = new DmParameter("p", value) { DmSqlType = DmDbType.TimeOffset, Scale = (byte)scale };
        if (unsupportedScale)
        {
            Assert.Throws<NotSupportedException>(() => fixture.Bind("SELECT :p FROM DUAL", parameter));
            Assert.Throws<NotSupportedException>(() => DmTemporalCodec.NormalizeForWire(value, 22, scale));
        }
        else
        {
            Assert.Throws<OverflowException>(() => fixture.Bind("SELECT :p FROM DUAL", parameter));
            Assert.Throws<OverflowException>(() => DmTemporalCodec.NormalizeForWire(value, 22, scale));
        }
        Assert.False(fixture.Fields[0].GetParamValue()[0].GetInDataBound());
        Assert.Null(fixture.Fields[0].GetParamValue()[0].m_InValue);
    }

    [Theory]
    [InlineData(DmDbType.Date, 14, 0)] [InlineData(DmDbType.Time, 15, 6)]
    [InlineData(DmDbType.DateTime, 16, 6)] [InlineData(DmDbType.DateTime, 26, 6)]
    public async Task DateTimeOffsetStillRequiresTimezoneTargets(DmDbType providerType, int type, int scale)
    {
        var fixture = new BindingFixture();
        await fixture.Describe([new(type, DmTemporalWireCodec.GetLength(DmTemporalWireCodec.ForCType(type)), scale, 1)]);
        var value = new DateTimeOffset(2026, 7, 23, 14, 15, 16, TimeSpan.FromMinutes(-30));
        Assert.Throws<NotSupportedException>(() => fixture.Bind("SELECT :p FROM DUAL",
            new DmParameter("p", value) { DmSqlType = providerType, Scale = (byte)scale }));
        Assert.Throws<NotSupportedException>(() => DmTemporalCodec.NormalizeForWire(value, type, scale));
    }

    [Theory]
    [InlineData(23, 6)] [InlineData(27, 7)]
    public async Task TimestampOffsetKeepsItsCalendar(int type, int scale)
    {
        var fixture = new BindingFixture();
        var layout = DmTemporalWireCodec.ForCType(type);
        await fixture.Describe([new(type, DmTemporalWireCodec.GetLength(layout), scale, 1)]);
        var value = new DateTimeOffset(2026, 7, 23, 14, 15, 16, TimeSpan.FromMinutes(-30)).AddTicks(1234560);
        using var plan = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", value) { DmSqlType = DmDbType.DateTimeOffset, Scale = (byte)scale });
        Assert.Equal(value, DmTemporalInteger.ProjectDateTimeOffset(DmTemporalWireCodec.DecodeRaw(fixture.Fields[0].GetInValue(0), layout, scale)));
    }
}
