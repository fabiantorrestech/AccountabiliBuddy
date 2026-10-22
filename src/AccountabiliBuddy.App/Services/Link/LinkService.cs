using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AccountabiliBuddy.Core.Link;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Link;

/// <summary>
/// Turns the link with phones and tablets (ADR 14) on and off as the Setup tab says (off by default), owns its server,
/// certificate and paired devices, and offers pairing codes. It listens only while Windows marks the networks this
/// PC is on as Private: on a Public one it pauses, and it resumes by itself (checked when the network changes and
/// every few minutes). Turning it off and unpairing count as Revokes, so they are locked during focus. Use from the
/// UI thread; its events are raised there.
/// </summary>
public sealed class LinkService : IAsyncDisposable
{
    private static readonly TimeSpan NetworkRecheckInterval = TimeSpan.FromMinutes(2);

    private readonly LinkDeviceStore _devices;
    private readonly ILinkSessionHost _host;
    private readonly SessionController _sessions;
    private readonly INetworkTrust _networkTrust;
    private readonly DispatcherTimer _networkRecheck;

    // Network changes come in bursts; one check at a time, so two can't both start a server.
    private readonly SemaphoreSlim _following = new(1, 1);

    private LinkServer? _server;
    private X509Certificate2? _certificate;

    public LinkService(
        SqliteJsonDocumentStore store, IDataProtector protector, ILinkSessionHost host, SessionController sessions, INetworkTrust networkTrust)
    {
        _devices = new LinkDeviceStore(store, protector);
        _host = host;
        _sessions = sessions;
        _networkTrust = networkTrust;
        _devices.Changed += (_, _) => Dispatcher.UIThread.Post(() => Changed?.Invoke(this, EventArgs.Empty));

        // A switch between Public and Private changes no address, hence the slow recheck as well.
        _networkRecheck = new DispatcherTimer { Interval = NetworkRecheckInterval };
        _networkRecheck.Tick += (_, _) => _ = FollowNetworkAsync();
        NetworkChange.NetworkAddressChanged += (_, _) => Dispatcher.UIThread.Post(() => _ = FollowNetworkAsync());
    }

    public bool IsEnabled => _devices.Settings.Enabled;

    public bool IsRunning => _server?.IsRunning == true;

    public int Port => _devices.Settings.Port;

    /// <summary>Why the link isn't working although it's on (e.g. the port is taken); null when fine.</summary>
    public string? Problem { get; private set; }

    public IReadOnlyList<PairedDevice> Devices => _devices.Devices;

    public IReadOnlyCollection<Guid> ConnectedDeviceIds => _server?.ConnectedDeviceIds ?? Array.Empty<Guid>();

    /// <summary>Raised when it's turned on or off, a device pairs, connects, disconnects or is removed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a device paired with the offered code.</summary>
    public event EventHandler<PairedDevice>? DevicePaired;

    /// <summary>Linking paused on a network Windows marks Public (raised at each check while paused).</summary>
    public event EventHandler<PublicNetwork>? PausedOnPublicNetwork;

    public void LoadBlocking()
    {
        try
        {
            Task.Run(_devices.LoadAsync).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Problem = "Linked devices couldn't be read; they may need pairing again.";
        }
    }

    public async Task StartIfEnabledAsync()
    {
        if (IsEnabled)
        {
            _networkRecheck.Start();
            await FollowNetworkAsync();
        }
    }

    /// <returns>Null when done, otherwise why not (turning it off is locked during focus).</returns>
    public async Task<string?> SetEnabledAsync(bool enabled)
    {
        if (!enabled && _sessions.IsEnforcing)
        {
            return "Turning linked devices off is locked during focus.";
        }

        await _devices.SaveSettingsAsync(_devices.Settings with { Enabled = enabled });
        if (enabled)
        {
            _networkRecheck.Start();
            await FollowNetworkAsync();
        }
        else
        {
            _networkRecheck.Stop();
            await StopServerAsync();
            Problem = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <returns>The QR code's link and the code, or null when the link is off or the PC isn't on a home network (see <see cref="Problem"/>).</returns>
    public PairingOffer? BeginPairing()
    {
        if (_server is null)
        {
            return null;
        }

        IPAddress? address = LinkAddresses.PreferredAddress();
        if (address is null)
        {
            Problem = "This PC isn't on a home network (Wi-Fi or Ethernet) right now, so a phone can't reach it.";
            Changed?.Invoke(this, EventArgs.Empty);
            return null;
        }

        return _server.BeginPairing(address);
    }

    public void CancelPairing()
    {
        _server?.CancelPairing();
    }

    /// <returns>Null when unpaired, otherwise why not (locked during focus, like every Revoke).</returns>
    public async Task<string?> UnpairAsync(Guid deviceId)
    {
        if (_sessions.IsEnforcing)
        {
            return "Unpairing is locked during focus.";
        }

        await _devices.RemoveAsync(deviceId);
        _server?.Disconnect(deviceId);
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        _networkRecheck.Stop();
        await StopServerAsync();
        _certificate?.Dispose();
    }

    /// <summary>Listens on a Private network, pauses on a Public one (ADR 14: never listen on public networks).</summary>
    private async Task FollowNetworkAsync()
    {
        await _following.WaitAsync();
        try
        {
            await FollowNetworkOnceAsync();
        }
        finally
        {
            _following.Release();
        }
    }

    private async Task FollowNetworkOnceAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        if (_networkTrust.IsOnPrivateNetwork())
        {
            if (_server is null)
            {
                await StartServerAsync();
                Changed?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (_server is not null)
        {
            await StopServerAsync();
        }

        Problem = "Paused: Windows marks this network as Public. Linking works on networks set to Private "
                  + "(Settings → Network & internet → your Wi-Fi or Ethernet → Private network).";
        Changed?.Invoke(this, EventArgs.Empty);
        if (_networkTrust.FindPublicNetwork() is PublicNetwork network)
        {
            PausedOnPublicNetwork?.Invoke(this, network);
        }
    }

    private async Task StartServerAsync()
    {
        if (_server is not null)
        {
            return;
        }

        try
        {
            _certificate ??= await Task.Run(() => _devices.LoadOrCreateIdentityAsync(_host.PcName));
            var server = new LinkServer(_host, _devices, _certificate, Port);
            server.ConnectionsChanged += (_, _) => Dispatcher.UIThread.Post(() => Changed?.Invoke(this, EventArgs.Empty));
            server.DevicePaired += (_, device) => Dispatcher.UIThread.Post(() =>
            {
                DevicePaired?.Invoke(this, device);
                Changed?.Invoke(this, EventArgs.Empty);
            });
            server.Start();
            _server = server;
            Problem = null;
        }
        catch (SocketException ex)
        {
            Problem = $"Port {Port} is in use by another program, so devices can't link ({ex.SocketErrorCode}).";
        }
        catch (CryptographicException ex)
        {
            Problem = $"The link's certificate couldn't be made ({ex.Message}).";
        }
        catch (SqliteException ex)
        {
            Problem = $"The link's settings couldn't be saved ({ex.Message}).";
        }
    }

    private async Task StopServerAsync()
    {
        LinkServer? server = _server;
        _server = null;
        if (server is not null)
        {
            await server.DisposeAsync();
        }
    }
}
