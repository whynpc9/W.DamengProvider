using System.Data;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.TransactionTests;

public sealed class ServerCapabilitiesTests
{
    [Fact]
    public void MeasuredProfileOpensEachGateThroughTheSameRuleAsTheOperation()
    {
        var temporal = DmTemporalCapabilityProfile.Resolve(new("8.1.5.60", 21, "utf-8", true));
        var capabilities = new DmServerCapabilities("8.1.5.60", temporal);
        Assert.Equal("8.1.5.60", capabilities.ServerVersion);
        Assert.True(capabilities.IsMeasuredProfile);
        Assert.True(capabilities.SupportsSavepoints);
        Assert.True(capabilities.SupportsReadUncommitted);
        Assert.True(capabilities.SupportsSerializable);
        Assert.True(capabilities.SupportsStatementErrorRecovery);
        Assert.True(capabilities.SupportsTableDdlInTransaction);
        Assert.Equal(7, capabilities.MaxTemporalWriteScale);
        Assert.Equal("TP01-v11-shared-physical", capabilities.TemporalProfile);
        Assert.Equal(DmConnection.IsPublicTransactionIsolationSupported(IsolationLevel.Serializable, "8.1.5.60"),
            capabilities.SupportsSerializable);
        Assert.Equal(DmServerErrorRecovery.IsMeasuredProfile("8.1.5.60"), capabilities.SupportsStatementErrorRecovery);
    }

    [Theory]
    [InlineData("8.1.4.6", 11, "gb18030", "TP01-v11-tls-physical")]
    [InlineData("8.1.5.61", 21, "utf-8", "CompatibilityLegacyUnverified")]
    [InlineData("8.1.5.60 ", 21, "utf-8", "CompatibilityLegacyUnverified")]
    [InlineData(null, 0, null, "CompatibilityLegacyUnverified")]
    public void UnmeasuredVersionsReportTheConservativeGates(string? version, int message, string? encoding, string evidence)
    {
        var temporal = DmTemporalCapabilityProfile.Resolve(new(version!, message, encoding!, true));
        var capabilities = new DmServerCapabilities(version!, temporal);
        Assert.False(capabilities.IsMeasuredProfile);
        Assert.False(capabilities.SupportsSavepoints);
        Assert.False(capabilities.SupportsReadUncommitted);
        Assert.False(capabilities.SupportsSerializable);
        Assert.False(capabilities.SupportsStatementErrorRecovery);
        Assert.False(capabilities.SupportsTableDdlInTransaction);
        Assert.Equal(6, capabilities.MaxTemporalWriteScale);
        Assert.Equal(evidence, capabilities.TemporalProfile);
    }

    [Fact]
    public void ClosedConnectionHasNoCapabilities()
    {
        using var connection = new DmConnection();
        Assert.Throws<InvalidOperationException>(() => connection.ServerCapabilities);
    }
}
