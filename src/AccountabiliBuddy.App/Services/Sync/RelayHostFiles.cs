using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The hosted relay's data folder (ADR 46): the certificate it made (<c>relay.pfx</c>, whose fingerprint is the pin), the
/// sealed files it stores (one folder per group, named by 32 hex characters), and the addresses other devices reach this
/// PC by. Never opens or shows what the stored files hold: they are sealed bytes.
/// </summary>
public sealed class RelayHostFiles
{
    private const string CertificateFileName = "relay.pfx";
    private const int GroupFolderNameLength = 32;

    private string? _cachedPin;
    private DateTime _cachedPinStamp;

    public RelayHostFiles(string dataDirectory)
    {
        DataDirectory = dataDirectory;
    }

    public string DataDirectory { get; }

    /// <summary>The relay certificate's pin (SHA-256, base64url), or null until the relay has made its certificate.</summary>
    public string? ReadPin()
    {
        string path = Path.Combine(DataDirectory, CertificateFileName);
        try
        {
            // Read again only when the file changed.
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            if (_cachedPin is not null && stamp == _cachedPinStamp)
            {
                return _cachedPin;
            }

            // Only the public part is needed: nothing is stored in the user's key store.
            using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password: null, X509KeyStorageFlags.EphemeralKeySet);
            _cachedPin = CertificateIdentity.Fingerprint(certificate);
            _cachedPinStamp = stamp;
            return _cachedPin;
        }
        catch (Exception ex) when (ex is IOException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The bytes the stored sealed files take (the certificate is not counted).</summary>
    public long StorageBytes()
    {
        long total = 0;
        foreach (string folder in GroupFolders())
        {
            try
            {
                total += new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A folder that disappears while counting adds nothing.
            }
        }

        return total;
    }

    /// <summary>Deletes every group's folder. The certificate stays, so the pin the devices hold stays valid. The relay must not be running.</summary>
    /// <returns>Null when done, else why not.</returns>
    public string? ClearStorage()
    {
        try
        {
            foreach (string folder in GroupFolders())
            {
                Directory.Delete(folder, recursive: true);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Some files couldn't be deleted: " + ex.Message;
        }
    }

    /// <summary>
    /// The https addresses (with the port) other devices can try: this PC's IPv4 addresses on real networks, home-style
    /// ones first, then its name. The pin is the same for all of them.
    /// </summary>
    public static IReadOnlyList<string> Addresses(int port)
    {
        List<string> hosts = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IsLinkLocal(a))
            .Distinct()
            .OrderBy(HomeRank)
            .Select(a => a.ToString())
            .ToList();
        hosts.Add(Environment.MachineName);
        return hosts.Select(host => $"https://{host}:{port}").ToList();
    }

    /// <summary>How many other computers or phones hold a connection to this port right now (this PC's own connections don't count).</summary>
    public static int ConnectedDevices(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
                .Where(c => c.State == TcpState.Established && c.LocalEndPoint.Port == port && !IPAddress.IsLoopback(c.RemoteEndPoint.Address))
                .Select(c => c.RemoteEndPoint.Address)
                .Distinct()
                .Count();
        }
        catch (NetworkInformationException)
        {
            return 0;
        }
    }

    private IEnumerable<string> GroupFolders()
    {
        if (!Directory.Exists(DataDirectory))
        {
            return [];
        }

        return Directory.EnumerateDirectories(DataDirectory).Where(IsGroupFolder);
    }

    // 32 lower-case hex characters, the relay's group id: nothing else in the data folder is ever deleted.
    private static bool IsGroupFolder(string path)
    {
        string name = Path.GetFileName(path);
        return name.Length == GroupFolderNameLength && name.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }

    // 192.168.x.x, 10.x.x.x and 172.16-31.x.x (home and office networks) before anything else, such as a VPN's range.
    private static int HomeRank(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        bool home = bytes[0] == 192 && bytes[1] == 168;
        bool tenNet = bytes[0] == 10;
        bool twelve = bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
        return home ? 0 : tenNet || twelve ? 1 : 2;
    }
}
