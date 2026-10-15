using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using W.Dm.Internal.Transport;

namespace W.DmProvider.SecurityTests;

internal sealed class CertificateMaterial : IDisposable
{
    private readonly RSA caKey = RSA.Create(2048);
    private readonly List<RSA> leafKeys = [];
    private readonly List<X509Certificate2> leaves = [];
    private readonly string directory;

    internal X509Certificate2 Authority { get; }
    internal string AuthorityPath { get; }
    internal string ClientCertificatePath => Path.Combine(directory, "client-cert.pem");
    internal string ClientPrivateKeyPath => Path.Combine(directory, "client-key.pem");
    internal string ClientPfxPath => Path.Combine(directory, "client-cert.pfx");

    internal CertificateMaterial()
    {
        directory = Path.Combine(Path.GetTempPath(), "wdm-t07-certs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        RestrictToOwner(directory, 0b111_000_000); // 0700
        var request = new CertificateRequest("CN=WDM-T07-CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        Authority = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(30));
        AuthorityPath = Path.Combine(directory, "ca-cert.pem");
        WritePrivate(AuthorityPath, Pem("CERTIFICATE", Authority.RawData));
    }

    /// <summary>An intermediate CA signed by this authority; it is not written to a trust file.</summary>
    internal X509Certificate2 Intermediate()
    {
        RSA key = RSA.Create(2048);
        leafKeys.Add(key);
        var request = new CertificateRequest("CN=WDM-T07-INTERMEDIATE", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var issued = request.Create(Authority, DateTimeOffset.UtcNow.AddDays(-5), DateTimeOffset.UtcNow.AddDays(20), Serial());
        var intermediate = issued.CopyWithPrivateKey(key);
        leaves.Add(intermediate);
        return intermediate;
    }

    internal X509Certificate2 Server(string dnsName = "localhost", bool includeIp = true,
        bool valid = true, bool serverAuth = true, X509Certificate2? issuer = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return Issue("CN=" + dnsName, serverAuth ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2",
            request =>
            {
                var san = new SubjectAlternativeNameBuilder();
                san.AddDnsName(dnsName);
                if (includeIp) san.AddIpAddress(IPAddress.Loopback);
                request.CertificateExtensions.Add(san.Build());
            }, valid ? now.AddDays(-1) : now.AddDays(-4), valid ? now.AddDays(10) : now.AddDays(-2),
            forTlsServer: true, issuer: issuer);
    }

    internal X509Certificate2 Client(bool writePemFiles = false)
    {
        var certificate = Issue("CN=WDM_PROVIDER_TEST", "1.3.6.1.5.5.7.3.2", null,
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10));
        if (writePemFiles)
        {
            WritePrivate(ClientCertificatePath, Pem("CERTIFICATE", certificate.RawData));
            using RSA key = certificate.GetRSAPrivateKey()!;
            WritePrivate(ClientPrivateKeyPath, Pem("PRIVATE KEY", key.ExportPkcs8PrivateKey()));
        }
        return certificate;
    }

    internal void WriteClientPfx(string password)
    {
        var certificate = Client();
        File.WriteAllBytes(ClientPfxPath, certificate.Export(X509ContentType.Pkcs12, password));
        RestrictToOwner(ClientPfxPath, 0b110_000_000); // 0600
    }

    private X509Certificate2 Issue(string subject, string extendedKeyUsage,
        Action<CertificateRequest>? configure, DateTimeOffset notBefore, DateTimeOffset notAfter,
        bool forTlsServer = false, X509Certificate2? issuer = null)
    {
        RSA key = RSA.Create(2048);
        leafKeys.Add(key);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var purposes = new OidCollection { new Oid(extendedKeyUsage) };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(purposes, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        configure?.Invoke(request);
        using var issued = request.Create(issuer ?? Authority, notBefore, notAfter, Serial());
        var withKey = issued.CopyWithPrivateKey(key);
        if (forTlsServer && OperatingSystem.IsWindows())
        {
            // Schannel needs a native key container for the loopback TLS server.
            // DefaultKeySet creates a temporary key that is removed on Dispose;
            // no certificate is installed in a system trust store.
            byte[] pfx = withKey.Export(X509ContentType.Pkcs12);
            try
            {
                using var generated = withKey;
                withKey = DmCertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.DefaultKeySet);
            }
            finally { CryptographicOperations.ZeroMemory(pfx); }
        }
        leaves.Add(withKey);
        return withKey;
    }

    private static byte[] Serial()
    {
        byte[] serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7f;
        return serial;
    }

    private static void WritePrivate(string path, string contents)
    {
        File.WriteAllText(path, contents);
        RestrictToOwner(path, 0b110_000_000); // 0600
    }

    // Same text as ExportCertificatePem/ExportPkcs8PrivateKeyPem, which are .NET 7+.
    private static string Pem(string label, byte[] data) => new(PemEncoding.Write(label, data));

    private static void RestrictToOwner(string path, int mode)
    {
        if (OperatingSystem.IsWindows()) return;
#if NET7_0_OR_GREATER
        File.SetUnixFileMode(path, (UnixFileMode)mode);
#else
        if (chmod(path, (uint)mode) != 0) throw new IOException("chmod failed: " + Marshal.GetLastWin32Error());
#endif
    }

#if !NET7_0_OR_GREATER
    [DllImport("libc", SetLastError = true)]
    private static extern int chmod(string path, uint mode);
#endif

    public void Dispose()
    {
        foreach (var leaf in leaves) leaf.Dispose();
        foreach (var key in leafKeys) key.Dispose();
        Authority.Dispose();
        caKey.Dispose();
        Directory.Delete(directory, recursive: true);
    }
}
