using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class TemporalNativeIntervalTests
{
    [Fact]
    public void WideMagnitudeHandlesTimeSpanMinAndMaxWithoutAbsOverflow()
    {
        // long.MinValue magnitude=9,223,372,036,854,775,808 ticks:
        // 10,675,199 days + 02:48:05 + 4,775,808 ticks; Max has 4,775,807 ticks.
        var min = new DmTemporalIntervalFields(true, 10675199, 2, 48, 5, 477580800);
        var max = new DmTemporalIntervalFields(false, 10675199, 2, 48, 5, 477580700);
        Assert.Equal(min, DmTemporalIntervalFields.FromTimeSpanExact(TimeSpan.MinValue, 7, 8));
        Assert.Equal(max, DmTemporalIntervalFields.FromTimeSpanExact(TimeSpan.MaxValue, 7, 8));
        Assert.Equal(long.MinValue, min.ToTimeSpanExact(7, 8).Ticks);
        Assert.Equal(long.MaxValue, max.ToTimeSpanExact(7, 8).Ticks);
        Assert.Throws<OverflowException>(() => (min with { Negative = false }).ToTimeSpanExact(7, 8));
        Assert.Throws<OverflowException>(() => DmTemporalIntervalFields.FromTimeSpanExact(TimeSpan.MinValue, 6, 8));
        Assert.Throws<OverflowException>(() => DmTemporalIntervalFields.FromTimeSpanExact(TimeSpan.MaxValue, 7, 7));
    }

    [Theory]
    [InlineData(-10, 6, true, 1000)] [InlineData(-1, 7, true, 100)]
    [InlineData(0, 0, false, 0)] [InlineData(1, 7, false, 100)] [InlineData(10, 6, false, 1000)]
    public void IntervalSignAndScaleAreIntegerExact(long ticks, int scale, bool negative, int ns)
    {
        var fields = new DmTemporalIntervalFields(negative, 0, 0, 0, 0, ns);
        Assert.Equal(fields, DmTemporalIntervalFields.FromTimeSpanExact(TimeSpan.FromTicks(ticks), scale, 1));
        Assert.Equal(ticks, fields.ToTimeSpanExact(scale, 1).Ticks);
    }

    [Fact]
    public void NegativeTickRemainderAndLeadingPrecisionAreNotTruncated()
    {
        Assert.Throws<OverflowException>(() => DmTemporalInteger.ValidateFractionalTicks(long.MinValue, 6));
        Assert.Throws<OverflowException>(() => DmTemporalInteger.ValidateFractionalTicks(-1, 6));
        DmTemporalInteger.ValidateFractionalTicks(-10, 6);
        DmTemporalInteger.ValidateFractionalTicks(long.MinValue, 7);
        DmTemporalInteger.ValidateFractionalTicks(long.MinValue, 9);
        Assert.Throws<OverflowException>(() => new DmTemporalIntervalFields(false, 100, 0, 0, 0, 0).ToTimeSpanExact(6, 2));
        Assert.Equal(99 * TimeSpan.TicksPerDay, new DmTemporalIntervalFields(false, 99, 0, 0, 0, 0).ToTimeSpanExact(6, 2).Ticks);
        Assert.Throws<OverflowException>(() => new DmTemporalIntervalFields(false, 0, 24, 0, 0, 0).ToTimeSpanExact(6, 2));
        Assert.Throws<OverflowException>(() => new DmTemporalIntervalFields(true, -1, 0, 0, 0, 0).ToTimeSpanExact(6, 2));
        Assert.Throws<OverflowException>(() => new DmTemporalIntervalFields(false, 0, 0, 0, 0, 1).ToTimeSpanExact(9, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DmTemporalIntervalFields(false, 0, 0, 0, 0, 0).ToTimeSpanExact(6, 0));
    }
}
