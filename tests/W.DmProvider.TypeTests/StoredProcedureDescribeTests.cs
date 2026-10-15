using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class StoredProcedureDescribeTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task OutputYearMonthPreservesRawDescriptorAndIgnoresUnusedFraction(bool asyncRead, byte flag)
    {
        var fixture = new TemporalNativeBindingContractTests.BindingFixture();
        await fixture.Describe([new(20, 12, 0x146, flag, 1)], asyncRead);
        var field = Assert.Single(fixture.Fields);
        Assert.Equal(0x146, field.TemporalServerMetadata!.Value.Scale);
        Assert.Equal(0x140, field.GetScale());
        var descriptor = field.GetTemporalReadDescriptor(DmTemporalResultFormat.OrdinaryPacked);
        Assert.Equal(0, descriptor.FractionalScale);
        Assert.Equal(1, descriptor.IntervalSubtype);
        Assert.Equal(4, descriptor.IntervalLeadingPrecision);
    }

    [Theory]
    [InlineData(0x146, 0)]
    [InlineData(0x146, 2)]
    [InlineData(0x14F, 1)]
    public async Task UnsupportedYearMonthDeclarationsRemainRejected(int scale, short direction)
    {
        var fixture = new TemporalNativeBindingContractTests.BindingFixture();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Describe([new(20, 12, scale, 1, direction)]));
    }
}
