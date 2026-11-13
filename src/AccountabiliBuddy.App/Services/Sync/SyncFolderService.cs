using System.Security.Cryptography;
using System.Text;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The sync folder method (ADR 22): this PC writes its sealed sync file as <c>&lt;file id&gt;.absync</c> into the folder
/// the user picked (e.g. inside Google Drive for desktop, which carries it; the app itself opens no connection) and
/// reads the other devices' files from it. It writes at the chosen interval, 5 seconds after its own last change when
/// the interval is Instant, and 5 seconds after every session ends; it reads 2 seconds after the folder last reported a
/// change, and at each interval. Its own file is never read. Nothing is written or read while the switch is off or the
/// PC isn't linked to a group. Use from the UI thread only.
/// </summary>
public sealed class SyncFolderService : IDisposable, IRekeyPublisher
{
    public const string Extension = ".absync";

    private static readonly TimeSpan OwnChangeDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FolderChangeDelay = TimeSpan.FromSeconds(2);

    // A sync file is a few KB; anything this big isn't one, and isn't read into memory.
    private const long MaxFileBytes = 16 * 1024 * 1024;

    private readonly SyncCoordinator _coordinator;
    private readonly SyncSettingsService _settings;
    private readonly SyncIdentityService _identity;
    private readonly ReceivedDevicesService _received;
    private readonly SessionController _sessions;

    // Reads and writes each run one at a time (a folder event and an interval tick can meet).
    private readonly SemaphoreSlim _reading = new(1, 1);
    private readonly SemaphoreSlim _writing = new(1, 1);

    // The files read so far, by path: unchanged ones (same size and write time) aren't read again.
    private readonly Dictionary<string, SeenFile> _seen = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _timer;
    private CancellationTokenSource? _pendingWrite;
    private CancellationTokenSource? _pendingRead;

    // Read by the folder watcher's thread to skip this PC's own file.
    private volatile string? _ownName;

    // Per file path: the verdict of the last read, kept because _seen skips files already read. Together they tell whether the
    // other devices have moved to a newer key (only leftovers of another group here, none of this group from another device).
    private readonly HashSet<string> _otherGroupFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _groupFiles = new(StringComparer.OrdinalIgnoreCase);

    private string? _readProblem;
    private string? _writeProblem;
    private string? _shownProblem;
    private bool _sessionActive;
    private bool _started;
    private bool _disposed;

    public SyncFolderService(
        SyncCoordinator coordinator, SyncSettingsService settings, SyncIdentityService identity, ReceivedDevicesService received, SessionController sessions)
    {
        _coordinator = coordinator;
        _settings = settings;
        _identity = identity;
        _received = received;
        _sessions = sessions;
    }

    /// <summary>
    /// The file name a device writes in the group's folder: the first 6 bytes of SHA-256 over the UTF-8 of the group id
    /// and the device id ("D" format, lower case), as 12 lower-case hex characters, then <see cref="Extension"/>. Opaque:
    /// never a device name.
    /// </summary>
    public static string FileNameFor(string groupId, Guid deviceId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(groupId + deviceId.ToString("D")));
        return Convert.ToHexStringLower(hash.AsSpan(0, 6)) + Extension;
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

    /// <summary>Writes this PC's file now (nothing while the switch is off or outside a group). True when the file was put in the folder.</summary>
    public async Task<bool> WriteNowAsync()
    {
        if (!TryGetFolder(out string folder, out string ownName, out SyncKey? key))
        {
            return false;
        }

        await _writing.WaitAsync();
        try
        {
            SyncFile file;
            try
            {
                file = await _coordinator.BuildOwnFileAsync();
            }
            catch (SqliteException)
            {
                // The history can't be read right now; the next write tries again.
                return false;
            }

            byte[] sealedBytes = SyncFileCodec.Seal(file, key);
            string target = Path.Combine(folder, ownName);
            bool wrote = false;
            try
            {
                // Written beside it and moved over it, so a reader never sees half a file.
                await Task.Run(() =>
                {
                    string temporary = target + ".tmp";
                    File.WriteAllBytes(temporary, sealedBytes);
                    File.Move(temporary, target, overwrite: true);
                });
                _writeProblem = null;
                wrote = true;
                _coordinator.NoteSent(SyncDeviceCard.MethodFolder, file.Device.WrittenAt, 0);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                _writeProblem = $"Can't write to the sync folder {folder}";
            }

            UpdateProblem();
            return wrote;
        }
        finally
        {
            _writing.Release();
        }
    }

