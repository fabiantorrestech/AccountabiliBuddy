using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The Google Drive method (ADR 26): this PC writes its sealed sync file into the app's Drive folder under the same
/// opaque name as in a sync folder (<see cref="SyncFolderService.FileNameFor"/>) and reads the other devices' files from
/// it. It writes and reads every 20 seconds while a session runs, else every 5 minutes, and writes 5 seconds after its
/// own last change and when a session ends. A file is downloaded again only when Drive says it changed. Nothing is
/// written or read while the switch is off, the PC isn't in a group, or no Drive account is signed in. Use from the UI
/// thread only.
/// </summary>
public sealed class SyncDriveService : IDisposable
{
    private static readonly TimeSpan BusyPoll = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan IdlePoll = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OwnChangeDelay = TimeSpan.FromSeconds(5);

    // A sync file is a few KB; anything this big isn't one.
    private const long MaxFileBytes = 16 * 1024 * 1024;

    private readonly SyncCoordinator _coordinator;
    private readonly SyncSettingsService _settings;
    private readonly SyncIdentityService _identity;
    private readonly ReceivedDevicesService _received;
    private readonly SessionController _sessions;
    private readonly IDriveFiles _drive;
    private readonly SemaphoreSlim _busy = new(1, 1);

    // The files read so far, by Drive id: one Drive says is unchanged isn't downloaded again.
    private readonly Dictionary<string, DateTimeOffset> _seen = [];
    private string? _ownId;
    private string? _problem;
    private DispatcherTimer? _timer;
    private CancellationTokenSource? _pendingWrite;
    private bool _sessionActive;
    private bool _started;
    private bool _disposed;

    public SyncDriveService(
        SyncCoordinator coordinator, SyncSettingsService settings, SyncIdentityService identity, ReceivedDevicesService received,
        SessionController sessions, IDriveFiles drive)
    {
        _coordinator = coordinator;
        _settings = settings;
        _identity = identity;
        _received = received;
        _sessions = sessions;
        _drive = drive;
    }

    /// <summary>False while no Drive account is signed in (set by the app); then nothing is written or read.</summary>
    public Func<bool> IsSignedIn { get; set; } = () => true;

    /// <summary>How often it writes and reads now: 20 seconds while a session runs, else 5 minutes.</summary>
    public TimeSpan PollInterval => _sessions.State.IsActive || InWorkHours?.Invoke() == true ? BusyPoll : IdlePoll;

    /// <summary>True during the day plan's work parts (polled as often as in a session); set by the app.</summary>
    public Func<bool>? InWorkHours { get; set; }

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

    /// <summary>Writes this PC's file to Drive now.</summary>
    public async Task WriteNowAsync()
    {
        if (!TryGetName(out string ownName, out SyncKey? key))
        {
            return;
        }

        await _busy.WaitAsync();
        try
        {
            SyncFile file;
            try
            {
                file = await _coordinator.BuildOwnFileAsync();
            }
            catch (SqliteException)
            {
                return; // The history can't be read right now; the next write tries again.
            }

            byte[] sealedBytes = SyncFileCodec.Seal(file, key);
            try
            {
                _ownId ??= (await _drive.ListAsync(CancellationToken.None)).FirstOrDefault(info => info.Name == ownName)?.Id;
                _ownId = await _drive.UploadAsync(ownName, sealedBytes, _ownId, CancellationToken.None);
                SetProblem(null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
            {
                SetProblem("Can't write to Google Drive right now");
            }
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>Reads the other devices' files from Drive now.</summary>
    public async Task ReadNowAsync()
    {
        if (!TryGetName(out string ownName, out _))
        {
            return;
        }

        await _busy.WaitAsync();
        try
        {
            IReadOnlyList<DriveFileInfo> files;
            try
            {
                files = await _drive.ListAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
            {
                SetProblem("Can't reach Google Drive right now");
                return;
            }

            bool unreadable = false;
            foreach (DriveFileInfo info in files.Where(info => info.Name.EndsWith(SyncFolderService.Extension, StringComparison.OrdinalIgnoreCase)))
            {
                if (info.Name == ownName)
                {
                    _ownId = info.Id;
                    continue;
                }

                if (_seen.TryGetValue(info.Id, out DateTimeOffset seenAt) && seenAt == info.ModifiedAt)
                {
                    continue;
                }

                if (info.Size > MaxFileBytes)
                {
                    _seen[info.Id] = info.ModifiedAt;
                    unreadable = true;
                    continue;
                }

                try
                {
                    byte[] bytes = await _drive.DownloadAsync(info.Id, CancellationToken.None);
                    string? reason = await _coordinator.AcceptSealedAsync(bytes, SyncDeviceCard.MethodDrive);
                    unreadable |= reason == SyncCoordinator.CannotReadReason;
                    if (reason is null || reason == SyncCoordinator.CannotReadReason || reason == SyncCoordinator.OwnFileReason)
                    {
                        _seen[info.Id] = info.ModifiedAt;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SqliteException)
                {
                    // Read again next time.
                }
            }

            SetProblem(unreadable ? "Can't read a file in Google Drive" : null);
        }
        finally
        {
            _busy.Release();
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
    }

    /// <summary>Follows the settings, the group and the sign-in: (re)starts the poll and writes and reads once.</summary>
    public void Restart()
    {
        _timer?.Stop();
        _timer = null;
        _seen.Clear();
        _ownId = null;
        if (!TryGetName(out _, out _))
        {
            SetProblem(null);
            return;
        }

        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) => _ = WriteThenReadAsync();
        _timer.Start();
        _ = WriteThenReadAsync();
    }

    private async Task WriteThenReadAsync()
    {
        await WriteNowAsync();
        await ReadNowAsync();
    }

    private bool TryGetName(out string ownName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SyncKey? key)
    {
        key = _identity.Key;
        if (_disposed || !_settings.Current.DriveOn || key is null || !IsSignedIn())
        {
            ownName = "";
            key = null;
            return false;
        }

        ownName = SyncFolderService.FileNameFor(key.GroupId, _identity.DeviceId);
        return true;
    }

    private void SetProblem(string? problem)
    {
        if (problem is not null)
        {
            _received.SetProblem(problem);
        }
        else if (_problem is not null && _received.LastProblem == _problem)
        {
            _received.SetProblem(null);
        }

        _problem = problem;
    }

    private void OnSettingsChanged(object? sender, SyncSettings settings) => Restart();

    private void OnIdentityChanged(object? sender, EventArgs e) => Restart();

    private void OnOwnDataChanged(object? sender, EventArgs e)
    {
        _pendingWrite = Schedule(_pendingWrite, OwnChangeDelay, WriteNowAsync);
    }

    // A session start or end: the poll speeds up or slows down, and an ended session goes up at once.
    private void OnSessionStateChanged(object? sender, SessionState state)
    {
        if (_sessionActive != state.IsActive && _timer is not null)
        {
            _timer.Interval = PollInterval;
        }

        if (_sessionActive && !state.IsActive)
        {
            _pendingWrite = Schedule(_pendingWrite, OwnChangeDelay, WriteNowAsync);
        }

        _sessionActive = state.IsActive;
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
}
