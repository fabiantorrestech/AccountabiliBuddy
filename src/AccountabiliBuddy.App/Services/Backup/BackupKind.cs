namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// What a backup holds (ADR 71), and the names the PC and the phone both write in a backup: synced settings (the same on every
/// device), this device's own settings, the task lists, or the PC's activity history.
/// </summary>
public enum BackupKind
{
    SyncedSettings,
    DeviceSettings,
    TaskLists,
    History,
}
