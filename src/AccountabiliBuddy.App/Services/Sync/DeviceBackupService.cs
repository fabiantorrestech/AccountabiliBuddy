using System.Globalization;
using System.Net;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Backups of a removed device's data (ADR 55). A backup is the device's last received sync file, kept as a sealed
/// <c>.absync</c> file, sealed with the group's NEW key (so call it after the key switch, <see cref="SyncRemovalService.RemoveAsync"/>,
/// and before erasing the received copy). It goes to Google Drive, in the app's own folder under
/// <c>Backups/&lt;device name&gt;/&lt;yyyy-MM-dd&gt;.absync</c> (scope drive.file, through <see cref="CalendarNetwork"/>), or to a file the user
/// picks, by default in <c>Documents\AccountabiliBuddy backups</c>. Nothing readable leaves this PC: the file is sealed
/// like any sync file, and live and shared-list parts of the copy (a running session, actions, the shared lists) are
/// left out, since those belong to the devices that stay. Use from the UI thread only.
/// </summary>
public sealed class DeviceBackupService
{
    private readonly IDriveFiles _drive;
    private readonly SyncIdentityService _identity;
    private readonly SyncSettingsService _settings;
    private readonly Func<bool> _driveSignedIn;

    /// <param name="driveSignedIn">True while a Google Drive account is signed in.</param>
    public DeviceBackupService(IDriveFiles drive, SyncIdentityService identity, SyncSettingsService settings, Func<bool> driveSignedIn)
    {
        _drive = drive;
        _identity = identity;
        _settings = settings;
        _driveSignedIn = driveSignedIn;
    }

    /// <summary>Where the default local backups go: <c>Documents\AccountabiliBuddy backups</c>.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AccountabiliBuddy backups");

    /// <summary>True when Google Drive is a sync method in use and signed in: a backup can go there.</summary>
    public bool DriveAvailable => _settings.Current.DriveOn && _driveSignedIn();

    /// <summary>True when a Google Drive account is signed in (backups on it can be listed, whether or not Drive is a sync method).</summary>
    public bool DriveSignedIn => _driveSignedIn();

    /// <summary>"2026-10-28.absync" for the day.</summary>
    public static string FileNameFor(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + SyncFolderService.Extension;

    /// <summary>A file name that is safe on disk for the device's name and the day, e.g. "Pixel 7 2026-10-28.absync".</summary>
    public static string LocalFileNameFor(string deviceName, Guid deviceId, DateOnly day)
    {
        string safe = string.Concat(deviceName.Where(character => !Path.GetInvalidFileNameChars().Contains(character))).Trim();
        return $"{(safe.Length == 0 ? "device" : safe)} ({ShortId(deviceId)}) {FileNameFor(day)}";
    }

    /// <summary>
    /// The Drive folder of a device's backups: its name and the first four characters of its id, so two devices with the same name
    /// (phones are often both "Pixel 7") never share a folder or overwrite each other's backup; "device" when the name is blank.
    /// </summary>
    public static string FolderNameFor(SyncDeviceCard device)
    {
        string name = device.Name.Trim();
        return $"{(name.Length == 0 ? "device" : name)} ({ShortId(device.DeviceId)})";
    }

    private static string ShortId(Guid deviceId) => deviceId.ToString("N")[..4];

    /// <summary>Writes the backup of <paramref name="device"/>'s data to Drive (<c>Backups/[name]/[day].absync</c>).</summary>
    public async Task<BackupOutcome> SaveToDriveAsync(SyncFile device, DateOnly day)
    {
        if (_identity.Key is not SyncKey key)
        {
            return BackupOutcome.Failed("This PC isn't in a device group.");
        }

        if (!DriveAvailable)
        {
            return BackupOutcome.Failed("Google Drive isn't signed in.");
        }

        string name = FolderNameFor(device.Device);
        byte[] sealedBytes = SyncFileCodec.Seal(ForBackup(device), key);
        try
        {
            await _drive.UploadBackupAsync(name, FileNameFor(day), sealedBytes, CancellationToken.None);
            return BackupOutcome.Saved($"Google Drive › {GoogleDriveFiles.FolderName} › {GoogleDriveFiles.BackupsFolderName} › {name} › {FileNameFor(day)}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return BackupOutcome.Failed("Google Drive refused the backup, most likely because it is full.", driveRefused: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return BackupOutcome.Failed("Couldn't reach Google Drive to save the backup.", driveRefused: true);
        }
    }

    /// <summary>Writes the backup of <paramref name="device"/>'s data to <paramref name="path"/>, making its folder when needed.</summary>
    public async Task<BackupOutcome> SaveToFileAsync(SyncFile device, string path)
    {
        if (_identity.Key is not SyncKey key)
        {
            return BackupOutcome.Failed("This PC isn't in a device group.");
        }

        byte[] sealedBytes = SyncFileCodec.Seal(ForBackup(device), key);
        try
        {
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // Written beside the target first, so an interrupted write never leaves half a backup under the real name.
            string temporary = path + ".tmp";
            await File.WriteAllBytesAsync(temporary, sealedBytes);
            File.Move(temporary, path, overwrite: true);
            return BackupOutcome.Saved(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return BackupOutcome.Failed($"Couldn't write the file: {ex.Message}");
        }
    }

    /// <returns>The backups on Google Drive, newest first; empty when Drive isn't signed in, there are none, or Drive can't be reached.</returns>
    public async Task<IReadOnlyList<DriveBackupInfo>> ListDriveAsync()
    {
        if (!_driveSignedIn())
        {
            return [];
        }

        try
        {
            return (await _drive.ListBackupsAsync(CancellationToken.None)).OrderByDescending(backup => backup.ModifiedAt).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <returns>The backup's bytes; null when Drive can't be reached.</returns>
    public async Task<byte[]?> DownloadAsync(DriveBackupInfo backup)
    {
        try
        {
            return await _drive.DownloadAsync(backup.Id, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    // What a backup keeps: the device's card, history, day plan and settings. What it leaves out is live or belongs to the group (a
    // running session, the phone's actions, the shared lists), so restoring it can't bring back a session that is long over or a task someone deleted.
    private static SyncFile ForBackup(SyncFile file)
    {
        return file with
        {
            Running = null,
            Actions = null,
            Acks = null,
            ActionResults = null,
            EndEarly = null,
            Placement = null,
            Events = null,
            NextTasks = null,
            Parked = null,
            Later = null,
            Comebacks = null,
            PastKeys = null,
        };
    }
}
