using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A self-signed TLS certificate and its SHA-256 fingerprint, for pinning instead of trusting an authority. Kept from
/// the deleted Wi-Fi link (ADR 45); the own relay's pinned TLS (ADR 46) checks a server certificate with
/// <see cref="Fingerprint"/>, the same format the relay prints.
/// </summary>
public static class CertificateIdentity
{
    /// <summary>Self-signed ECDSA P-256, valid for 20 years.</summary>
    public static byte[] CreatePfx(string pcName)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN=AccountabiliBuddy on {pcName}", key, HashAlgorithmName.SHA256);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20));
        return certificate.Export(X509ContentType.Pfx);
    }

    /// <summary>Loads it with a key the system's TLS can use (Windows refuses keys that only live in memory).</summary>
    public static X509Certificate2 Load(byte[] pfx)
    {
        return X509CertificateLoader.LoadPkcs12(pfx, password: null, X509KeyStorageFlags.UserKeySet);
    }

    /// <summary>The SHA-256 of the certificate, base64url.</summary>
    public static string Fingerprint(X509Certificate certificate)
    {
        return Convert.ToBase64String(certificate.GetCertHash(HashAlgorithmName.SHA256)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
