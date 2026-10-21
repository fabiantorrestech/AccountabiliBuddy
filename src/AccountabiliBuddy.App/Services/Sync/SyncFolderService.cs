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
public sealed class SyncFolderService : IDisposable
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
        _coordinator.OwnDataChanged += OnOwnDataChanged;
        _sessions.StateChanged += OnSessionStateChanged;
        Restart();
    }

    /// <summary>Writes this PC's file now (nothing while the switch is off or outside a group).</summary>
    public async Task WriteNowAsync()
    {
        if (!TryGetFolder(out string folder, out string ownName, out SyncKey? key))
        {
            return;
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
                return;
            }

            byte[] sealedBytes = SyncFileCodec.Seal(file, key);
            string target = Path.Combine(folder, ownName);
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
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                _writeProblem = $"Can't write to the sync folder {folder}";
            }

            UpdateProblem();
        }
        finally
        {
            _writing.Release();
        }
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
                        && !file.Name.Equals(ownName, StringComparison.OrdinalIgnoreCase))
                    .ToList());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
                _readProblem = $"Can't open the sync folder {folder}";
                UpdateProblem();
                return;
            }

            bool unreadable = false;
            foreach (FileInfo file in files)
            {
                bool? bad = await ReadFileAsync(file);
                unreadable |= bad == true;
            }

            // Forget files that are gone, so a removed bad file no longer counts.
            foreach (string gone in _seen.Keys.Where(path => files.All(file => !file.FullName.Equals(path, StringComparison.OrdinalIgnoreCase))).ToList())
            {
                _seen.Remove(gone);
            }

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
            reason = await _coordinator.AcceptSealedAsync(bytes, SyncDeviceCard.MethodFolder);
        }
        catch (SqliteException)
        {
            // Couldn't be kept right now: read again next time.
            return null;
        }

        bool unreadable = reason == SyncCoordinator.CannotReadReason;
        // Accepted, unreadable or this PC's own: the same bytes give the same answer. An older file is read again.
        if (reason is null || unreadable || reason == SyncCoordinator.OwnFileReason)
        {
            _seen[file.FullName] = new SeenFile(file.Length, file.LastWriteTimeUtc, unreadable);
        }

        return unreadable;
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

        _timer = new DispatcherTimer { Interval = IntervalOf(_settings.Current.Interval) };
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
        _ = WriteThenReadAsync();
    }

    private void OnSettingsChanged(object? sender, SyncSettings settings)
    {
        Restart();
    }

    private void OnIdentityChanged(object? sender, EventArgs e)
    {
        Restart();
    }

    private void OnOwnDataChanged(object? sender, EventArgs e)
    {
        if (_settings.Current.Interval == SyncInterval.Instant)
        {
            _pendingWrite = Schedule(_pendingWrite, OwnChangeDelay, WriteNowAsync);
        }
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
