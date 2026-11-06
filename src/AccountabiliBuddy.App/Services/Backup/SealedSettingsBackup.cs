using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// A settings backup sealed with the device group's key for Google Drive (ADR 71), which holds sealed files only: the backup's JSON as
/// the one document of a sync file (<see cref="SyncFileCodec"/>), so the PC and the phone open it with the code they already have.
/// </summary>
public static class SealedSettingsBackup
{
    public const string Extension = ".absync";

    private const string DocumentPrefix = "backup.";

    public static byte[] Seal(BackupKind kind, string json, Guid deviceId, string deviceName, string appVersion, DateTimeOffset now, SyncKey key)
    {
        var card = new SyncDeviceCard(deviceId, deviceName, SyncDeviceCard.KindPc, appVersion, now, [], false, new Dictionary<Guid, DateTimeOffset>());
        var file = new SyncFile(SyncFile.CurrentFormatVersion, card, [], [], null, [new SyncDocument(DocumentPrefix + BackupKinds.Name(kind), json, now)]);
        return SyncFileCodec.Seal(file, key);
    }

    /// <summary>The backup's JSON, or null when the key doesn't open it or it holds no backup.</summary>
    public static string? Open(ReadOnlySpan<byte> sealedBytes, SyncKey key) =>
        SyncFileCodec.Open(sealedBytes, key)?.Documents.FirstOrDefault(document => document.Key.StartsWith(DocumentPrefix, StringComparison.Ordinal))?.Json;
}
