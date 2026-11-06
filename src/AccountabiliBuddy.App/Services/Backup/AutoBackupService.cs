using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Backup;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// Auto-backup on this PC (ADR 71): synced settings, this PC's settings, and the task lists while they aren't shared. Every 2 minutes
/// each kind's content is built and, when it changed since the last backup (or a day has passed, or the app was just updated), written
/// to <c>&lt;folder&gt;\&lt;kind&gt;\</c> (plain backup files, no secrets; the newest 10 stay) and, with Drive sync on, sealed with the group key to
/// the app's Drive folder (the newest 10 per kind and device stay). History is never auto-backed up. Use from the UI thread only.
/// </summary>
public sealed class AutoBackupService : IDisposable
{
    public const int Keep = 10;

    private const string SettingsKey = "backup.auto.settings";
    private const string StateKey = "backup.auto";
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AtLeastEvery = TimeSpan.FromDays(1);

    private readonly BackupService _backups;
    private readonly SqliteJsonDocumentStore _store;
    private readonly IDriveFiles _drive;
    private readonly SyncIdentityService _identity;
    private readonly SyncSettingsService _sync;
    private readonly Func<bool> _driveSignedIn;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _timer;
    private AutoBackupState _state = AutoBackupState.Empty;
    private bool _checking;

    public AutoBackupService(
        BackupService backups, SqliteJsonDocumentStore store, IDriveFiles drive, SyncIdentityService identity, SyncSettingsService sync,
        Func<bool> driveSignedIn, TimeProvider time)
    {
        _backups = backups;
        _store = store;
        _drive = drive;
        _identity = identity;
        _sync = sync;
        _driveSignedIn = driveSignedIn;
        _time = time;
        _timer = new DispatcherTimer { Interval = CheckEvery };
        _timer.Tick += (_, _) => _ = CheckAsync(force: false);
    }

    public AutoBackupSettings Settings { get; private set; } = AutoBackupSettings.Default;

    /// <summary>Raised after a backup is written or the settings change (the cards show the last time).</summary>
    public event EventHandler? Changed;

    public static string DefaultFolder => DeviceBackupService.DefaultFolder;

    public string FolderFor(BackupKind kind) => Path.Combine(Settings.Folder ?? DefaultFolder, BackupKinds.Title(kind));

    /// <summary>When auto-backup last wrote this kind; null before the first.</summary>
    public DateTimeOffset? LastWritten(BackupKind kind) =>
        _state.WrittenAt?.TryGetValue(BackupKinds.Name(kind), out DateTimeOffset at) == true ? at : null;

    /// <summary>Whether a kind is backed up automatically now (task lists only while they aren't shared).</summary>
    public bool IsOn(BackupKind kind) => kind switch
    {
        BackupKind.SyncedSettings => Settings.Synced,
        BackupKind.DeviceSettings => Settings.Device,
        BackupKind.TaskLists => Settings.Tasks && !_sync.Current.ShareTaskLists,
        _ => false,
    };

    public void LoadBlocking()
    {
        try
        {
            Settings = Task.Run(() => _store.LoadAsync<AutoBackupSettings>(SettingsKey)).GetAwaiter().GetResult() ?? AutoBackupSettings.Default;
            _state = Task.Run(() => _store.LoadAsync<AutoBackupState>(StateKey)).GetAwaiter().GetResult() ?? AutoBackupState.Empty;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Settings = AutoBackupSettings.Default;
            _state = AutoBackupState.Empty;
        }
    }

    /// <param name="appUpdated">This version runs for the first time: every kind is backed up now.</param>
    public void Start(bool appUpdated)
    {
        _timer.Start();
        _ = CheckAsync(force: appUpdated);
    }

    public async Task SaveSettingsAsync(AutoBackupSettings settings)
    {
        Settings = settings;
        await _store.SaveAsync(SettingsKey, settings);
        Changed?.Invoke(this, EventArgs.Empty);
        await CheckAsync(force: false);
    }

    /// <summary>"Back up now": every kind that is on, whether or not it changed.</summary>
    public Task BackUpNowAsync() => CheckAsync(force: true);

    public void Dispose() => _timer.Stop();

    private async Task CheckAsync(bool force)
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        try
        {
            foreach (BackupKind kind in new[] { BackupKind.SyncedSettings, BackupKind.DeviceSettings, BackupKind.TaskLists }.Where(IsOn))
            {
                string json = await _backups.CreateKindJsonAsync(kind);
                string hash = Hash(json);
                string name = BackupKinds.Name(kind);
                DateTimeOffset now = _time.GetUtcNow();
                bool changed = _state.Hashes?.GetValueOrDefault(name) != hash;
                bool due = LastWritten(kind) is not DateTimeOffset last || now - last >= AtLeastEvery;
                if (force || changed || due)
                {
                    await WriteAsync(kind, json, now);
                    _state = new AutoBackupState(
                        new Dictionary<string, string>(_state.Hashes ?? new Dictionary<string, string>()) { [name] = hash },
                        new Dictionary<string, DateTimeOffset>(_state.WrittenAt ?? new Dictionary<string, DateTimeOffset>()) { [name] = now });
                    await _store.SaveAsync(StateKey, _state);
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            // The folder can't be written now (a removed drive, no permission): the next check tries again.
        }
        finally
        {
            _checking = false;
        }
    }

    private async Task WriteAsync(BackupKind kind, string json, DateTimeOffset now)
    {
        string stamp = TimeZoneInfo.ConvertTime(now, _time.LocalTimeZone).ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
        string folder = FolderFor(kind);
        Directory.CreateDirectory(folder);
        string extension = BackupKinds.Extension(kind);
        await File.WriteAllTextAsync(Path.Combine(folder, stamp + extension), BackupEnvelope.Seal(json, BackupProtection.None));
        foreach (string old in Directory.GetFiles(folder, "*" + extension).OrderByDescending(path => path, StringComparer.Ordinal).Skip(Keep))
        {
            File.Delete(old);
        }

        if (Settings.ToDrive && _sync.Current.DriveOn && _driveSignedIn() && _identity.Key is SyncKey key)
        {
            try
            {
                byte[] sealedBytes = SealedSettingsBackup.Seal(kind, json, _identity.DeviceId, _identity.DeviceName, BackupService.AppVersion, now, key);
                string deviceFolder = $"{_identity.DeviceName} ({_identity.DeviceId.ToString("N")[..4]})";
                await _drive.UploadSettingsBackupAsync(BackupKinds.Name(kind), deviceFolder, stamp + SealedSettingsBackup.Extension, sealedBytes, Keep, CancellationToken.None);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
            {
                // Offline or signed out: the local copy is there; the next change or day tries Drive again.
            }
        }
    }

    // The content without its creation time, so an unchanged kind isn't written again just because time passed.
    private static string Hash(string json)
    {
        string stable = JsonNode.Parse(json) is JsonObject content && content.Remove("createdAt") ? content.ToJsonString() : json;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stable)));
    }
}
