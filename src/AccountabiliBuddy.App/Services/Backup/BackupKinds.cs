namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>The written names, file extensions and parts of each <see cref="BackupKind"/> (ADR 71), shared with the phone.</summary>
public static class BackupKinds
{
    public const string PlatformPc = "pc";
    public const string PlatformPhone = "phone";

    public static string Name(BackupKind kind) => kind switch
    {
        BackupKind.SyncedSettings => "synced",
        BackupKind.DeviceSettings => "device",
        BackupKind.TaskLists => "tasks",
        _ => "history",
    };

    public static BackupKind? Parse(string? name) => name switch
    {
        "synced" => BackupKind.SyncedSettings,
        "device" => BackupKind.DeviceSettings,
        "tasks" => BackupKind.TaskLists,
        "history" => BackupKind.History,
        _ => null,
    };

    public static string Extension(BackupKind kind) => kind switch
    {
        BackupKind.SyncedSettings => ".absettings",
        BackupKind.DeviceSettings => ".abdevice",
        BackupKind.TaskLists => ".abtasks",
        _ => ".abbackup",
    };

    /// <summary>"Synced settings", for folders and the cards.</summary>
    public static string Title(BackupKind kind) => kind switch
    {
        BackupKind.SyncedSettings => "Synced settings",
        BackupKind.DeviceSettings => "This PC's settings",
        BackupKind.TaskLists => "Task lists",
        _ => "History",
    };

    /// <summary>The parts a kind's backup holds; synced settings carry the task lists while they are shared.</summary>
    public static BackupParts Parts(BackupKind kind, bool shareTaskLists) => kind switch
    {
        BackupKind.SyncedSettings => BackupParts.Profiles | BackupParts.SyncedSettings | (shareTaskLists ? BackupParts.TaskLists : BackupParts.None),
        BackupKind.DeviceSettings => BackupParts.DeviceSettings | BackupParts.Calendars,
        BackupKind.TaskLists => BackupParts.TaskLists,
        _ => BackupParts.History,
    };
}
