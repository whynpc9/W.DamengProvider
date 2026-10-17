using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using W.Dm;

namespace W.Dm.Internal.Transport;

/// <summary>Immutable TLS input. Its diagnostic form never includes certificate material.</summary>
internal sealed class DmTlsOptions
{
    private readonly string caCertificatePath;
    private readonly string clientCertificatePath;
    private readonly string clientPrivateKeyPath;
    private readonly string clientCertificatePassword;
    private readonly DmTlsRevocationMode revocationMode;

    internal string TargetHost { get; }

    private DmTlsOptions(DmConnectionSettings settings)
    {
        TargetHost = settings.Host;
        caCertificatePath = settings.TlsCaCertificatePath;
        clientCertificatePath = settings.TlsClientCertificatePath;
        clientPrivateKeyPath = settings.TlsClientPrivateKeyPath;
        clientCertificatePassword = settings.TlsClientCertificatePassword;
        revocationMode = settings.TlsRevocationMode;
        if (string.IsNullOrWhiteSpace(TargetHost)) throw new ArgumentException("A TLS target host is required.");
    }

    internal static DmTlsOptions FromSettings(DmConnectionSettings settings) =>
        new(settings ?? throw new ArgumentNullException(nameof(settings)));

#if NETSTANDARD2_1
    internal DmTlsOptionsScope CreateAuthenticationScope(DmDeadline deadline) =>
        throw new PlatformNotSupportedException("TLS is unavailable in the netstandard2.1 provider asset. Use a .NET 6 or later asset for TLS.");
#else
    internal DmTlsOptionsScope CreateAuthenticationScope(DmDeadline deadline)
    {
        var owned = new List<X509Certificate2>();
        try
        {
            X509RevocationMode revocation = revocationMode == DmTlsRevocationMode.Online
                ? X509RevocationMode.Online : X509RevocationMode.NoCheck;
            var authentication = new SslClientAuthenticationOptions
            {
                TargetHost = TargetHost,
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = revocation,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                AllowRenegotiation = false
            };

            var policy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.System,
                RevocationMode = revocation,
                RevocationFlag = X509RevocationFlag.ExcludeRoot,
                VerificationFlags = X509VerificationFlags.NoFlag,
                UrlRetrievalTimeout = deadline.IsInfinite
                    ? TimeSpan.FromSeconds(15)
                    : TimeSpan.FromMilliseconds(deadline.RemainingMilliseconds)
            };
            policy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1")); // TLS Web Server Authentication

            if (caCertificatePath.Length != 0)
            {
                var ca = DmCertificateLoader.LoadCertificateFromFile(caCertificatePath);
                owned.Add(ca);
                ValidateCaCertificate(ca);
                policy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                policy.CustomTrustStore.Add(ca);
            }
#if NET7_0_OR_GREATER
            authentication.CertificateChainPolicy = policy;
#else
            // .NET 6 has no CertificateChainPolicy. The callback rebuilds the chain
            // with the same policy, so the platform chain result is never used and
            // need not repeat an online revocation check inside the handshake.
            authentication.CertificateRevocationCheckMode = X509RevocationMode.NoCheck;
            authentication.RemoteCertificateValidationCallback = (_, certificate, platformChain, errors) =>
                ValidateServerCertificate(policy, certificate, platformChain, errors);
#endif

            if (clientCertificatePath.Length != 0)
            {
                X509Certificate2 client = LoadClientCertificate();
                owned.Add(client);
                if (!client.HasPrivateKey) throw new AuthenticationException("TLS client certificate has no private key.");
                authentication.ClientCertificates = new X509CertificateCollection { client };
            }
            return new DmTlsOptionsScope(authentication, policy, owned);
        }
        catch (TimeoutException)
        {
            foreach (var certificate in owned) certificate.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or AuthenticationException)
        {
            foreach (var certificate in owned) certificate.Dispose();
            throw new AuthenticationException("TLS certificate configuration is invalid.");
        }
    }

    private X509Certificate2 LoadClientCertificate()
    {
        if (clientPrivateKeyPath.Length == 0)
        {
            // Schannel requires a native key container; macOS requires a keychain.
            // Without PersistKeySet, .NET removes these temporary keys when the
            // certificate held by the TLS scope is disposed.
            return DmCertificateLoader.LoadPkcs12FromFile(clientCertificatePath,
                clientCertificatePassword,
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                    ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet);
        }

        var pem = clientCertificatePassword.Length == 0
            ? X509Certificate2.CreateFromPemFile(clientCertificatePath, clientPrivateKeyPath)
            : X509Certificate2.CreateFromEncryptedPemFile(clientCertificatePath,
                clientCertificatePassword.AsSpan(), clientPrivateKeyPath);
        if (!OperatingSystem.IsWindows()) return pem;

        // PEM loading produces an ephemeral key that Schannel cannot use for mTLS.
        // Reimport in memory so the native key has the same scoped lifetime as PFX.
        using (pem)
        {
            byte[] pfx = pem.Export(X509ContentType.Pkcs12);
            try { return DmCertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.DefaultKeySet); }
            finally { CryptographicOperations.ZeroMemory(pfx); }
        }
    }

