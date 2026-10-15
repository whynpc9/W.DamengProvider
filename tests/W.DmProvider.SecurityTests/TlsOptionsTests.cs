using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using W.Dm;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.SecurityTests;

public sealed class TlsOptionsTests
{
    [Fact]
    public void EphemeralAuthorityAndServerCarryExpectedConstraintsAndSan()
    {
        using var certificates = new CertificateMaterial();
        using var server = certificates.Server();
        Assert.True(certificates.Authority.Extensions.OfType<X509BasicConstraintsExtension>()
            .Single().CertificateAuthority);
        Assert.False(server.Extensions.OfType<X509BasicConstraintsExtension>()
            .Single().CertificateAuthority);
#if NET7_0_OR_GREATER
        var san = server.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
        Assert.Contains("localhost", san.EnumerateDnsNames());
        Assert.Contains("127.0.0.1", san.EnumerateIPAddresses().Select(ip => ip.ToString()));
#else
        // X509SubjectAlternativeNameExtension is .NET 7+; the loopback TLS tests cover the IP entry.
        Assert.Single(server.Extensions.Cast<X509Extension>(), extension => extension.Oid?.Value == "2.5.29.17");
        Assert.Equal("localhost", server.GetNameInfo(X509NameType.DnsName, forIssuer: false));
#endif
    }

    [Fact]
    public void OptionsKeepConfiguredTargetAndOnlineRevocationDefault()
    {
        using var certificates = new CertificateMaterial();
        var builder = new DmConnectionStringBuilder { Server = "localhost", TlsCaCertificatePath = certificates.AuthorityPath };
        var options = DmTlsOptions.FromSettings(builder.ToSettings());
        using var scope = options.CreateAuthenticationScope(DmDeadline.Start(TimeSpan.FromSeconds(3)));
        Assert.Equal("localhost", options.TargetHost);
        AssertChainPolicyEnforced(scope, X509RevocationMode.Online);
        Assert.Equal(X509RevocationMode.Online, scope.ChainPolicy.RevocationMode);
        Assert.Equal(X509ChainTrustMode.CustomRootTrust, scope.ChainPolicy.TrustMode);
        Assert.Single(scope.ChainPolicy.CustomTrustStore);
    }

    [Fact]
    public void ExplicitNoCheckChangesOnlyRevocationPolicy()
    {
        using var certificates = new CertificateMaterial();
        var builder = new DmConnectionStringBuilder
        {
            Server = "localhost", TlsCaCertificatePath = certificates.AuthorityPath,
            TlsRevocationMode = DmTlsRevocationMode.NoCheck
        };
        using var scope = DmTlsOptions.FromSettings(builder.ToSettings())
            .CreateAuthenticationScope(DmDeadline.Start(TimeSpan.FromSeconds(3)));
        AssertChainPolicyEnforced(scope, X509RevocationMode.NoCheck);
        Assert.Equal(X509RevocationMode.NoCheck, scope.ChainPolicy.RevocationMode);
        Assert.Equal(X509VerificationFlags.NoFlag, scope.ChainPolicy.VerificationFlags);
        Assert.Contains(scope.ChainPolicy.ApplicationPolicy.Cast<System.Security.Cryptography.Oid>(),
            policy => policy.Value == "1.3.6.1.5.5.7.3.1");
    }

