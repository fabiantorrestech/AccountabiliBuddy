using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The own-relay method (ADR 46): this PC puts its sealed sync file on the user's relay under the same opaque name as in
/// a sync folder (<see cref="SyncFolderService.FileNameFor"/>) and reads the other devices' files from it. The relay
/// pushes a "changed" event over an open stream, so a file the other devices wrote is read within about a second; the
/// stream reconnects with a wait of 1 to 60 seconds. A change on this PC goes up 1 second later, and never more than
/// once in 3 seconds. A fallback tick reads again in case the stream missed something: every 5 minutes while the
/// stream is open, and while it is down every 10 seconds during a session, a work part or busy noise, else 5 minutes.
/// It writes only when this PC's data changed since the last upload or the file is 5 minutes old. A file is downloaded only
/// when its etag changed, and the events of this PC's own file are ignored, so a write can't cause another write. When
/// the PC leaves or changes group, its file is deleted from the relay. Nothing is sent while the switch is off, the PC
/// isn't in a group or the address is invalid. The relay's calls all go through <see cref="CalendarNetwork"/>; the
/// token and the sealed bytes are never logged. Use from the UI thread only.
/// </summary>
public sealed class SyncRelayService : IDisposable
{
    private static readonly TimeSpan BusyPoll = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan IdlePoll = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OwnChangeDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MinWriteGap = TimeSpan.FromSeconds(3);

    // Even with nothing new, the file is put again this often, so the other devices see this PC as up to date.
    private static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReadAfterEventDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LastRetry = TimeSpan.FromSeconds(60);

    // The relay refused this PC or its certificate: waiting a minute again and again won't change that.
    private static readonly TimeSpan RefusedRetry = TimeSpan.FromMinutes(5);

    // The relay itself refuses a bigger file.
    private const long MaxFileBytes = 4 * 1024 * 1024;

    private readonly SyncCoordinator _coordinator;
    private readonly SyncSettingsService _settings;
    private readonly SyncIdentityService _identity;
    private readonly ReceivedDevicesService _received;
    private readonly SessionController _sessions;
    private readonly CalendarNetwork _network;
    private readonly SemaphoreSlim _busy = new(1, 1);

    // The files read so far, by name: one the relay says is unchanged isn't downloaded again.
    private readonly Dictionary<string, string> _seen = [];

    // What the service is running with, fixed at each Restart.
    private RelayAccess? _access;
    private SyncKey? _key;
    private string _ownName = string.Empty;
    private string? _appliedKey;
    private CancellationTokenSource _lifetime = new();

    private string? _lastPutEtag;
    private long _lastWriteTick = -1_000_000;

    // This PC's data changed since the last upload (or there was none yet): the next tick writes it.
    private bool _ownDirty = true;
    private string? _writeProblem;
    private string? _readProblem;
    private string? _streamProblem;
    private string? _shownProblem;
    private DispatcherTimer? _timer;
    private CancellationTokenSource? _pendingWrite;
    private CancellationTokenSource? _pendingRead;
    private bool _sessionActive;
    private bool _started;
    private bool _disposed;

    public SyncRelayService(
        SyncCoordinator coordinator, SyncSettingsService settings, SyncIdentityService identity, ReceivedDevicesService received,
        SessionController sessions, CalendarNetwork network)
    {
        _coordinator = coordinator;
        _settings = settings;
        _identity = identity;
        _received = received;
        _sessions = sessions;
        _network = network;
    }

    /// <summary>Raised when <see cref="Problem"/> or <see cref="IsConnected"/> changes.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Why the relay can't be used right now (in plain words, never a token or a path); null when it works.</summary>
    public string? Problem => _writeProblem ?? _readProblem ?? _streamProblem;

    /// <summary>True while the relay's push stream is open.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// The fallback tick: 5 minutes while the push stream is open (changes already travel by push), else, while it is down,
    /// 10 seconds during a session, a work part or busy noise, and 5 minutes otherwise.
    /// </summary>
    public TimeSpan PollInterval =>
        !IsConnected && (_sessions.State.IsActive || InWorkHours?.Invoke() == true || NoiseBusy?.Invoke() == true) ? BusyPoll : IdlePoll;

    /// <summary>True during the day plan's work parts (polled as often as in a session); set by the app.</summary>
    public Func<bool>? InWorkHours { get; set; }

