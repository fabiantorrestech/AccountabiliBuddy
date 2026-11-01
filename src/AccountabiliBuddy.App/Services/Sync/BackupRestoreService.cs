using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Restoring a removed device's backup (ADR 55), from the setup assistant's "Restore from an older device" step. A backup
/// is a sealed sync file (<see cref="DeviceBackupService"/>); it opens only with the key of the group it was made in, which
/// is the group this PC is in once it has linked. Restoring takes it in as a received device under its old name
/// (<see cref="ReceivedDevicesService.AcceptAsync"/>) and records that device as removed, so History and Stats show it as
/// "[name] (removed)" and nothing more is expected from it. Use from the UI thread only.
/// </summary>
public sealed class BackupRestoreService
{
    // A backup is a few KB to a few MB; anything bigger isn't one.
    private const long MaxBackupBytes = 16 * 1024 * 1024;

    private readonly DeviceBackupService _backups;
    private readonly SyncIdentityService _identity;
    private readonly ReceivedDevicesService _received;
    private readonly SyncRemovalService _removal;

    public BackupRestoreService(DeviceBackupService backups, SyncIdentityService identity, ReceivedDevicesService received, SyncRemovalService removal)
    {
        _backups = backups;
        _identity = identity;
        _received = received;
        _removal = removal;
    }

    /// <summary>The Drive backups and Drive's sign-in, for the restore step's list.</summary>
    public DeviceBackupService Backups => _backups;

    /// <summary>True when this PC is in a device group: only then can a backup be opened.</summary>
    public bool CanOpen => _identity.Key is not null;

    /// <summary>Opens a backup's bytes with this PC's group key.</summary>
    public BackupOpening Open(byte[] backupBytes)
    {
        if (_identity.Key is not SyncKey key)
        {
            return new BackupOpening(BackupOpeningKind.NotInGroup, null, "Link this PC to your other devices first: a backup opens only in the group it was made in.");
        }

        if (backupBytes.Length == 0 || backupBytes.Length > MaxBackupBytes)
        {
            return new BackupOpening(BackupOpeningKind.Unreadable, null, "That isn't an AccountabiliBuddy backup.");
        }

        SyncFile? file = SyncFileCodec.Open(backupBytes, key);
        if (file is null)
        {
            // A sync file starts "ABS1" and the 4-byte id of the group it was sealed for: a different id is another group.
            bool isSyncFile = backupBytes.Length >= 8 && backupBytes[0] == (byte)'A' && backupBytes[1] == (byte)'B' && backupBytes[2] == (byte)'S' && backupBytes[3] == (byte)'1';
            bool otherGroup = isSyncFile && !backupBytes.AsSpan(4, 4).SequenceEqual(Convert.FromHexString(key.GroupId));
            return otherGroup
                ? new BackupOpening(BackupOpeningKind.DifferentGroup, null, "Made in a different group; link to that group to restore it.")
                : new BackupOpening(BackupOpeningKind.Unreadable, null, "That backup can't be read. It may be cut off, changed, or from a newer version.");
        }

        return file.Device.DeviceId == _identity.DeviceId
            ? new BackupOpening(BackupOpeningKind.ThisPc, null, "That backup is this PC's own data.")
            : new BackupOpening(BackupOpeningKind.Ready, file, null);
    }

    /// <summary>Brings the backup's device in as a received device and marks it removed (shown as "[name] (removed)").</summary>
    /// <param name="opening">An opening that <see cref="BackupOpening.IsReady"/>.</param>
    public async Task RestoreAsync(BackupOpening opening)
    {
        if (opening.File is not SyncFile file)
        {
            return;
        }

        await _received.AcceptAsync(file, SyncDeviceCard.MethodFile, []);
        await _removal.RecordRestoredAsync(file.Device.DeviceId, file.Device.Name, file.Device.WrittenAt);
    }

    /// <summary>Reads a backup file the user picked.</summary>
    /// <returns>The opening; <see cref="BackupOpeningKind.Unreadable"/> when the file can't be read.</returns>
    public async Task<BackupOpening> OpenFileAsync(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxBackupBytes)
            {
                return new BackupOpening(BackupOpeningKind.Unreadable, null, "That isn't an AccountabiliBuddy backup.");
            }

            return Open(await File.ReadAllBytesAsync(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new BackupOpening(BackupOpeningKind.Unreadable, null, "Couldn't read that file.");
        }
    }

    /// <summary>Downloads a backup from Google Drive and opens it.</summary>
    public async Task<BackupOpening> OpenDriveAsync(DriveBackupInfo backup)
    {
        byte[]? bytes = await _backups.DownloadAsync(backup);
        return bytes is null
            ? new BackupOpening(BackupOpeningKind.Unreadable, null, "Couldn't reach Google Drive to download the backup.")
            : Open(bytes);
    }
}
