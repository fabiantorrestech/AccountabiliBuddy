using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The Google Drive method (ADR 26): this PC writes its sealed sync file into the app's Drive folder under the same
/// opaque name as in a sync folder (<see cref="SyncFolderService.FileNameFor"/>) and reads the other devices' files from
/// it. It writes and reads every 10 seconds while a session runs, the noise player plays or a phone controlled it in
/// the last 10 minutes (<see cref="NoiseBusy"/>), else every 5 minutes, and writes 5 seconds after its
/// own last change and when a session ends. A file is downloaded again only when Drive says it changed. Nothing is
/// written or read while the switch is off, the PC isn't in a group, or no Drive account is signed in. Use from the UI
/// thread only.
/// </summary>
public sealed class SyncDriveService : IDisposable, IRekeyPublisher
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

    // Per file id: the verdict of the last read, kept because _seen skips files already read (see SyncFolderService).
    private readonly HashSet<string> _otherGroupFiles = [];
    private readonly HashSet<string> _groupFiles = [];
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

    /// <summary>How often it writes and reads now: 10 seconds while a session runs, in a work part or while noise is busy, else 5 minutes.</summary>
    public TimeSpan PollInterval =>
        _sessions.State.IsActive || InWorkHours?.Invoke() == true || NoiseBusy?.Invoke() == true ? BusyPoll : IdlePoll;

    /// <summary>True during the day plan's work parts (polled as often as in a session); set by the app.</summary>
    public Func<bool>? InWorkHours { get; set; }

    /// <summary>ADR 79 shared history (GS3b): a pass over the goal history files in the Drive folder, run after a read when due; set by the app.</summary>
    public GoalHistorySync? GoalHistory { get; set; }

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
        _identity.KeySwitched += OnKeySwitched;
        _coordinator.OwnDataChanged += OnOwnDataChanged;
        _sessions.StateChanged += OnSessionStateChanged;
        Restart();
    }

    /// <summary>Writes this PC's file to Drive now. True when the file was uploaded.</summary>
    public async Task<bool> WriteNowAsync()
    {
        if (!TryGetName(out string ownName, out SyncKey? key))
        {
            return false;
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
                return false; // The history can't be read right now; the next write tries again.
            }

            byte[] sealedBytes = SyncFileCodec.Seal(file, key);
            try
            {
                _ownId ??= (await _drive.ListAsync(CancellationToken.None)).FirstOrDefault(info => info.Name == ownName)?.Id;
                _ownId = await _drive.UploadAsync(ownName, sealedBytes, _ownId, CancellationToken.None);
                SetProblem(null);
                _coordinator.NoteSent(SyncDeviceCard.MethodDrive, file.Device.WrittenAt, 0);
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
            {
                SetProblem("Can't write to Google Drive right now");
                return false;
            }
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>Writes the rekey file (ADR 54) into the app's Drive folder beside the devices' files.</summary>
    public async Task<bool?> PublishRekeyAsync(byte[] sealedBytes, string fileName)
    {
        if (!TryGetName(out _, out _))
        {
            return null;
        }

        await _busy.WaitAsync();
        try
        {
            string? existingId = (await _drive.ListAsync(CancellationToken.None)).FirstOrDefault(info => info.Name == fileName)?.Id;
            await _drive.UploadAsync(fileName, sealedBytes, existingId, CancellationToken.None);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>The rekey files (ADR 54) in the app's Drive folder under any of <paramref name="fileNames"/>.</summary>
    public async Task<IReadOnlyList<byte[]>> FindRekeysAsync(IReadOnlyCollection<string> fileNames)
    {
        if (!TryGetName(out _, out _))
        {
            return [];
        }

        await _busy.WaitAsync();
        try
        {
            var found = new List<byte[]>();
            foreach (DriveFileInfo info in (await _drive.ListAsync(CancellationToken.None)).Where(info => fileNames.Contains(info.Name) && info.Size <= MaxFileBytes))
            {
                found.Add(await _drive.DownloadAsync(info.Id, CancellationToken.None));
            }

            return found;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return [];
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
            int read = 0;
            int unreadableCount = 0;
            long started = Environment.TickCount64;
            SyncKey? passKey = _identity.Key;
            string? ownSeenId = null;
            foreach (DriveFileInfo info in files.Where(info => info.Name.EndsWith(SyncFolderService.Extension, StringComparison.OrdinalIgnoreCase)
                && !SyncFolderService.IsConflictCopy(info.Name)))
            {
                // A rekey (ADR 54) switched the group's key during this pass: the rest are the old group's, the next pass reads the new group's.
                if (_identity.Key?.SameGroupAs(passKey) != true)
                {
                    break;
                }

                if (info.Name == ownName)
                {
                    // The first of this PC's own name is its file; any more are stale copies (listed for "Remove my stale file").
                    if (ownSeenId is null)
                    {
                        ownSeenId = info.Id;
                        _ownId = info.Id;
                    }
                    else
                    {
                        _coordinator.Unreadable?.Report(SyncDeviceCard.MethodDrive, info.Id, SyncUnreadableFile.OwnCopy, _identity.DeviceId, ownStale: true);
                    }

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
                    read++;
                    unreadableCount++;
                    if (_identity.Key is SyncKey tooBigKey)
                    {
                        _coordinator.Unreadable?.Report(SyncDeviceCard.MethodDrive, info.Name, "tooBig", _coordinator.DeviceOfName(tooBigKey, info.Name), _coordinator.IsOwnStale(info.Name));
                    }

                    continue;
                }

                try
                {
                    byte[] bytes = await _drive.DownloadAsync(info.Id, CancellationToken.None);
                    read++;
                    string? reason = await _coordinator.AcceptSealedAsync(bytes, SyncDeviceCard.MethodDrive, info.Name);
                    unreadableCount += reason == SyncCoordinator.CannotReadReason ? 1 : 0;
                    _otherGroupFiles.Remove(info.Id);
                    _groupFiles.Remove(info.Id);
                    if (reason == SyncCoordinator.OtherGroupReason)
                    {
                        _otherGroupFiles.Add(info.Id);
                    }
                    else if (reason != SyncCoordinator.CannotReadReason && reason != SyncCoordinator.OwnFileReason
                        && reason != SyncCoordinator.RemovedDeviceReason)
                    {
                        _groupFiles.Add(info.Id);
                    }

                    unreadable |= reason == SyncCoordinator.CannotReadReason;
                    if (reason is null || reason == SyncCoordinator.CannotReadReason || reason == SyncCoordinator.OwnFileReason
                        || reason == SyncCoordinator.RemovedDeviceReason || reason == SyncCoordinator.OtherGroupReason)
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
            _coordinator.NoteReadPass(SyncDeviceCard.MethodDrive, read, 0, unreadableCount, (int)(Environment.TickCount64 - started));
            LastPass = (read, unreadableCount);
            _namesById.Clear();
            foreach (DriveFileInfo listed in files)
            {
                _namesById[listed.Id] = listed.Name;
            }

            _coordinator.Unreadable?.Prune(SyncDeviceCard.MethodDrive, files.Select(info => info.Name).Concat(files.Select(info => info.Id)).ToHashSet());

            // Files that are gone no longer count.
            _otherGroupFiles.IntersectWith(files.Select(info => info.Id));
            _groupFiles.IntersectWith(files.Select(info => info.Id));
            _received.SetNewerLink(SyncDeviceCard.MethodDrive, _otherGroupFiles.Count > 0 && _groupFiles.Count == 0);

            // A lower goal that waited for work hours to end (also when no file changed).
            await _coordinator.RetryGoalAsync();
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>
    /// Removes one of this PC's own stale files from Drive (SYNC1b), by its Drive id as listed (the entry's name): an extra copy of its current
    /// name, or its file of an old group (<see cref="SyncCoordinator.IsOwnStale"/>). Never another device's, never the current file.
    /// </summary>
    public async Task<SyncRemoveOutcome> RemoveOwnStaleAsync(string driveId)
    {
        if (!TryGetName(out string ownName, out _))
        {
            return SyncRemoveOutcome.Refused;
        }

        await _busy.WaitAsync();
        try
        {
            IReadOnlyList<DriveFileInfo> listing = await _drive.ListAsync(CancellationToken.None);
            DriveFileInfo? info = listing.FirstOrDefault(item => item.Id == driveId) ?? listing.FirstOrDefault(item => item.Name == driveId && item.Id != _ownId && _coordinator.IsOwnStale(item.Name));
            if (info is null)
            {
                _coordinator.Unreadable?.Clear(SyncDeviceCard.MethodDrive, driveId);
                return SyncRemoveOutcome.AlreadyGone;
            }

            driveId = info.Id;

            // Before this PC knows which Drive file is its own, a file with its current name could be the real one: never a "duplicate" then.
            bool duplicate = info.Name == ownName && _ownId is not null && info.Id != _ownId;
            if (!duplicate && !_coordinator.IsOwnStale(info.Name))
            {
                return SyncRemoveOutcome.Refused;
            }

            await _drive.DeleteAsync(info.Id, CancellationToken.None);
            _coordinator.Unreadable?.Clear(SyncDeviceCard.MethodDrive, driveId);
            _seen.Remove(driveId);
            return SyncRemoveOutcome.Removed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return SyncRemoveOutcome.Refused;
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>The files the last read pass looked at and how many of them could not be read (SYNC1b's result line).</summary>
    public (int Files, int Problems) LastPass { get; private set; }

    /// <summary>"Re-read everything" (SYNC1b): forgets what was read and downloads and merges every file again (one download each). Merging is idempotent.</summary>
    public async Task<SyncRereadResult> RereadAllAsync()
    {
        if (!TryGetName(out _, out _))
        {
            return SyncRereadResult.None(SyncDeviceCard.MethodDrive);
        }

        ForgetSeen();
        LastPass = (0, 0);
        await ReadNowAsync();
        return new SyncRereadResult(SyncDeviceCard.MethodDrive, LastPass.Files, LastPass.Problems, 0, false);
    }

    // The names of the files in the last listing, by Drive id (to forget one file's "seen" mark by its name).
    private readonly Dictionary<string, string> _namesById = [];

    /// <summary>Makes the next read download every file again (SYNC1b "Re-read everything"/"Try again"); <paramref name="name"/> limits it to one file.</summary>
    public void ForgetSeen(string? name = null)
    {
        foreach (string id in _seen.Keys.Where(id => name is null || (_namesById.TryGetValue(id, out string? known) && known == name)).ToList())
        {
            _seen.Remove(id);
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
            _identity.KeySwitched -= OnKeySwitched;
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
        _coordinator.Unreadable?.ClearAll(SyncDeviceCard.MethodDrive);
        _otherGroupFiles.Clear();
        _groupFiles.Clear();
        _received.SetNewerLink(SyncDeviceCard.MethodDrive, false);
        _ownId = null;
        if (!TryGetName(out _, out _))
        {
            SetProblem(null);
            return;
        }

        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) =>
        {
            // Noise commands ran out of their 10 minutes, or the player stopped: back to the slow rate.
            UpdatePollInterval();
            _ = WriteThenReadAsync();
        };
        _timer.Start();
        _ = WriteThenReadAsync();
    }

    private async Task WriteThenReadAsync()
    {
        await WriteNowAsync();
        await ReadNowAsync();
        await HistoryAsync();
    }

    /// <summary>
    /// A pass over the goal history files in the Drive folder when one is due (<see cref="GoalHistorySync.RunEvery"/>); at once with
    /// <paramref name="force"/>. Waits for any write or read in flight; a Drive failure is not shown (tried again at the next pass).
    /// </summary>
    public async Task HistoryAsync(bool force = false)
    {
        GoalHistorySync? history = GoalHistory;
        if (history is null || _disposed || !_settings.Current.DriveOn || !IsSignedIn())
        {
            return;
        }

        bool inGroup = TryGetName(out _, out _);
        await _busy.WaitAsync();
        try
        {
            var store = new DriveGoalHistoryStore(_drive);
            if (!inGroup)
            {
                // Left the group: the history files this PC put in the Drive folder go (best effort).
                await history.LeaveAsync(store, CancellationToken.None);
                return;
            }

            await (force ? history.RunAsync(store, CancellationToken.None) : history.RunIfDueAsync(store, CancellationToken.None));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException or SqliteException)
        {
            // Tried again at the next pass.
        }
        finally
        {
            _busy.Release();
        }
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

    private void OnIdentityChanged(object? sender, EventArgs e)
    {
        Restart();
        _ = HistoryAsync();
    }

    // The group got a new key (ADR 54): this PC's file and the removed devices' files of the old group leave Drive. Best
    // effort; the rekey file stays, since a device that is behind (or the removed one) still has to read it.
    private void OnKeySwitched(object? sender, SyncKeySwitchedEventArgs e)
    {
        if (_disposed || !_settings.Current.DriveOn || !IsSignedIn())
        {
            return;
        }

        // The old group is dead: this PC's file, the removed devices' and every device this PC knows (their old copies, which
        // the phone may have carried) go; each of them writes a new file under the new key.
        var names = e.RemovedDeviceIds.Append(_identity.DeviceId)
            .Concat(_received.Devices.Select(device => device.File.Device.DeviceId))
            .Select(id => SyncFolderService.FileNameFor(e.OldKey.GroupId, id))
            .ToHashSet();
        _ = DeleteOldFilesAsync(names);
    }

    private async Task DeleteOldFilesAsync(HashSet<string> names)
    {
        await _busy.WaitAsync();
        try
        {
            foreach (DriveFileInfo info in (await _drive.ListAsync(CancellationToken.None)).Where(info => names.Contains(info.Name)))
            {
                await _drive.DeleteAsync(info.Id, CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            // Left for the user to clear; the new group never reads it.
        }
        finally
        {
            _busy.Release();
        }
    }

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
