using System.Net;
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
/// certificate and paired devices, and offers pairing codes. Turning it off and unpairing count as Revokes, so they
/// are locked during focus. Use from the UI thread; its events are raised there.
/// </summary>
public sealed class LinkService : IAsyncDisposable
{
    private readonly LinkDeviceStore _devices;
    private readonly ILinkSessionHost _host;
    private readonly SessionController _sessions;

    private LinkServer? _server;
    private X509Certificate2? _certificate;

    public LinkService(SqliteJsonDocumentStore store, IDataProtector protector, ILinkSessionHost host, SessionController sessions)
    {
        _devices = new LinkDeviceStore(store, protector);
        _host = host;
        _sessions = sessions;
        _devices.Changed += (_, _) => Dispatcher.UIThread.Post(() => Changed?.Invoke(this, EventArgs.Empty));
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
            await StartServerAsync();
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
            await StartServerAsync();
        }
        else
        {
            await StopServerAsync();
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
        await StopServerAsync();
        _certificate?.Dispose();
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