    /// <summary>Writes the rekey file (ADR 54) into the folder beside the devices' files, the way <see cref="WriteNowAsync"/> writes this PC's.</summary>
    public async Task<bool?> PublishRekeyAsync(byte[] sealedBytes, string fileName)
    {
        if (!TryGetFolder(out string folder, out _, out _))
        {
            return null;
        }

        await _writing.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                string target = Path.Combine(folder, fileName);
                string temporary = target + ".tmp";
                File.WriteAllBytes(temporary, sealedBytes);
                File.Move(temporary, target, overwrite: true);
            });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            _writing.Release();
        }
    }

    /// <summary>The rekey files (ADR 54) in the folder under any of <paramref name="fileNames"/>.</summary>
    public async Task<IReadOnlyList<byte[]>> FindRekeysAsync(IReadOnlyCollection<string> fileNames)
    {
        if (!TryGetFolder(out string folder, out _, out _))
        {
            return [];
        }

        return await Task.Run(() =>
        {
            var found = new List<byte[]>();
            foreach (string name in fileNames)
            {
                try
                {
                    var file = new FileInfo(Path.Combine(folder, name));
                    if (file.Exists && file.Length <= MaxFileBytes)
                    {
                        found.Add(File.ReadAllBytes(file.FullName));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    // Being written or synced: not counted.
                }
            }

            return (IReadOnlyList<byte[]>)found;
        });
    }

    /// <summary>Reads the other devices' files in the folder now; a file that can't be read shows as "Can't read a file from …".</summary>
    public async Task ReadNowAsync()
    {
        if (!TryGetFolder(out string folder, out string ownName, out _))
        {
            return;
        }

        await _reading.WaitAsync();
        try
        {
            List<FileInfo> files;
            try
            {
                files = await Task.Run(() => new DirectoryInfo(folder).GetFiles("*" + Extension)
                    .Where(file => file.Extension.Equals(Extension, StringComparison.OrdinalIgnoreCase)
                        && !file.Name.Equals(ownName, StringComparison.OrdinalIgnoreCase)
                        && !IsConflictCopy(file.Name))
                    .ToList());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
                _readProblem = $"Can't open the sync folder {folder}";
                UpdateProblem();
                return;
            }

            bool unreadable = false;
            int read = 0;
            int unreadableCount = 0;
            long started = Environment.TickCount64;
            SyncKey? passKey = _identity.Key;
            foreach (FileInfo file in files)
            {
                // A rekey (ADR 54) switched the group's key during this pass: the rest are the old group's, the next pass reads the new group's.
                if (_identity.Key?.SameGroupAs(passKey) != true)
                {
                    break;
                }

                bool? bad = await ReadFileAsync(file);
                unreadable |= bad == true;
                read++;
                unreadableCount += bad == true ? 1 : 0;
            }

            _coordinator.NoteReadPass(SyncDeviceCard.MethodFolder, read, 0, unreadableCount, (int)(Environment.TickCount64 - started));
            LastPass = (read, unreadableCount);

            // This PC's own extra copies the folder's sync made (a "conflict" copy of its own file) are listed as stale (SYNC1b).
            ReportOwnCopies(folder, ownName);
            _coordinator.Unreadable?.Prune(SyncDeviceCard.MethodFolder, GetFolderNames(folder));

            // Forget files that are gone, so a removed bad file no longer counts.
            foreach (string gone in _seen.Keys.Where(path => files.All(file => !file.FullName.Equals(path, StringComparison.OrdinalIgnoreCase))).ToList())
            {
                _seen.Remove(gone);
                _otherGroupFiles.Remove(gone);
                _groupFiles.Remove(gone);
            }

            _received.SetNewerLink(SyncDeviceCard.MethodFolder, _otherGroupFiles.Count > 0 && _groupFiles.Count == 0);

            _readProblem = unreadable ? $"Can't read a file from {folder}" : null;
            UpdateProblem();
        }
        finally
        {
            _reading.Release();
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

        StopWatching();
        _pendingWrite?.Cancel();
        _pendingRead?.Cancel();
    }

    /// <returns>True when the file can't be read (another group's, damaged, too big); false when read; null when it couldn't be opened now.</returns>
    private async Task<bool?> ReadFileAsync(FileInfo file)
    {
        if (_seen.TryGetValue(file.FullName, out SeenFile? seen) && seen.Length == file.Length && seen.WrittenAt == file.LastWriteTimeUtc)
        {
            return seen.Unreadable;
        }

        if (file.Length > MaxFileBytes)
        {
            _seen[file.FullName] = new SeenFile(file.Length, file.LastWriteTimeUtc, Unreadable: true);
            ReportTooBig(file.Name);
            return true;
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(file.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Being written or synced: the next read takes it.
            return null;
        }

        string? reason;
        try
        {
            reason = await _coordinator.AcceptSealedAsync(bytes, SyncDeviceCard.MethodFolder, file.Name);
        }
        catch (SqliteException)
        {
            // Couldn't be kept right now: read again next time.
            return null;
        }

        bool unreadable = reason == SyncCoordinator.CannotReadReason;
        _otherGroupFiles.Remove(file.FullName);
        _groupFiles.Remove(file.FullName);
        if (reason == SyncCoordinator.OtherGroupReason)
        {
            _otherGroupFiles.Add(file.FullName);
        }
        else if (!unreadable && reason != SyncCoordinator.OwnFileReason && reason != SyncCoordinator.RemovedDeviceReason)
        {
            // Opened with this group's key: a file of this group from another device (accepted, or an older copy of one held).
            _groupFiles.Add(file.FullName);
        }
        // Accepted, unreadable or this PC's own: the same bytes give the same answer. An older file is read again.
        if (reason is null || unreadable || reason == SyncCoordinator.OwnFileReason || reason == SyncCoordinator.RemovedDeviceReason
            || reason == SyncCoordinator.OtherGroupReason)
        {
            _seen[file.FullName] = new SeenFile(file.Length, file.LastWriteTimeUtc, unreadable);
        }

        return unreadable;
    }

    // A file over the limit is never read; the troubleshooting list says so, and whose it is when the name tells.
    private void ReportTooBig(string name)
    {
        if (_coordinator.Unreadable is not { } list || _identity.Key is not SyncKey key)
        {
            return;
        }

        list.Report(SyncDeviceCard.MethodFolder, name, "tooBig", _coordinator.DeviceOfName(key, name), _coordinator.IsOwnStale(name));
    }

    // The folder's file names (the listing's names, for dropping entries of files that are gone).
    private static IReadOnlySet<string> GetFolderNames(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).GetFiles("*" + Extension).Select(file => file.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return new HashSet<string>();
        }
    }

    // "<own name without extension> (conflict ...).absync" and the like: this PC's own extra copies, listed so "Remove my stale file" can clear them.
    private void ReportOwnCopies(string folder, string ownName)
    {
        if (_coordinator.Unreadable is not { } list)
        {
            return;
        }

        string stem = Path.GetFileNameWithoutExtension(ownName);
        foreach (string name in GetFolderNames(folder).Where(name => IsConflictCopy(name) && name.StartsWith(stem, StringComparison.OrdinalIgnoreCase)))
        {
            list.Report(SyncDeviceCard.MethodFolder, name, SyncUnreadableFile.OwnCopy, _identity.DeviceId, ownStale: true);
        }
    }

    /// <summary>
    /// Removes one of this PC's own stale files from the folder (SYNC1b): an old group's file (<see cref="SyncCoordinator.IsOwnStale"/>) or an extra
    /// copy of its current file. Never another device's file, never this PC's current one. True when it is gone.
    /// </summary>
    public async Task<SyncRemoveOutcome> RemoveOwnStaleAsync(string name)
    {
        if (!TryGetFolder(out string folder, out string ownName, out _) || string.Equals(name, ownName, StringComparison.OrdinalIgnoreCase)
            || name != Path.GetFileName(name) || !name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            return SyncRemoveOutcome.Refused;
        }

        string stem = Path.GetFileNameWithoutExtension(ownName);
        bool own = _coordinator.IsOwnStale(name) || (IsConflictCopy(name) && name.StartsWith(stem, StringComparison.OrdinalIgnoreCase));
        if (!own)
        {
            return SyncRemoveOutcome.Refused;
        }

        try
        {
            string path = Path.Combine(folder, name);
            bool existed = await Task.Run(() =>
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                File.Delete(path);
                return true;
            });
            _coordinator.Unreadable?.Clear(SyncDeviceCard.MethodFolder, name);
            return existed ? SyncRemoveOutcome.Removed : SyncRemoveOutcome.AlreadyGone;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return SyncRemoveOutcome.Refused;
        }
    }

    /// <summary>The files the last read pass looked at and how many of them could not be read (SYNC1b's result line).</summary>
    public (int Files, int Problems) LastPass { get; private set; }

    /// <summary>"Re-read everything" (SYNC1b): forgets what was read and reads every file in the folder again. Merging is idempotent.</summary>
    public async Task<SyncRereadResult> RereadAllAsync()
    {
        if (!TryGetFolder(out _, out _, out _))
        {
            return SyncRereadResult.None(SyncDeviceCard.MethodFolder);
        }

        ForgetSeen();
        LastPass = (0, 0);
        await ReadNowAsync();
        return new SyncRereadResult(SyncDeviceCard.MethodFolder, LastPass.Files, LastPass.Problems, 0, false);
    }

    /// <summary>
    /// Makes the next read take every file again (SYNC1b "Re-read everything"/"Try again"): the "seen" marks go, so even an unchanged file is
    /// read and merged once more. Merging is idempotent. <paramref name="name"/> limits it to one file.
    /// </summary>
    public void ForgetSeen(string? name = null)
    {
        foreach (string path in _seen.Keys.Where(path => name is null || string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _seen.Remove(path);
        }
    }

    /// <summary>A copy Autosync or Drive made in a clash ("&lt;id&gt; (conflict &lt;date&gt;).absync"): never a device's file, which is named <c>&lt;12 hex&gt;.absync</c>.</summary>
    public static bool IsConflictCopy(string name)
    {
        return name.Contains("(conflict", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetFolder(out string folder, out string ownName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SyncKey? key)
    {
        SyncSettings settings = _settings.Current;
        key = _identity.Key;
        if (_disposed || !settings.FolderOn || string.IsNullOrWhiteSpace(settings.FolderPath) || key is null)
        {
            folder = "";
            ownName = "";
            key = null;
            return false;
        }

        folder = settings.FolderPath;
        ownName = FileNameFor(key.GroupId, _identity.DeviceId);
        return true;
    }

    /// <summary>Follows the current settings and group: watches the folder, starts the interval, and writes and reads once.</summary>
    private void Restart()
    {
        StopWatching();
        _seen.Clear();
        _coordinator.Unreadable?.ClearAll(SyncDeviceCard.MethodFolder);
        _otherGroupFiles.Clear();
        _groupFiles.Clear();
        _received.SetNewerLink(SyncDeviceCard.MethodFolder, false);
        _readProblem = null;
        _writeProblem = null;
        if (!TryGetFolder(out string folder, out string ownName, out _))
        {
            _ownName = null;
            UpdateProblem();
            return;
        }

        _ownName = ownName;
        try
        {
            _watcher = new FileSystemWatcher(folder, "*" + Extension)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Created += OnFolderChanged;
            _watcher.Changed += OnFolderChanged;
            _watcher.Renamed += OnFolderChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            // Read at each interval instead.
            _watcher?.Dispose();
            _watcher = null;
        }

        _timer = new DispatcherTimer { Interval = ReadInterval };
        _timer.Tick += OnTick;
        _timer.Start();
        _ = WriteThenReadAsync();
    }

    private void StopWatching()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
    }

    private async Task WriteThenReadAsync()
    {
        await WriteNowAsync();
        await ReadNowAsync();
    }

    /// <summary>On the watcher's thread: another device's file arrived or changed.</summary>
    private void OnFolderChanged(object sender, FileSystemEventArgs e)
    {
        string? name = e.Name;
        if (name is null || !name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
            || name.Equals(_ownName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Dispatcher.UIThread.Post(() => _pendingRead = Schedule(_pendingRead, FolderChangeDelay, ReadNowAsync));
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // A lunch started or came due since the last tick: the next one follows the new rate.
        UpdateInterval();
        _ = WriteThenReadAsync();
    }

    /// <summary>True while a lunch runs here or in a received file, or one is due soon (<see cref="Core.Sync.LunchListening"/>): the folder is read every minute whatever the interval setting; set by the app.</summary>
    public Func<bool>? LunchBusy { get; set; }

    /// <summary>Follows the lunch state now (a lunch started or ended), not only at the next tick.</summary>
    public void UpdateInterval()
    {
        TimeSpan interval = ReadInterval;
        if (_timer is not null && _timer.Interval != interval)
        {
            _timer.Interval = interval;
        }
    }

    /// <summary>How often the folder is read now: the interval setting, but at most a minute while a lunch runs or is due (every device listens fast then).</summary>
    public TimeSpan ReadInterval
    {
        get
        {
            TimeSpan setting = IntervalOf(_settings.Current.Interval);
            TimeSpan fast = TimeSpan.FromMinutes(1);
            return LunchBusy?.Invoke() == true && setting > fast ? fast : setting;
        }
    }

    private void OnSettingsChanged(object? sender, SyncSettings settings)
    {
        Restart();
    }

    private void OnIdentityChanged(object? sender, EventArgs e)
    {
        Restart();
    }

    // The group got a new key (ADR 54): this PC's file and the removed devices' files of the old group leave the folder.
    // Best effort; the rekey file stays, since a device that is behind (or the removed one) still has to read it.
    private void OnKeySwitched(object? sender, SyncKeySwitchedEventArgs e)
    {
        SyncSettings settings = _settings.Current;
        if (_disposed || !settings.FolderOn || string.IsNullOrWhiteSpace(settings.FolderPath))
        {
            return;
        }

        string folder = settings.FolderPath;
        // The old group is dead: this PC's file, the removed devices' and every device this PC knows (their old copies, which
        // the phone may have carried) go; each of them writes a new file under the new key.
        List<string> names = e.RemovedDeviceIds.Append(_identity.DeviceId)
            .Concat(_received.Devices.Select(device => device.File.Device.DeviceId))
            .Distinct()
            .Select(id => FileNameFor(e.OldKey.GroupId, id))
            .ToList();
        _ = Task.Run(() =>
        {
            foreach (string name in names)
            {
                try
                {
                    File.Delete(Path.Combine(folder, name));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    // Left for the user to clear; the new group never reads it.
                }
            }
        });
    }

    private void OnOwnDataChanged(object? sender, EventArgs e)
    {
        if (_settings.Current.Interval == SyncInterval.Instant)
        {
            _pendingWrite = Schedule(_pendingWrite, OwnChangeDelay, WriteNowAsync);
        }
    }

    /// <summary>
    /// Writes this PC's file in about 5 seconds whatever the interval (a lunch changed here, or one taken from another device), so the
    /// other devices have it as soon as the folder's own sync moves it. Repeated calls join into one write.
    /// </summary>
    public void WriteSoon()
    {
        _pendingWrite = Schedule(_pendingWrite, OwnChangeDelay, WriteNowAsync);
    }

    /// <summary>Always written when a session ends, once its end is in the history.</summary>
    private void OnSessionStateChanged(object? sender, SessionState state)
    {
        if (_sessionActive && !state.IsActive)
        {
            _pendingWrite = Schedule(_pendingWrite, OwnChangeDelay, WriteNowAsync);
        }

        _sessionActive = state.IsActive;
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread once <paramref name="delay"/> has passed with no newer request.</summary>
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

    /// <summary>Shows this service's problem (writing first), or clears the one it showed; never clears another method's.</summary>
    private void UpdateProblem()
    {
        string? problem = _writeProblem ?? _readProblem;
        if (problem is not null)
        {
            _received.SetProblem(problem);
        }
        else if (_shownProblem is not null && _received.LastProblem == _shownProblem)
        {
            _received.SetProblem(null);
        }

        _shownProblem = problem;
    }

    /// <summary>Instant still writes and reads every minute, so devices see it's there.</summary>
    private static TimeSpan IntervalOf(SyncInterval interval)
    {
        return interval switch
        {
            SyncInterval.FiveMinutes => TimeSpan.FromMinutes(5),
            SyncInterval.FifteenMinutes => TimeSpan.FromMinutes(15),
            _ => TimeSpan.FromMinutes(1),
        };
    }

    private sealed record SeenFile(long Length, DateTime WrittenAt, bool Unreadable);
}
