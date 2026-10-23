using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace AccountabiliBuddy.Core.Link;

/// <summary>
/// The PC side of the link (ADR 14). While running it listens on one TCP port, speaks TLS with the PC's own
/// certificate and answers private-network addresses only. A connection first pairs (the one-time code from
/// <see cref="BeginPairing"/>: five minutes, five tries) or signs in with a device token; after that the device can
/// start a session or end one early by the app's own rules (<see cref="ILinkSessionHost"/>), and every session change
/// is pushed to it, as are the settings and today's focus when they change, and the sync files (ADR 22) each once per
/// connection; a phone can send the sync files it carries. A watch (ADR 21) gets the session and today's focus only,
/// never the settings or sync files, and can't start, end, change or send anything. A device that is unpaired is
/// disconnected at once.
/// </summary>
public sealed class LinkServer : IAsyncDisposable
{
    private const int MaxPairingAttempts = 5;
    private const int MaxDeviceNameLength = 60;
    private const string WatchKind = "watch";
    private static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15);

    private readonly ILinkSessionHost _host;
    private readonly LinkDeviceStore _devices;
    private readonly X509Certificate2 _certificate;
    private readonly ConcurrentDictionary<Connection, byte> _connections = new();
    private readonly object _pairingLock = new();

    private PairingWindow? _pairing;
    private TcpListener? _listener;
    private LinkDiscoveryResponder? _discovery;
    private CancellationTokenSource? _stopping;
    private Task? _acceptLoop;

    public LinkServer(ILinkSessionHost host, LinkDeviceStore devices, X509Certificate2 certificate, int port)
    {
        _host = host;
        _devices = devices;
        _certificate = certificate;
        Port = port;
        Fingerprint = LinkIdentity.Fingerprint(certificate);
    }

    public int Port { get; }

    /// <summary>The certificate's SHA-256, which devices pin (it is in the QR code).</summary>
    public string Fingerprint { get; }

    public bool IsRunning => _listener is not null;

    /// <summary>True when devices can find this PC by asking the network (its UDP port was free).</summary>
    public bool IsDiscoverable { get; private set; }

    /// <summary>The devices connected right now.</summary>
    public IReadOnlyCollection<Guid> ConnectedDeviceIds =>
        _connections.Keys.Select(connection => connection.DeviceId).OfType<Guid>().Distinct().ToList();

    /// <summary>Raised (on a link thread) when a device connects or disconnects.</summary>
    public event EventHandler? ConnectionsChanged;

    /// <summary>Raised (on a link thread) when a device paired with the offered code.</summary>
    public event EventHandler<PairedDevice>? DevicePaired;

    /// <exception cref="SocketException">The port is taken.</exception>
    public void Start()
    {
        if (_listener is not null)
        {
            return;
        }

        var listener = new TcpListener(IPAddress.IPv6Any, Port);
        listener.Server.DualMode = true;
        listener.Start();
        _listener = listener;
        _stopping = new CancellationTokenSource();
        _host.StateChanged += OnStateChanged;
        _host.SettingsChanged += OnSettingsChanged;
        _host.FocusDayChanged += OnFocusDayChanged;
        _host.SyncFilesChanged += OnSyncFilesChanged;
        _acceptLoop = AcceptLoopAsync(listener, _stopping.Token);

        // "Where is the PC?" on the same port number over UDP; without it devices need the remembered address.
        _discovery = new LinkDiscoveryResponder(Fingerprint, Port, _host.PcName);
        IsDiscoverable = _discovery.Start(Port);
    }

    public async Task StopAsync()
    {
        if (_listener is null || _stopping is null)
        {
            return;
        }

        _host.StateChanged -= OnStateChanged;
        _host.SettingsChanged -= OnSettingsChanged;
        _host.FocusDayChanged -= OnFocusDayChanged;
        _host.SyncFilesChanged -= OnSyncFilesChanged;
        _discovery?.Dispose();
        _discovery = null;
        _stopping.Cancel();
        _listener.Stop();
        _listener = null;
        foreach (Connection connection in _connections.Keys)
        {
            connection.Close();
        }

        if (_acceptLoop is not null)
        {
            await _acceptLoop.ConfigureAwait(false);
        }

        CancelPairing();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    /// <summary>Offers a new one-time code (replacing any earlier one) and the link the QR code carries.</summary>
    public PairingOffer BeginPairing(IPAddress address)
    {
        string code = LinkSecrets.NewPairingCode();
        DateTimeOffset expiresAt = _host.Now + PairingLifetime;
        lock (_pairingLock)
        {
            _pairing = new PairingWindow(code, expiresAt, MaxPairingAttempts);
        }

        string link = $"accountabilibuddy://pair?h={address}&p={Port}&f={Fingerprint}&c={code}&n={Uri.EscapeDataString(_host.PcName)}";
        return new PairingOffer(link, code, address.ToString(), Port, Fingerprint, expiresAt);
    }

    public void CancelPairing()
    {
        lock (_pairingLock)
        {
            _pairing = null;
        }
    }

    /// <summary>Closes the device's connections (after unpairing it).</summary>
    public void Disconnect(Guid deviceId)
    {
        foreach (Connection connection in _connections.Keys.Where(candidate => candidate.DeviceId == deviceId))
        {
            connection.Close();
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stopping).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (stopping.IsCancellationRequested)
                {
                    return;
                }

                continue;
            }

            _ = ServeAsync(client, stopping);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken stopping)
    {
        using (client)
        {
            if (client.Client.RemoteEndPoint is not IPEndPoint remote || !LinkAddresses.IsPrivate(remote.Address))
            {
                return;
            }

            var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            var connection = new Connection(tls);
            try
            {
                using CancellationTokenSource handshake = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                handshake.CancelAfter(HandshakeTimeout);
                await tls.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _certificate,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        ClientCertificateRequired = false,
                    },
                    handshake.Token).ConfigureAwait(false);

                LinkEnvelope? first = await LinkFraming.ReadAsync(tls, handshake.Token).ConfigureAwait(false);
                PairedDevice? device = first?.Type switch
                {
                    LinkMessages.Pair => await PairAsync(connection, first.BodyAs<LinkPairRequest>()).ConfigureAwait(false),
                    LinkMessages.Hello => SignIn(first.BodyAs<LinkHello>()),
                    _ => null,
                };
                if (device is null)
                {
                    string why = first?.Type == LinkMessages.Pair
                        ? "The pairing code is wrong or has expired. Show a new QR code on the PC."
                        : "This device isn't paired with this PC any more. Pair it again.";
                    await connection.SendAsync(LinkEnvelope.Of(LinkMessages.Refused, new LinkResult(false, why)), stopping).ConfigureAwait(false);
                    return;
                }

                connection.DeviceId = device.Id;
                connection.IsWatch = device.Kind == WatchKind;
                _connections[connection] = 0;
                ConnectionsChanged?.Invoke(this, EventArgs.Empty);
                await _devices.MarkSeenAsync(device.Id, _host.Now).ConfigureAwait(false);
                await connection.SendAsync(
                    LinkEnvelope.Of(LinkMessages.Welcome, new LinkWelcome(_host.PcName, LinkMessages.ProtocolVersion)), stopping).ConfigureAwait(false);
                await connection.SendAsync(LinkEnvelope.Of(LinkMessages.State, StateFor(connection, await _host.GetStateAsync().ConfigureAwait(false))), stopping)
                    .ConfigureAwait(false);
                // A newly paired device has none yet; a signed-in one says which it has. A watch never gets them.
                connection.SettingsVersion = first!.Type == LinkMessages.Hello ? first.BodyAs<LinkHello>()?.SettingsVersion : null;
                if (!connection.IsWatch)
                {
                    await SendSettingsIfNewerAsync(connection, await SettingsAsync().ConfigureAwait(false), stopping).ConfigureAwait(false);
                }

                if (await FocusDayAsync().ConfigureAwait(false) is VersionedFocusDay focusDay)
                {
                    await SendFocusDayIfChangedAsync(connection, focusDay, stopping).ConfigureAwait(false);
                }

                if (!connection.IsWatch && await SyncFilesAsync().ConfigureAwait(false) is { } syncFiles)
                {
                    await SendNewSyncFilesAsync(connection, syncFiles, stopping).ConfigureAwait(false);
                }

                while (await LinkFraming.ReadAsync(tls, stopping).ConfigureAwait(false) is LinkEnvelope request)
                {
                    if (!await HandleAsync(connection, device, request, stopping).ConfigureAwait(false))
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or AuthenticationException or InvalidDataException
                                           or OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // Dropped, timed out, not this protocol, or the server stopped.
            }
            finally
            {
                connection.Close();
                if (_connections.TryRemove(connection, out _))
                {
                    ConnectionsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    private async Task<PairedDevice?> PairAsync(Connection connection, LinkPairRequest? request)
    {
        if (request is null || !TryUsePairingCode(request.Code))
        {
            return null;
        }

        string token = LinkSecrets.NewToken();
        string name = request.DeviceName.Trim() is { Length: > 0 } given ? given[..Math.Min(given.Length, MaxDeviceNameLength)] : "Phone";
        string kind = request.DeviceKind switch
        {
            "tablet" => "tablet",
            WatchKind => WatchKind,
            _ => "phone",
        };
        PairedDevice device = await _devices.AddAsync(name, kind, token, _host.Now).ConfigureAwait(false);
        await connection.SendAsync(LinkEnvelope.Of(LinkMessages.Paired, new LinkPaired(device.Id, token, _host.PcName)), CancellationToken.None)
            .ConfigureAwait(false);
        DevicePaired?.Invoke(this, device);
        return device;
    }

    /// <summary>One try of the offered code: right and in time uses it up; wrong costs one of the tries.</summary>
    private bool TryUsePairingCode(string code)
    {
        lock (_pairingLock)
        {
            if (_pairing is null || _host.Now > _pairing.ExpiresAt)
            {
                _pairing = null;
                return false;
            }

            if (LinkSecrets.CodeMatches(code, _pairing.Code))
            {
                _pairing = null;
                return true;
            }

            _pairing = _pairing.AttemptsLeft <= 1 ? null : _pairing with { AttemptsLeft = _pairing.AttemptsLeft - 1 };
            return false;
        }
    }

    private PairedDevice? SignIn(LinkHello? hello)
    {
        return hello is null ? null : _devices.Authenticate(hello.DeviceId, hello.Token);
    }

    /// <returns>False when the connection should close.</returns>
    private async Task<bool> HandleAsync(Connection connection, PairedDevice device, LinkEnvelope request, CancellationToken stopping)
    {
        if (connection.IsWatch && request.Type != LinkMessages.Unpair)
        {
            // A watch only shows (ADR 21): starting, ending, plan edits and sync files stay with the PC and the phone.
            await connection.SendAsync(ResultOf("A watch can only show the session, not start, end or change it.", string.Empty), stopping)
                .ConfigureAwait(false);
            return true;
        }

        switch (request.Type)
        {
            case LinkMessages.Start:
                string? startError = request.BodyAs<LinkStartRequest>() is { } start
                    ? await _host.StartAsync(start).ConfigureAwait(false)
                    : "The request was empty.";
                await connection.SendAsync(ResultOf(startError, "Started."), stopping).ConfigureAwait(false);
                return true;

            case LinkMessages.EndEarlyBegin:
                LinkEndEarlyTerms? terms = await _host.GetEndEarlyTermsAsync().ConfigureAwait(false);
                connection.EndEarlyTerms = terms;
                await connection.SendAsync(
                    terms is null ? ResultOf("No session is running.", string.Empty) : LinkEnvelope.Of(LinkMessages.EndEarlyTerms, terms),
                    stopping).ConfigureAwait(false);
                return true;

            case LinkMessages.EndEarly:
                string? endError = await EndEarlyAsync(connection, request.BodyAs<LinkEndEarlyRequest>()).ConfigureAwait(false);
                await connection.SendAsync(ResultOf(endError, "Session ended."), stopping).ConfigureAwait(false);
                return true;

            case LinkMessages.Unpair:
                await _devices.RemoveAsync(device.Id).ConfigureAwait(false);
                return false;

            case LinkMessages.DayPlanEdit:
                LinkDayPlanAnswer answer = request.BodyAs<LinkDayPlanEdit>() is { } edit
                    ? await _host.ApplyDayPlanAsync(edit).ConfigureAwait(false)
                    : new LinkDayPlanAnswer(LinkDayPlanStatus.Invalid, "The request was empty.");
                await connection.SendAsync(LinkEnvelope.Of(LinkMessages.DayPlanAnswer, answer), stopping).ConfigureAwait(false);
                return true;

            case LinkMessages.SyncFile:
                string? syncError = await AcceptSyncFileAsync(connection, request.BodyAs<LinkSyncFile>()).ConfigureAwait(false);
                await connection.SendAsync(ResultOf(syncError, "Synced."), stopping).ConfigureAwait(false);
                return true;

            default:
                await connection.SendAsync(ResultOf($"Unknown request '{request.Type}'.", string.Empty), stopping).ConfigureAwait(false);
                return true;
        }
    }

    /// <summary>Ends the session only after the PC's own wait, counted on its own clock, and with a long enough reason.</summary>
    private async Task<string?> EndEarlyAsync(Connection connection, LinkEndEarlyRequest? request)
    {
        LinkEndEarlyTerms? terms = connection.EndEarlyTerms;
        if (terms is null)
        {
            return "Ask to end the session first (the session may have changed meanwhile).";
        }

        TimeSpan left = terms.AllowedFrom - _host.Now;
        if (left > TimeSpan.Zero)
        {
            return $"Wait until the countdown ends ({Math.Ceiling(left.TotalSeconds):0} s left).";
        }

        string reason = request?.Reason.Trim() ?? string.Empty;
        if (reason.Length < terms.MinReasonLength)
        {
            return $"The reason needs at least {terms.MinReasonLength} characters.";
        }

        connection.EndEarlyTerms = null;
        return await _host.EndEarlyAsync(reason).ConfigureAwait(false);
    }

    /// <summary>Hands a file the phone carried to the host; the phone has it, so it is never sent back on this connection.</summary>
    private async Task<string?> AcceptSyncFileAsync(Connection connection, LinkSyncFile? request)
    {
        byte[] sealedBytes;
        try
        {
            sealedBytes = Convert.FromBase64String(request?.Data ?? string.Empty);
        }
        catch (FormatException)
        {
            return "The sync file isn't readable.";
        }

        if (sealedBytes.Length == 0)
        {
            return "The sync file was empty.";
        }

        connection.MarkSyncFile(HashOf(sealedBytes));
        return await _host.AcceptSyncFileAsync(sealedBytes).ConfigureAwait(false);
    }

    private static LinkEnvelope ResultOf(string? error, string success)
    {
        return LinkEnvelope.Of(LinkMessages.Result, error is null ? new LinkResult(true, success) : new LinkResult(false, error));
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        _ = PushStateAsync();
    }

    /// <summary>Sends the new state to every device; a pending "end early" starts over (the session changed).</summary>
    private async Task PushStateAsync()
    {
        LinkState state = await _host.GetStateAsync().ConfigureAwait(false);
        foreach (Connection connection in _connections.Keys)
        {
            connection.EndEarlyTerms = null;
            try
            {
                await connection.SendAsync(LinkEnvelope.Of(LinkMessages.State, StateFor(connection, state)), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                connection.Close();
            }
        }
    }

    /// <summary>A watch shows the running session only (ADR 21): it doesn't get the list of profiles to start.</summary>
    private static LinkState StateFor(Connection connection, LinkState state)
    {
        // A watch gets neither the profile list nor come-backs (ADR 21, ADR 30).
        return connection.IsWatch ? state with { Profiles = Array.Empty<LinkProfile>(), CountUpProfileId = null, Comebacks = null } : state;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _ = PushSettingsAsync();
    }

    /// <summary>Sends the settings to every device whose copy is older (most changes, e.g. a calendar check, change nothing).</summary>
    private async Task PushSettingsAsync()
    {
        if (_connections.IsEmpty)
        {
            return;
        }

        LinkSettingsMessage settings = await SettingsAsync().ConfigureAwait(false);
        foreach (Connection connection in _connections.Keys.Where(connection => !connection.IsWatch))
        {
            try
            {
                await SendSettingsIfNewerAsync(connection, settings, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                connection.Close();
            }
        }
    }

    private static async Task SendSettingsIfNewerAsync(Connection connection, LinkSettingsMessage settings, CancellationToken cancellationToken)
    {
        if (connection.SettingsVersion == settings.Version)
        {
            return;
        }

        connection.SettingsVersion = settings.Version;
        await connection.SendAsync(LinkEnvelope.Of(LinkMessages.Settings, settings), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The host's settings with their version: a hash of their JSON.</summary>
    private async Task<LinkSettingsMessage> SettingsAsync()
    {
        JsonElement content = await _host.GetSettingsAsync().ConfigureAwait(false);
        string version = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(content, LinkFraming.JsonOptions)));
        return new LinkSettingsMessage(version, content);
    }

    private void OnFocusDayChanged(object? sender, EventArgs e)
    {
        _ = PushFocusDayAsync();
    }

    /// <summary>Sends today's focus to every device whose copy differs (a raise without a change sends nothing).</summary>
    private async Task PushFocusDayAsync()
    {
        if (_connections.IsEmpty)
        {
            return;
        }

        if (await FocusDayAsync().ConfigureAwait(false) is not VersionedFocusDay focusDay)
        {
            return;
        }

        foreach (Connection connection in _connections.Keys)
        {
            try
            {
                await SendFocusDayIfChangedAsync(connection, focusDay, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                connection.Close();
            }
        }
    }

    private static async Task SendFocusDayIfChangedAsync(Connection connection, VersionedFocusDay focusDay, CancellationToken cancellationToken)
    {
        if (connection.FocusDayVersion == focusDay.Version)
        {
            return;
        }

        connection.FocusDayVersion = focusDay.Version;
        await connection.SendAsync(LinkEnvelope.Of(LinkMessages.FocusDay, focusDay.Day), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The host's focus day with a hash of its JSON, to send it only when it changed; null when it couldn't be built
    /// (e.g. the history couldn't be read): nothing is sent then, so devices keep what they had, and signing in and the
    /// requests after it never depend on it.
    /// </summary>
    private async Task<VersionedFocusDay?> FocusDayAsync()
    {
        LinkFocusDay day;
        try
        {
            day = await _host.GetFocusDayAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        string version = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(day, LinkFraming.JsonOptions)));
        return new VersionedFocusDay(version, day);
    }

    private void OnSyncFilesChanged(object? sender, EventArgs e)
    {
        _ = PushSyncFilesAsync();
    }

    /// <summary>Sends every phone the sync files it hasn't had on its connection (a raise without a change sends nothing).</summary>
    private async Task PushSyncFilesAsync()
    {
        if (_connections.IsEmpty || await SyncFilesAsync().ConfigureAwait(false) is not { } files)
        {
            return;
        }

        foreach (Connection connection in _connections.Keys.Where(connection => !connection.IsWatch))
        {
            try
            {
                await SendNewSyncFilesAsync(connection, files, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                connection.Close();
            }
        }
    }

    /// <summary>Sends each file whose hash this connection hasn't sent or received yet, so an unchanged file goes once.</summary>
    private static async Task SendNewSyncFilesAsync(Connection connection, IReadOnlyList<byte[]> files, CancellationToken cancellationToken)
    {
        foreach (byte[] file in files)
        {
            if (!connection.MarkSyncFile(HashOf(file)))
            {
                continue;
            }

            try
            {
                await connection.SendAsync(LinkEnvelope.Of(LinkMessages.SyncFile, new LinkSyncFile(Convert.ToBase64String(file))), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                // Too large for one message: it still travels by the other methods.
            }
        }
    }

    /// <summary>
    /// The host's sync files; null when they couldn't be had: nothing is sent then, and signing in and the requests
    /// after it never depend on them.
    /// </summary>
    private async Task<IReadOnlyList<byte[]>?> SyncFilesAsync()
    {
        try
        {
            return await _host.GetSyncFilesAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static string HashOf(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private sealed record PairingWindow(string Code, DateTimeOffset ExpiresAt, int AttemptsLeft);

    private sealed record VersionedFocusDay(string Version, LinkFocusDay Day);

    /// <summary>One device's connection; sends go one at a time (state pushes and answers can overlap).</summary>
    private sealed class Connection
    {
        private readonly Stream _stream;
        private readonly SemaphoreSlim _sending = new(1, 1);
        private LinkEndEarlyTerms? _endEarlyTerms;
        private string? _settingsVersion;
        private string? _focusDayVersion;

        // Hashes of the sync files sent on this connection or received on it.
        private readonly HashSet<string> _syncFiles = [];

        public Connection(Stream stream)
        {
            _stream = stream;
        }

        public Guid? DeviceId { get; set; }

        /// <summary>A watch: no settings, no requests (ADR 21).</summary>
        public bool IsWatch { get; set; }

        /// <summary>The settings version the device has (from its hello, then each one sent).</summary>
        public string? SettingsVersion
        {
            get => Volatile.Read(ref _settingsVersion);
            set => Volatile.Write(ref _settingsVersion, value);
        }

        /// <summary>A hash of the last focus day sent.</summary>
        public string? FocusDayVersion
        {
            get => Volatile.Read(ref _focusDayVersion);
            set => Volatile.Write(ref _focusDayVersion, value);
        }

        /// <summary>Notes that the device has this sync file (by its hash): sent to it, or received from it.</summary>
        /// <returns>True when it is new on this connection.</returns>
        public bool MarkSyncFile(string hash)
        {
            lock (_syncFiles)
            {
                return _syncFiles.Add(hash);
            }
        }

        /// <summary>The terms given for ending early, until used or the session changes.</summary>
        public LinkEndEarlyTerms? EndEarlyTerms
        {
            get => Volatile.Read(ref _endEarlyTerms);
            set => Volatile.Write(ref _endEarlyTerms, value);
        }

        public async Task SendAsync(LinkEnvelope envelope, CancellationToken cancellationToken)
        {
            await _sending.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await LinkFraming.WriteAsync(_stream, envelope, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sending.Release();
            }
        }

        public void Close()
        {
            try
            {
                _stream.Dispose();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Already closed.
            }
        }
    }
}
