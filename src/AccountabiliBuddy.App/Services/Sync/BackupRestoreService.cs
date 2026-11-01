using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Restoring a removed device's backup (ADR 55), from the setup assistant's "Restore from an older device" step. A backup
/// is a sealed sync file (<see cref="DeviceBackupService"/>) sealed with the key the group had when it was made. It opens with
/// the group's current key, or with the past key of the group named in its header (<see cref="PastKeysService"/>, ADR 56), so a
/// backup survives later removals. Restoring takes it in as a received device under its old name
/// (<see cref="ReceivedDevicesService.AcceptAsync"/>) and records that device as removed, so History and Stats show it as
/// "[name] (removed)" and nothing more is expected from it. Use from the UI thread only.
/// </summary>
public sealed class BackupRestoreService
{
    // A backup is a few KB to a few MB; anything bigger isn't one.
    private const long MaxBackupBytes = 16 * 1024 * 1024;

    private const string DifferentGroupMessage =
        "This backup was made in a different group, or in one older than the keys your devices keep (the 20 newest, up to a year), "
        + "so it can't be opened here. Link this PC to the group it was made in, or restore it on a device that holds its key.";

    private readonly DeviceBackupService _backups;
    private readonly SyncIdentityService _identity;
    private readonly ReceivedDevicesService _received;
    private readonly SyncRemovalService _removal;
    private readonly PastKeysService _pastKeys;

    public BackupRestoreService(
        DeviceBackupService backups, SyncIdentityService identity, ReceivedDevicesService received, SyncRemovalService removal, PastKeysService pastKeys)
    {
        _backups = backups;
        _identity = identity;
        _received = received;
        _removal = removal;
        _pastKeys = pastKeys;
    }

    /// <summary>The Drive backups and Drive's sign-in, for the restore step's list.</summary>
    public DeviceBackupService Backups => _backups;

    /// <summary>True when this PC is in a device group: only then can a backup be opened.</summary>
    public bool CanOpen => _identity.Key is not null;

    /// <summary>Opens a backup's bytes with this PC's group key, or the past key of the group its header names.</summary>
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

        // A sync file starts "ABS1" and the 4-byte id of the group it was sealed for.
        bool isSyncFile = backupBytes.Length >= 8 && backupBytes[0] == (byte)'A' && backupBytes[1] == (byte)'B' && backupBytes[2] == (byte)'S' && backupBytes[3] == (byte)'1';
        SyncKey openWith = key;
        if (isSyncFile && !backupBytes.AsSpan(4, 4).SequenceEqual(Convert.FromHexString(key.GroupId)))
        {
            // Another group id: a past key of this group may open it (ADR 56); if none matches, it is a different group.
            SyncKey? past = _pastKeys.KeyFor(Convert.ToHexStringLower(backupBytes.AsSpan(4, 4)));
            if (past is null)
            {
                return new BackupOpening(BackupOpeningKind.DifferentGroup, null, DifferentGroupMessage);
            }

            openWith = past;
        }

        SyncFile? file = SyncFileCodec.Open(backupBytes, openWith);
        if (file is null)
        {
            return new BackupOpening(BackupOpeningKind.Unreadable, null, "That backup can't be read. It may be cut off, changed, or from a newer version.");
        }

        return file.Device.DeviceId == _identity.DeviceId
            ? new BackupOpening(BackupOpeningKind.ThisPc, null, "That backup is this PC's own data.")
            : new BackupOpening(BackupOpeningKind.Ready, file, null);
    }

    /// <summary>
    /// Brings the backup's device in as a received device and marks it removed (shown as "[name] (removed)"). A device that is
    /// linked and sending here already with newer data (it was removed and added again) is left as it is.
    /// </summary>
    /// <param name="opening">An opening that <see cref="BackupOpening.IsReady"/>.</param>
    /// <returns>Null when restored; otherwise why not.</returns>
    public async Task<string?> RestoreAsync(BackupOpening opening)
    {
        if (opening.File is not SyncFile file)
        {
            return opening.Message;
        }

        Guid id = file.Device.DeviceId;
        ReceivedDevice? held = _received.Devices.FirstOrDefault(device => device.File.Device.DeviceId == id);
        if (held is not null && !_removal.IsRemoved(id) && held.File.Device.WrittenAt >= file.Device.WrittenAt)
        {
            return $"{file.Device.Name} is linked to your group again and this PC already has newer data from it.";
        }

        try
        {
            await _received.AcceptAsync(file with { PastKeys = null }, SyncDeviceCard.MethodFile, []);
            // A device that is linked in the group is not marked removed (it was added again after the backup's removal).
            if (held is null || _removal.IsRemoved(id))
            {
                await _removal.RecordRestoredAsync(id, file.Device.Name, file.Device.WrittenAt);
            }

            return null;
        }
        catch (SqliteException)
        {
            return "Couldn't save the restored data right now. Try again.";
        }
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