    /// <summary>True while the noise player plays and for 10 minutes after a phone's noise command (ADR 42); set by the app.</summary>
    public Func<bool>? NoiseBusy { get; set; }

    /// <summary>Follows the busy state now (noise started playing, or a noise command came in), not only at the next poll.</summary>
    public void UpdatePollInterval()
    {
        if (_timer is not null && _timer.Interval != PollInterval)
        {
            _timer.Interval = PollInterval;
        }
    }

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _sessionActive = _sessions.State.IsActive;
        _settings.Changed += OnSettingsChanged;
        _identity.Changed += OnIdentityChanged;
        _coordinator.OwnDataChanged += OnOwnDataChanged;
        _sessions.StateChanged += OnSessionStateChanged;
        Restart();
    }

    /// <summary>Seals this PC's file and puts it on the relay now.</summary>
    public async Task WriteNowAsync()
    {
        RelayAccess? access = _access;
        SyncKey? key = _key;
        string ownName = _ownName;
        if (_disposed || access is null || key is null)
        {
            return;
        }

        CancellationToken token = _lifetime.Token;
        await _busy.WaitAsync();
        try
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            // Changes during the write set it again.
            _ownDirty = false;
            SyncFile file;
            try
            {
                file = await _coordinator.BuildOwnFileAsync();
            }
            catch (SqliteException)
            {
                _ownDirty = true;
                return; // The history can't be read right now; the next write tries again.
            }

            byte[] sealedBytes = SyncFileCodec.Seal(file, key);
            try
            {
                _lastPutEtag = await _network.PutRelayFileAsync(access, ownName, sealedBytes, token);
                _lastWriteTick = Environment.TickCount64;
                SetWriteProblem(null);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Restarted or stopped while writing.
                _ownDirty = true;
            }
            catch (Exception ex) when (IsRelayFailure(ex))
            {
                _ownDirty = true;
                SetWriteProblem(Describe(ex, access));
            }
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>Reads the other devices' files from the relay now (only the ones whose etag changed).</summary>
    public async Task ReadNowAsync()
    {
        RelayAccess? access = _access;
        string ownName = _ownName;
        if (_disposed || access is null)
        {
            return;
        }

        CancellationToken token = _lifetime.Token;
        await _busy.WaitAsync();
        try
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            IReadOnlyList<RelayFileInfo> files;
            try
            {
                files = await _network.ListRelayFilesAsync(access, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (IsRelayFailure(ex))
            {
                SetReadProblem(Describe(ex, access));
                return;
            }

            bool unreadable = false;
            string? downloadProblem = null;
            foreach (RelayFileInfo info in files.Where(info => IsSyncFileName(info.Name)))
            {
                if (info.Name == ownName || (_seen.TryGetValue(info.Name, out string? seenTag) && seenTag == info.ETag))
                {
                    continue;
                }

                if (info.Size > MaxFileBytes)
                {
                    _seen[info.Name] = info.ETag;
                    unreadable = true;
                    continue;
                }

                try
                {
                    RelayDownload? download = await _network.GetRelayFileAsync(access, info.Name, token);
                    if (download is null)
                    {
                        continue; // Gone since the listing.
                    }

                    string? reason = await _coordinator.AcceptSealedAsync(download.Bytes, SyncDeviceCard.MethodRelay);
                    unreadable |= reason == SyncCoordinator.CannotReadReason;
                    if (reason is null || reason == SyncCoordinator.CannotReadReason || reason == SyncCoordinator.OwnFileReason)
                    {
                        _seen[info.Name] = download.ETag ?? info.ETag;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (IsRelayFailure(ex) || ex is SqliteException)
                {
                    // Read again next time.
                    downloadProblem = ex is SqliteException ? null : Describe(ex, access);
                }
            }

            SetReadProblem(downloadProblem ?? (unreadable ? "Can't read a file on the relay: it's from another device group, or it's damaged." : null));

            // A lower goal that waited for work hours to end (also when no file changed).
            await _coordinator.RetryGoalAsync();
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>
    /// Checks an address and pin the user typed (before or after saving them): a call to the relay that creates nothing.
    /// With this PC in a group it asks for the group's file list (so a relay that belongs to another group says so);
    /// otherwise for a made-up group, which only proves the relay answers and the certificate is accepted.
    /// </summary>
    /// <returns>One line in plain words.</returns>
    public async Task<string> TestAsync(string? addressText, string? pinText)
    {
        if (!RelayAddress.TryNormalize(addressText, out Uri? address))
        {
            return "That isn't an https address. Example: https://192.168.1.20:8743";
        }

        if (!RelayAddress.TryNormalizePin(pinText, out string? pin))
        {
            return "The pin is the 43-character fingerprint the relay prints (letters, digits, - and _).";
        }

        // The group's own id and token go only to the relay already saved here; an address that isn't saved yet gets a
        // made-up group, so a mistyped or unfamiliar address learns nothing about the group.
        SyncSettings saved = _settings.Current;
        bool isSavedRelay = RelayAddress.TryNormalize(saved.RelayUrl, out Uri? savedAddress) && savedAddress == address
            && RelayAddress.TryNormalizePin(saved.RelayPin, out string? savedPin) && savedPin == pin;
        SyncKey? key = isSavedRelay ? _identity.Key : null;
        RelayAccess access = key is null
            ? new RelayAccess(address, pin, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)), Base64Url(RandomNumberGenerator.GetBytes(32)))
            : RelayAccess.For(key, address, pin, null);
        string certificate = pin is null ? "its certificate is trusted" : "the pinned certificate matches";
        try
        {
            IReadOnlyList<RelayFileInfo> files = await _network.ListRelayFilesAsync(access, CancellationToken.None);
            return key is null ? $"Reached the relay: {certificate}."
                : files.Count == 0 ? $"Reached the relay: {certificate}. It has no files of your group yet."
                : $"Reached the relay: {certificate}. {files.Count} file(s) of your group are there.";
        }
        catch (Exception ex) when (IsRelayFailure(ex))
        {
            return Describe(ex, access);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_started)
        {
            _settings.Changed -= OnSettingsChanged;
            _identity.Changed -= OnIdentityChanged;
            _coordinator.OwnDataChanged -= OnOwnDataChanged;
            _sessions.StateChanged -= OnSessionStateChanged;
        }

        _timer?.Stop();
        _pendingWrite?.Cancel();
        _pendingRead?.Cancel();
        _lifetime.Cancel();
    }

    /// <summary>
    /// Follows the settings and the group: stops what ran, then (when the switch is on and the PC is in a group) writes
    /// and reads once, opens the push stream and starts the fallback poll. Does nothing when no relay setting and not
    /// the group changed.
    /// </summary>
    public void Restart()
    {
        if (_disposed)
        {
            return;
        }

        SyncSettings settings = _settings.Current;
        string applied = $"{settings.RelayOn}|{settings.RelayUrl}|{settings.RelayPin}|{settings.RelayAdmissionProtected}|{_identity.Key?.GroupId}";
        if (applied == _appliedKey)
        {
            return;
        }

        _appliedKey = applied;
        RelayAccess? previous = _access;
        SyncKey? previousKey = _key;
        string previousName = _ownName;
        _lifetime.Cancel();
        _lifetime = new CancellationTokenSource();
        _timer?.Stop();
        _timer = null;
        _pendingWrite?.Cancel();
        _pendingRead?.Cancel();
        _seen.Clear();
        _lastPutEtag = null;
        _ownDirty = true;
        IsConnected = false;
        _writeProblem = null;
        _readProblem = null;
        _streamProblem = null;

        // Left the group, or joined or made another: the old group's file leaves the relay.
        if (previous is not null && previousKey?.GroupId != _identity.Key?.GroupId)
        {
            _ = ForgetFileAsync(previous, previousName);
        }

        _key = _identity.Key;
        _ownName = _key is null ? string.Empty : SyncFolderService.FileNameFor(_key.GroupId, _identity.DeviceId);
        if (!TryResolve(out RelayAccess? access, out string? settingsProblem))
        {
            _access = null;
            SetReadProblem(settingsProblem);
            return;
        }

        _access = access;

        PublishProblem();
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) =>
        {
            // Noise commands ran out of their 10 minutes, or the player stopped: back to the slow rate.
            UpdatePollInterval();
            _ = FallbackTickAsync();
        };
        _timer.Start();
        _ = RunAsync(access, _lifetime.Token);
    }

    // The first write and read, then the push stream until this run is stopped.
    private async Task RunAsync(RelayAccess access, CancellationToken token)
    {
        await WriteThenReadAsync();
        await ListenAsync(access, token);
    }

    private async Task WriteThenReadAsync()
    {
        await WriteNowAsync();
        await ReadNowAsync();
    }

    // The fallback tick always reads (in case the push stream missed something). It writes only when this PC's data
    // changed since the last upload or the heartbeat is due: changes already go up about a second after they happen.
    private async Task FallbackTickAsync()
    {
        bool heartbeatDue = Environment.TickCount64 - _lastWriteTick >= Heartbeat.TotalMilliseconds;
        if (_ownDirty || heartbeatDue)
        {
            await WriteNowAsync();
        }

        await ReadNowAsync();
    }

    // Keeps the push stream open, waiting longer after each failure (1 s up to 60 s) and starting over once connected.
    private async Task ListenAsync(RelayAccess access, CancellationToken token)
    {
        TimeSpan delay = FirstRetry;
        while (!token.IsCancellationRequested)
        {
            bool connected = false;
            TimeSpan wait = delay;
            try
            {
                await foreach (RelayChange change in _network.ReadRelayEventsAsync(access, () =>
                {
                    connected = true;
                    OnConnected();
                }, token))
                {
                    OnChange(change);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (IsRelayFailure(ex))
            {
                // A relay without this group yet (404), or one that can't be reached, is told by the writes and reads.
                // A refusal or a certificate that doesn't match is shown, and retried slowly.
                bool refused = ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } || IsCertificateFailure(ex);
                SetStreamProblem(refused ? Describe(ex, access) : null);
                if (refused)
                {
                    wait = RefusedRetry;
                }
            }

            IsConnected = false;
            UpdatePollInterval();
            PublishProblem();
            delay = connected ? FirstRetry : TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, LastRetry.Ticks));
            try
            {
                await Task.Delay(connected ? FirstRetry : wait, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void OnConnected()
    {
        IsConnected = true;
        UpdatePollInterval();
        SetStreamProblem(null);

        // Whatever changed before the stream opened.
        _pendingRead = Schedule(_pendingRead, ReadAfterEventDelay, ReadNowAsync);
    }

    private void OnChange(RelayChange change)
    {
        // This PC's own write comes back as an event: it is not news, and acting on it would loop.
        if (change.Name == _ownName || (change.ETag.Length > 0 && change.ETag == _lastPutEtag) || !IsSyncFileName(change.Name))
        {
            return;
        }

        if (change.ETag.Length == 0)
        {
            _seen.Remove(change.Name); // Deleted: the device left the group. What came from it stays.
            return;
        }

        if (_seen.TryGetValue(change.Name, out string? seenTag) && seenTag == change.ETag)
        {
            return;
        }

        _pendingRead = Schedule(_pendingRead, ReadAfterEventDelay, ReadNowAsync);
    }

    private bool TryResolve([NotNullWhen(true)] out RelayAccess? access, out string? settingsProblem)
    {
        access = null;
        settingsProblem = null;
        SyncKey? key = _identity.Key;
        SyncSettings settings = _settings.Current;
        if (!settings.RelayOn || key is null)
        {
            return false;
        }

        if (!RelayAddress.TryNormalize(settings.RelayUrl, out Uri? address))
        {
            settingsProblem = "The relay address isn't an https address. Fix it in Devices.";
            return false;
        }

        // A pin that doesn't parse stops the relay; it is never turned into "no pin".
        if (!RelayAddress.TryNormalizePin(settings.RelayPin, out string? pin))
        {
            settingsProblem = "The relay pin isn't a certificate fingerprint. Fix it in Devices.";
            return false;
        }

        access = RelayAccess.For(key, address, pin, _settings.ReadRelayAdmission());
        return true;
    }

    // Best effort: a relay that can't be reached keeps the file until its 30-day cleanup.
    // It waits for the write that may be in flight (it was cancelled by the restart, and any write waiting behind it sees
    // the cancelled token and does nothing), so the old file can't land on the relay after the delete.
    private async Task ForgetFileAsync(RelayAccess access, string ownName)
    {
        await _busy.WaitAsync();
        try
        {
            await _network.DeleteRelayFileAsync(access, ownName, CancellationToken.None);
        }
        catch (Exception ex) when (IsRelayFailure(ex))
        {
            // Nothing more to do.
        }
        finally
        {
            _busy.Release();
        }
    }

    // Writing, reading and the push stream each keep their own problem, so one succeeding doesn't hide another failing.
    private void SetWriteProblem(string? problem)
    {
        _writeProblem = problem;
        PublishProblem();
    }

    private void SetReadProblem(string? problem)
    {
        _readProblem = problem;
        PublishProblem();
    }

    private void SetStreamProblem(string? problem)
    {
        _streamProblem = problem;
        PublishProblem();
    }

    private void PublishProblem()
    {
        string? problem = Problem;
        if (problem is not null)
        {
            _received.SetProblem(problem);
        }
        else if (_shownProblem is not null && _received.LastProblem == _shownProblem)
        {
            _received.SetProblem(null);
        }

        _shownProblem = problem;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsChanged(object? sender, SyncSettings settings) => Restart();

    private void OnIdentityChanged(object? sender, EventArgs e) => Restart();

    private void OnOwnDataChanged(object? sender, EventArgs e) => ScheduleOwnWrite();

    // A session start or end: the poll speeds up or slows down, and the change goes up at once.
    private void OnSessionStateChanged(object? sender, SessionState state)
    {
        if (_sessionActive != state.IsActive && _timer is not null)
        {
            _timer.Interval = PollInterval;
        }

        if (_sessionActive != state.IsActive)
        {
            ScheduleOwnWrite();
        }

        _sessionActive = state.IsActive;
    }

    // One second after the change, but never within 3 seconds of the last write.
    private void ScheduleOwnWrite()
    {
        if (_access is null)
        {
            return;
        }

        _ownDirty = true;
        TimeSpan sinceLast = TimeSpan.FromMilliseconds(Environment.TickCount64 - _lastWriteTick);
        TimeSpan wait = MinWriteGap - sinceLast > OwnChangeDelay ? MinWriteGap - sinceLast : OwnChangeDelay;
        _pendingWrite = Schedule(_pendingWrite, wait, WriteNowAsync);
    }

    private CancellationTokenSource Schedule(CancellationTokenSource? previous, TimeSpan delay, Func<Task> action)
    {
        previous?.Cancel();
        var next = new CancellationTokenSource();
        CancellationToken token = next.Token;
        Task.Delay(delay, token).ContinueWith(
            _ => Dispatcher.UIThread.Post(() =>
            {
                if (!token.IsCancellationRequested && !_disposed)
                {
                    _ = action();
                }
            }),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
        return next;
    }

    private static bool IsSyncFileName(string name)
    {
        // 12 lower-case hex characters plus the extension, exactly what a device writes (SyncFolderService.FileNameFor).
        return name.Length == 12 + SyncFolderService.Extension.Length
            && name.EndsWith(SyncFolderService.Extension, StringComparison.Ordinal)
            && name.AsSpan(0, 12).IndexOfAnyExcept("0123456789abcdef") < 0;
    }

    private static bool IsRelayFailure(Exception ex)
    {
        return ex is HttpRequestException or OperationCanceledException or JsonException or IOException
            or InvalidOperationException or KeyNotFoundException or FormatException;
    }

    private static bool IsCertificateFailure(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
            {
                return true;
            }
        }

        return ex is HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError };
    }

    // What to tell the user, in plain words. Nothing from the request (address, token) goes in.
    private static string Describe(Exception ex, RelayAccess access)
    {
        if (ex is HttpRequestException { StatusCode: HttpStatusCode status })
        {
            return status switch
            {
                HttpStatusCode.Unauthorized => "The relay didn't accept this PC's sign-in (401). Check the address, and that the relay is up to date.",
                HttpStatusCode.Forbidden => "This group already belongs to someone else on that relay (403). Use a relay of your own, or clear the group's files on it.",
                HttpStatusCode.PreconditionRequired => "This relay needs its admission key to start a new group (428). Enter the key it printed in Devices > Own relay, or join with the link from a device that has it.",
                HttpStatusCode.RequestEntityTooLarge =>"The sync file is too big for the relay (413).",
                HttpStatusCode.TooManyRequests => "The relay is limiting this PC for now (429). It will try again.",
                HttpStatusCode.InsufficientStorage => "The relay's disk is full (507).",
                _ => $"The relay answered with an error ({(int)status}).",
            };
        }

        if (IsCertificateFailure(ex))
        {
            return access.Pin is null
                ? "The relay's certificate isn't trusted. If it is your own relay with a self-signed certificate, enter its pin."
                : "The relay's certificate doesn't match the pin, so nothing was sent. If you changed the relay's certificate, enter its new pin; otherwise something may be sitting between this PC and the relay.";
        }

        return "Can't reach the relay right now.";
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
