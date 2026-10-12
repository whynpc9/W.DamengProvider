using System.IO;
using W.Dm;
using W.Dm.Internal.Types;
using Xunit;
using Fixture = W.DmProvider.TypeTests.TemporalNativeBindingContractTests.BindingFixture;
using Describe = W.DmProvider.TypeTests.TemporalNativeBindingContractTests.DescribeField;

namespace W.DmProvider.TypeTests;

public sealed class TemporalNativeMetadataContractTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EveryParameterDescribeCapturesRawBeforeNumericNormalization(bool asyncRead)
    {
        var fixture = new Fixture();
        await fixture.Describe([new(9, 50, 129, 1), new(2, 32767, 0, 2), new(16, 8, 0x1006, 1)], asyncRead);
        var numeric = fixture.Fields[0];
        Assert.Equal(50, numeric.TemporalServerMetadata!.Value.Precision);
        Assert.Equal(129, numeric.TemporalServerMetadata.Value.Scale);
        // Preserve the original parameter decoder's GetPrec() normalization: it reads
        // the new DmParamValue's zero precision, unlike ResultColumn's field precision.
        Assert.Equal(0, numeric.GetPrecision()); Assert.Equal(0, numeric.GetScale());
        Assert.True(numeric.TemporalServerMetadata.Value.HasServerSource);
        Assert.Equal(2, fixture.Fields[1].TemporalServerMetadata!.Value.CType);
        var stamp = fixture.Fields[2];
        Assert.Equal(0x1006, stamp.TemporalServerMetadata!.Value.Scale);
        Assert.Equal(0, stamp.TemporalServerMetadata.Value.MaskBeforeNormalization);
        Assert.Equal(4, stamp.TemporalServerMetadata.Value.DerivedMask);
        Assert.Equal(6, stamp.GetScale()); Assert.Equal(4, stamp.mask);
        var descriptor = stamp.GetTemporalReadDescriptor(DmTemporalResultFormat.Bdta);
        Assert.Equal(6, descriptor.FractionalScale); Assert.Equal(4, descriptor.Mask);
        Assert.Equal(DmTemporalResultFormat.Unknown, descriptor.RawMetadata!.Value.ResultFormat);
        Assert.Equal(DmTemporalResultFormat.Bdta, descriptor.ActualFormat);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task OnlyTemporalResultColumnsReceiveSnapshotAndNumericBehaviorIsRetained(bool asyncRead)
    {
        var fixture = new Fixture();
        await fixture.DescribeResults([new(2, 64, 0, 1), new(9, 50, 129, 1),
            new(26, 9, 0x2000, 1), new(23, 10, 9, 1), new(21, 24, 0x696, 1)], asyncRead);
        Assert.Null(fixture.Columns[0].TemporalServerMetadata);
        Assert.Null(fixture.Columns[1].TemporalServerMetadata);
        Assert.Equal(50, fixture.Columns[1].originalPrec); Assert.Equal(16, fixture.Columns[1].GetPrecision());
        Assert.Equal(-1, fixture.Columns[1].GetScale()); Assert.Equal(2, fixture.Columns[1].mask);
        var oracle = fixture.Columns[2];
        Assert.Equal(0x2000, oracle.TemporalServerMetadata!.Value.Scale);
        Assert.Equal(DmTemporalMetadataSource.ResultColumn, oracle.TemporalServerMetadata.Value.Source);
        Assert.Equal(0, oracle.TemporalServerMetadata.Value.TypeFlag);
        var descriptor = oracle.GetTemporalReadDescriptor(DmTemporalResultFormat.OrdinaryPacked);
        Assert.Equal(1, descriptor.Mask); Assert.Equal(26, descriptor.CType); Assert.Equal(0, descriptor.FractionalScale);
        Assert.Equal(9, fixture.Columns[3].GetTemporalReadDescriptor(DmTemporalResultFormat.OrdinaryPacked).FractionalScale);
        var interval = fixture.Columns[4].GetTemporalReadDescriptor(DmTemporalResultFormat.OrdinaryPacked);
        Assert.Equal(0x696, interval.WireScale); Assert.Equal(6, interval.FractionalScale);
        Assert.Equal(9, interval.IntervalLeadingPrecision); Assert.Equal(6, interval.IntervalSubtype);
    }

    [Fact]
    public async Task RebindingAndEffectiveSettersCannotRewriteRawAndNewDescribeClearsOldState()
    {
        var fixture = new Fixture(); await fixture.Describe([new(2, 64, 0, 2)]);
        var field = fixture.Fields[0]; var raw = field.TemporalServerMetadata;
        using (var plan = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", DateTime.MinValue)))
            Assert.NotNull(field.TemporalBinding);
        field.SetScale(3); field.SetPrecision(11); field.SetCType(27); field.SetTypeFlag(1);
        Assert.Equal(raw, field.TemporalServerMetadata);
        field.resetType(26); Assert.Equal(raw, field.TemporalServerMetadata);
        await fixture.Describe([new(16, 8, 0x1006, 1)]);
        Assert.Null(fixture.Fields[0].TemporalBinding); Assert.Equal(4, fixture.Fields[0].mask);
        await fixture.Describe([new(2, 64, 0, 2)]);
        Assert.Equal(0, fixture.Fields[0].mask); Assert.Equal(2, fixture.Fields[0].TemporalServerMetadata!.Value.CType);
        await fixture.Describe([]); Assert.Empty(fixture.Fields);
    }

    [Fact]
    public void ClientFlagsAndResetDoNotManufactureServerSource()
    {
        var field = new DmField("utf-8");
        field.SetTypeFlag(2); field.SetCType(2); field.SetScale(0); field.resetType(26);
        Assert.Null(field.TemporalServerMetadata); Assert.Null(field.TemporalBinding);
        var descriptor = field.GetTemporalReadDescriptor(DmTemporalResultFormat.Unknown);
        Assert.Null(descriptor.RawMetadata); Assert.Equal(DmTemporalResultFormat.Unknown, descriptor.ActualFormat);
        Assert.False(new DmTemporalRawMetadata(DmTemporalMetadataSource.Synthetic,
            26, 9, 7, 1, 0, 0, DmTemporalResultFormat.Unknown).HasServerSource);
        field.SetScale(6); field.SetTypeFlag(1);
        var request = W.Dm.Internal.Execution.DmParameterMetadata.From(new DmParameter("p", DBNull.Value)
            { DbType = System.Data.DbType.DateTime });
        Assert.Throws<InvalidOperationException>(() => DmTemporalBindingResolver.Create(request,
            DmParameterTypeSource.ExplicitDbType, 16, field,
            DmTemporalCapabilityProfile.Resolve(new(null!, 0, null!, false))));
    }

    [Theory]
    [InlineData(16, 0x3006)] [InlineData(26, 0x4006)] [InlineData(23, 0x1006)]
    [InlineData(15, 19)] [InlineData(14, 1)] [InlineData(21, 0x1696)]
    [InlineData(21, 0x606)] [InlineData(21, 0xD96)] [InlineData(20, 0x196)]
    public async Task InvalidMetadataIsRejectedWithoutLowNibbleMasking(int type, int scale)
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Describe([new(type, 24, scale, 1)]));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.DescribeResults([new(type, 24, scale, 1)]));
    }

    [Fact]
    public async Task OnlyExactRealAdvisoryIntervalZeroIsAnUnresolvedPlaceholder()
    {
        var fixture = new Fixture(); await fixture.Describe([new(21, 24, 0, 2)]);
        var field = fixture.Fields[0];
        Assert.Equal(0, field.TemporalServerMetadata!.Value.Scale); Assert.Equal(0, field.GetScale());
        Assert.Throws<NotSupportedException>(() => field.GetTemporalReadDescriptor(DmTemporalResultFormat.Unknown));
        using (var plan = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", TimeSpan.Zero) { Scale = 3 }))
        {
            Assert.False(plan.TemporalBindings[0].ReliableFixed); Assert.Null(plan.TemporalBindings[0].ColumnScale);
            Assert.Equal(0x693, plan.TemporalBindings[0].WireScale);
            Assert.Equal(0, plan.TemporalBindings[0].RawMetadata!.Value.Scale);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Describe([new(21, 24, 0, 1)]));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.DescribeResults([new(21, 24, 0, 2)]));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Describe([new(21, 24, 0x1000, 2)]));
    }
}
