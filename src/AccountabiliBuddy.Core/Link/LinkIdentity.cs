using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AccountabiliBuddy.Core.Link;

/// <summary>
/// The PC's own TLS certificate for the link (ADR 14): self-signed ECDSA P-256, made once and kept encrypted.
/// Devices pin its SHA-256 fingerprint from the QR code instead of trusting any authority.
/// </summary>
public static class LinkIdentity
{
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

    /// <summary>The SHA-256 of the certificate, base64url, as the QR code carries it.</summary>
    public static string Fingerprint(X509Certificate2 certificate)
    {
        return LinkSecrets.ToBase64Url(certificate.GetCertHash(HashAlgorithmName.SHA256));
    }
}
