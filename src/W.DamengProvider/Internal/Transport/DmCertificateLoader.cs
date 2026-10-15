using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace W.Dm.Internal.Transport;

/// <summary>Strict certificate loading; .NET 6 has no <c>X509CertificateLoader</c>.</summary>
internal static class DmCertificateLoader
{
#if NET8_0_OR_GREATER
    internal static X509Certificate2 LoadCertificateFromFile(string path) =>
        X509CertificateLoader.LoadCertificateFromFile(path);

    internal static X509Certificate2 LoadPkcs12FromFile(string path, string password, X509KeyStorageFlags flags) =>
        X509CertificateLoader.LoadPkcs12FromFile(path, password, flags);

    internal static X509Certificate2 LoadPkcs12(byte[] data, string password, X509KeyStorageFlags flags) =>
        X509CertificateLoader.LoadPkcs12(data, password, flags);
#else
    // The X509Certificate2 constructors accept any supported format. Check the
    // content type of the same bytes that are loaded, so a trust file cannot be
    // a PFX and a client file cannot be a bare certificate, as X509CertificateLoader.
    internal static X509Certificate2 LoadCertificateFromFile(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        RequireContentType(data, X509ContentType.Cert);
        return new X509Certificate2(data);
    }

    internal static X509Certificate2 LoadPkcs12FromFile(string path, string password, X509KeyStorageFlags flags)
    {
        byte[] data = File.ReadAllBytes(path);
        try { return LoadPkcs12(data, password, flags); }
        finally { CryptographicOperations.ZeroMemory(data); }
    }

    internal static X509Certificate2 LoadPkcs12(byte[] data, string password, X509KeyStorageFlags flags)
    {
        RequireContentType(data, X509ContentType.Pkcs12);
        return new X509Certificate2(data, password, flags);
    }

    private static void RequireContentType(byte[] data, X509ContentType expected)
    {
        if (X509Certificate2.GetCertContentType(data) != expected)
            throw new CryptographicException("The certificate data has an unexpected content type.");
    }
#endif
}
