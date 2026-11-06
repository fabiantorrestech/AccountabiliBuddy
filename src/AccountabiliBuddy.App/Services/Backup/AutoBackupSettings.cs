namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// Auto-backup on this PC (ADR 71), a device setting (document "backup.auto.settings"): one switch per kind, all on; task lists are
/// backed up on their own only while this PC doesn't share them (shared, they are in the synced settings). <see cref="Folder"/> null:
/// Documents\AccountabiliBuddy backups. <see cref="ToDrive"/>: also a sealed copy on Google Drive when Drive sync is on.
/// </summary>
public sealed record AutoBackupSettings(bool Synced = true, bool Device = true, bool Tasks = true, string? Folder = null, bool ToDrive = true)
{
    public static AutoBackupSettings Default { get; } = new();
}
