using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AccountabiliBuddy.Relay;

/// <summary>
/// The relay's own TLS certificate for the pinned (no reverse proxy) mode: a self-signed ECDSA P-256 certificate made on
/// the first run, valid 10 years, with the host's names and addresses as alternative names, kept as <c>relay.pfx</c> in
/// the data folder. The apps pin its SHA-256 fingerprint. Deleting the file makes a new one (and a new pin).
/// </summary>
internal static class RelayCertificate
{
    private const string FileName = "relay.pfx";

    public static X509Certificate2 LoadOrCreate(string dataDirectory, out bool created)
    {
        string path = Path.Combine(dataDirectory, FileName);
        created = !File.Exists(path);
        if (created)
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllBytes(path, CreatePfx());
            RestrictToOwner(path);
        }

        return X509CertificateLoader.LoadPkcs12FromFile(path, password: null, X509KeyStorageFlags.UserKeySet);
    }

    /// <summary>The SHA-256 of the certificate as base64url without padding, the same format as the link's fingerprint.</summary>
    public static string Fingerprint(X509Certificate2 certificate)
    {
        return Convert.ToBase64String(certificate.GetCertHash(HashAlgorithmName.SHA256)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>The names and addresses other devices could reach this host by, for the certificate and the printed address line.</summary>
    public static IReadOnlyList<string> HostAddresses()
    {
        var hosts = new List<string> { Environment.MachineName };
        foreach (IPAddress address in LocalIPv4Addresses())
        {
            hosts.Add(address.ToString());
        }

        return hosts;
    }

    private static byte[] CreatePfx()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN=AccountabiliBuddy Relay on {Environment.MachineName}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        request.CertificateExtensions.Add(BuildAlternativeNames());

        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        return certificate.Export(X509ContentType.Pfx);
    }

    private static X509Extension BuildAlternativeNames()
    {
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(Environment.MachineName);
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        names.AddIpAddress(IPAddress.IPv6Loopback);
        foreach (IPAddress address in LocalIPv4Addresses())
        {
            names.AddIpAddress(address);
        }

        return names.Build();
    }

    private static IEnumerable<IPAddress> LocalIPv4Addresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Distinct();
    }

    private static void RestrictToOwner(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
