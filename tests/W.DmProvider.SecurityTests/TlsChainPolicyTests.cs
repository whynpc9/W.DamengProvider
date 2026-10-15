using System.Security.Cryptography.X509Certificates;
using W.Dm;
using Xunit;

namespace W.DmProvider.SecurityTests;

/// <summary>Chain-building cases that .NET 7+ enforces through CertificateChainPolicy
/// and .NET 6 through the driver's rebuilt chain.</summary>
public sealed class TlsChainPolicyTests
{
    [Fact]
    public async Task ServerSentIntermediateCompletesChainToConfiguredRoot()
    {
        using var certificates = new CertificateMaterial();
        X509Certificate2 intermediate = certificates.Intermediate();
        using var server = certificates.Server(issuer: intermediate);
        var result = await TlsLoopbackHarness.ConnectAsync(certificates, server, "localhost", certificates.AuthorityPath,
            serverChain: new X509Certificate2Collection(intermediate));
        Assert.True(result.ClientAuthenticated);
        Assert.True(result.ServerAuthenticated);
        Assert.Equal(1, result.SuccessfulUpgrades);
        Assert.Equal(result.CreatedSockets, result.DisposedSockets);
    }

    [Fact]
    public async Task MissingIntermediateFailsClosed()
    {
        using var certificates = new CertificateMaterial();
        X509Certificate2 intermediate = certificates.Intermediate();
        using var server = certificates.Server(issuer: intermediate);
        var result = await TlsLoopbackHarness.ConnectAsync(certificates, server, "localhost", certificates.AuthorityPath);
        Assert.False(result.ClientAuthenticated);
        Assert.Equal(0, result.SuccessfulUpgrades);
        Assert.Equal(1, result.FailedUpgrades);
        Assert.Equal(result.CreatedSockets, result.DisposedSockets);
    }

    [Fact]
    public async Task ServerSentRootIsNeverATrustAnchor()
    {
        using var certificates = new CertificateMaterial();
        using var stranger = new CertificateMaterial();
        X509Certificate2 intermediate = certificates.Intermediate();
        using var server = certificates.Server(issuer: intermediate);
        // The peer supplies its complete chain, including its root; only the configured CA is trusted.
        var result = await TlsLoopbackHarness.ConnectAsync(certificates, server, "localhost", stranger.AuthorityPath,
            serverChain: new X509Certificate2Collection(new[] { intermediate, certificates.Authority }));
        Assert.False(result.ClientAuthenticated);
        Assert.Equal(0, result.SuccessfulUpgrades);
        Assert.Equal(1, result.FailedUpgrades);
        Assert.Equal(result.CreatedSockets, result.DisposedSockets);
    }

    [Fact]
    public async Task OnlineRevocationWithoutRevocationDataFailsClosed()
    {
        // The synthetic chain has no CRL or OCSP location, so Online cannot prove
        // the leaf is unrevoked. That must reject, never degrade to NoCheck.
        using var certificates = new CertificateMaterial();
        using var server = certificates.Server();
        var result = await TlsLoopbackHarness.ConnectAsync(certificates, server, "localhost", certificates.AuthorityPath,
            revocation: DmTlsRevocationMode.Online);
        Assert.False(result.ClientAuthenticated);
        Assert.Equal(0, result.SuccessfulUpgrades);
        Assert.Equal(1, result.FailedUpgrades);
        Assert.Equal(result.CreatedSockets, result.DisposedSockets);
    }
}