#if !NET7_0_OR_GREATER
    /// <summary>Equivalent of SslStream's CertificateChainPolicy handling on .NET 7+.</summary>
    internal static bool ValidateServerCertificate(X509ChainPolicy policy, X509Certificate certificate,
        X509Chain platformChain, SslPolicyErrors errors)
    {
        // Only chain trust is re-evaluated. A missing certificate or a host name
        // mismatch reported by SslStream is final.
        if (certificate == null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
            return false;

        using var chain = new X509Chain();
        X509ChainPolicy rebuilt = chain.ChainPolicy;
        rebuilt.TrustMode = policy.TrustMode;
        rebuilt.CustomTrustStore.AddRange(policy.CustomTrustStore);
        rebuilt.RevocationMode = policy.RevocationMode;
        rebuilt.RevocationFlag = policy.RevocationFlag;
        rebuilt.VerificationFlags = policy.VerificationFlags;
        rebuilt.UrlRetrievalTimeout = policy.UrlRetrievalTimeout;
        foreach (Oid usage in policy.ApplicationPolicy) rebuilt.ApplicationPolicy.Add(usage);
        // Intermediates sent by the server only help path building; ExtraStore
        // never makes them trust anchors in either System or CustomRootTrust mode.
        if (platformChain != null) rebuilt.ExtraStore.AddRange(platformChain.ChainPolicy.ExtraStore);

        var leaf = certificate as X509Certificate2;
        bool ownsLeaf = leaf == null;
        leaf ??= new X509Certificate2(certificate);
        try { return chain.Build(leaf); }
        finally
        {
            foreach (X509ChainElement element in chain.ChainElements) element.Certificate.Dispose();
            if (ownsLeaf) leaf.Dispose();
        }
    }

#endif
    private static void ValidateCaCertificate(X509Certificate2 certificate)
    {
        bool isCa = false;
        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension is X509BasicConstraintsExtension constraints)
                isCa = constraints.CertificateAuthority;
            if (extension is X509KeyUsageExtension usage &&
                (usage.KeyUsages & X509KeyUsageFlags.KeyCertSign) == 0)
                throw new AuthenticationException("TLS trust certificate cannot sign certificates.");
        }
        if (!isCa) throw new AuthenticationException("TLS trust certificate is not a CA certificate.");
    }

#endif
    public override string ToString() => "DmTlsOptions";
}

internal sealed class DmTlsOptionsScope : IDisposable
{
    private readonly IReadOnlyList<X509Certificate2> certificates;
    private int disposed;
    internal SslClientAuthenticationOptions Authentication { get; }
    /// <summary>The server chain policy; enforced by SslStream on .NET 7+ and by the validation callback on .NET 6.</summary>
    internal X509ChainPolicy ChainPolicy { get; }

    internal DmTlsOptionsScope(SslClientAuthenticationOptions authentication, X509ChainPolicy chainPolicy,
        IReadOnlyList<X509Certificate2> certificates)
    {
        Authentication = authentication;
        ChainPolicy = chainPolicy;
        this.certificates = certificates;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        foreach (var certificate in certificates) certificate.Dispose();
    }
}