    [Fact]
    public void ClientCertificatePasswordIsRedactedAcrossSnapshotAndBaseSetterPoison()
    {
        const string marker = "synthetic-t07-private-marker";
        var builder = new DmConnectionStringBuilder
        {
            Server = "localhost", TlsClientCertificatePath = Path.Combine(Path.GetTempPath(), "dummy.pfx"),
            TlsClientCertificatePassword = marker
        };
        var settings = builder.ToSettings();
        Assert.True(settings.TlsClientCertificatePassword == marker);
        Assert.True(settings.WithSchema("T07_SCHEMA").TlsClientCertificatePassword == marker);
        Assert.False(settings.ToConnectionString(includeSecrets: false).Contains(marker, StringComparison.Ordinal));
        Assert.False(builder.ToRedactedString().Contains(marker, StringComparison.Ordinal));
        System.Data.Common.DbConnectionStringBuilder baseView = builder;
        Assert.Throws<NotSupportedException>(() => baseView.ConnectionString =
            "server=localhost;tls_client_certificate_password=synthetic-t07-private-marker;unknown_option=1");
        Assert.Throws<InvalidOperationException>(() => _ = builder.ToSettings());
        Assert.Throws<InvalidOperationException>(() => _ = builder.ToRedactedString());
    }

#if !NET7_0_OR_GREATER
    [Fact]
    public void Net6CallbackDecidesOnlyFromRebuiltChainAndSslStreamIdentityChecks()
    {
        using var certificates = new CertificateMaterial();
        using var stranger = new CertificateMaterial();
        using var server = certificates.Server();
        using var foreign = stranger.Server();
        var builder = new DmConnectionStringBuilder
        {
            Server = "localhost", TlsCaCertificatePath = certificates.AuthorityPath,
            TlsRevocationMode = DmTlsRevocationMode.NoCheck
        };
        using var scope = DmTlsOptions.FromSettings(builder.ToSettings())
            .CreateAuthenticationScope(DmDeadline.Start(TimeSpan.FromSeconds(3)));
        var callback = scope.Authentication.RemoteCertificateValidationCallback!;
        Assert.True(callback(this, server, null, SslPolicyErrors.None));
        // The platform chain uses system trust; its verdict is replaced by the configured CA policy.
        Assert.True(callback(this, server, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(callback(this, foreign, null, SslPolicyErrors.None));
        Assert.False(callback(this, server, null, SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.False(callback(this, server, null,
            SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(callback(this, null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
    }

    [Fact]
    public void Net6CallbackUsesServerSentCertificatesOnlyToBuildThePath()
    {
        using var certificates = new CertificateMaterial();
        using var stranger = new CertificateMaterial();
        X509Certificate2 intermediate = certificates.Intermediate();
        using var server = certificates.Server(issuer: intermediate);
        // Simulates SslStream's platform chain: ExtraStore holds what the peer sent.
        using var sentIntermediate = new X509Chain();
        sentIntermediate.ChainPolicy.ExtraStore.Add(intermediate);
        using var sentWithRoot = new X509Chain();
        sentWithRoot.ChainPolicy.ExtraStore.Add(intermediate);
        sentWithRoot.ChainPolicy.ExtraStore.Add(certificates.Authority);

        using var trusted = Scope(certificates.AuthorityPath);
        using var foreign = Scope(stranger.AuthorityPath);
        var trustedCallback = trusted.Authentication.RemoteCertificateValidationCallback!;
        var foreignCallback = foreign.Authentication.RemoteCertificateValidationCallback!;
        Assert.True(trustedCallback(this, server, sentIntermediate, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(trustedCallback(this, server, null, SslPolicyErrors.RemoteCertificateChainErrors));
        // A root supplied by the peer completes the path but is never a trust anchor.
        Assert.False(foreignCallback(this, server, sentWithRoot, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(foreignCallback(this, server, sentWithRoot, SslPolicyErrors.None));

        static DmTlsOptionsScope Scope(string authorityPath) => DmTlsOptions.FromSettings(new DmConnectionStringBuilder
        {
            Server = "localhost", TlsCaCertificatePath = authorityPath, TlsRevocationMode = DmTlsRevocationMode.NoCheck
        }.ToSettings()).CreateAuthenticationScope(DmDeadline.Start(TimeSpan.FromSeconds(3)));
    }

#endif
    private static void AssertChainPolicyEnforced(DmTlsOptionsScope scope, X509RevocationMode revocation)
    {
#if NET7_0_OR_GREATER
        Assert.Same(scope.ChainPolicy, scope.Authentication.CertificateChainPolicy);
        Assert.Equal(revocation, scope.Authentication.CertificateRevocationCheckMode);
#else
        // The callback rebuilds the chain with ChainPolicy and discards the platform chain result.
        Assert.NotNull(scope.Authentication.RemoteCertificateValidationCallback);
        Assert.Equal(X509RevocationMode.NoCheck, scope.Authentication.CertificateRevocationCheckMode);
#endif
    }
}
