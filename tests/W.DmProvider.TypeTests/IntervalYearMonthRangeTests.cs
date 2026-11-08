using System.Buffers.Binary;
using W.Dm;
using Xunit;
using static W.DmProvider.TypeTests.TemporalNativeBindingContractTests;

namespace W.DmProvider.TypeTests;

public sealed class IntervalYearMonthRangeTests
{
    private const string Sql = "SELECT :p FROM DUAL";

    [Theory]
    [InlineData(51539607552L, 9)]
    [InlineData(-51539607552L, 9)]
    [InlineData(long.MinValue, 9)]
    [InlineData(long.MaxValue, 9)]
    [InlineData(25769803776L, 9)]
    [InlineData(-25769803776L, 9)]
    [InlineData(12000000000L, 9)]
    [InlineData(-12000000000L, 9)]
    [InlineData(120L, 1)]
    [InlineData(-120L, 1)]
    [InlineData(0L, -1)]
    [InlineData(12L, 0)]
    [InlineData(-12L, 0)]
    [InlineData(25769803776L, 10)]
    [InlineData(-25769803776L, 10)]
    public void ConstructorRejectsUnrepresentableMonths(long months, int leading)
    {
        var error = Assert.Throws<DmException>(() => new DmIntervalYM(months, leading));
        Assert.Equal(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL, error.Number);
    }

    [Theory]
    [InlineData(11L, 0, 0, 11)]
    [InlineData(-11L, 0, 0, 11)]
    [InlineData(25769803775L, 10, int.MaxValue, 11)]
    [InlineData(-25769803775L, 10, int.MaxValue, 11)]
    public void ConstructorPreservesRepresentableMagnitudesOutsideBindingPrecision(
        long months, int leading, int yearMagnitude, int monthMagnitude)
    {
        var interval = new DmIntervalYM(months, leading);
        Assert.Equal(yearMagnitude, interval.getYear());
        Assert.Equal(monthMagnitude, interval.getMonth());
    }

    [Theory]
    [InlineData(0L, 0x190, 0, 0)]
    [InlineData(1L, 0x190, 0, 1)]
    [InlineData(-1L, 0x190, 0, -1)]
    [InlineData(11L, 0x190, 0, 11)]
    [InlineData(-11L, 0x190, 0, -11)]
    [InlineData(12L, 0x190, 1, 0)]
    [InlineData(-12L, 0x190, -1, 0)]
    [InlineData(13L, 0x190, 1, 1)]
    [InlineData(-13L, 0x190, -1, -1)]
    [InlineData(11999999999L, 0x190, 999999999, 11)]
    [InlineData(-11999999999L, 0x190, -999999999, -11)]
    [InlineData(119L, 0x110, 9, 11)]
    [InlineData(-119L, 0x110, -9, -11)]
    public async Task ConstructorAndFixedBindingEncodeExactSignedFields(
        long months, int packedScale, int signedYears, int signedMonths)
    {
        var interval = new DmIntervalYM(months, (packedScale >> 4) & 0xF);
        Assert.Equal(Math.Abs(signedYears), interval.getYear());
        Assert.Equal(Math.Abs(signedMonths), interval.getMonth());
        AssertIntervalPayload(interval.encode(packedScale), packedScale, signedYears, signedMonths);

        var fixture = new BindingFixture();
        await fixture.Describe([new(20, 12, packedScale, 1)]);
        var parameter = new DmParameter("p", months) { DmSqlType = DmDbType.IntervalYearToMonth };
        using var plan = fixture.Bind(Sql, parameter);
        Assert.True(Assert.Single(plan.TemporalBindings).ReliableFixed);
        AssertExecuteInterval(fixture.Packet(), packedScale, signedYears, signedMonths);
        Assert.Equal(months, Assert.IsType<long>(parameter.Value));
    }

    [Theory]
    [InlineData(51539607552L, 0x190)]
    [InlineData(-51539607552L, 0x190)]
    [InlineData(long.MinValue, 0x190)]
    [InlineData(long.MaxValue, 0x190)]
    [InlineData(25769803776L, 0x190)]
    [InlineData(-25769803776L, 0x190)]
    [InlineData(12000000000L, 0x190)]
    [InlineData(-12000000000L, 0x190)]
    [InlineData(120L, 0x110)]
    [InlineData(-120L, 0x110)]
    public async Task FixedBindingRejectsBeforePublishingBytesAndCanBindAgain(long months, int packedScale)
    {
        var fixture = new BindingFixture();
        await fixture.Describe([new(20, 12, packedScale, 1)]);
        var rejected = new DmParameter("p", months) { DmSqlType = DmDbType.IntervalYearToMonth };

        var error = Assert.Throws<DmException>(() => { using var plan = fixture.Bind(Sql, rejected); });
        Assert.Equal(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL, error.Number);
        Assert.Null(fixture.Fields[0].TemporalBinding);
        Assert.False(fixture.Fields[0].GetInDataBound());
        Assert.Null(fixture.Fields[0].GetInBytes(0));
        Assert.Equal(months, Assert.IsType<long>(rejected.Value));

        var valid = new DmParameter("p", -13L) { DmSqlType = DmDbType.IntervalYearToMonth };
        using var recovered = fixture.Bind(Sql, valid);
        AssertExecuteInterval(fixture.Packet(), packedScale, -1, -1);
        Assert.Equal(-13L, Assert.IsType<long>(valid.Value));
    }

    private static void AssertExecuteInterval(byte[] packet, int packedScale, int years, int months)
    {
        Assert.Equal(20, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(65)));
        Assert.Equal(packedScale, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(73)));
        Assert.Equal(12, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(77)));
        AssertIntervalPayload(packet.AsSpan(79), packedScale, years, months);
    }

    private static void AssertIntervalPayload(ReadOnlySpan<byte> payload, int packedScale, int years, int months)
    {
        Assert.Equal(12, payload.Length);
        Assert.Equal(years, BinaryPrimitives.ReadInt32LittleEndian(payload));
        Assert.Equal(months, BinaryPrimitives.ReadInt32LittleEndian(payload[4..]));
        Assert.Equal(packedScale, BinaryPrimitives.ReadInt32LittleEndian(payload[8..]));
    }
}
