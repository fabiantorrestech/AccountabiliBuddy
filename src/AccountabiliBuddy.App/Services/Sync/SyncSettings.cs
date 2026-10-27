namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Which ways this PC shares its progress with the user's other devices (ADR 22). Nothing is written or sent by a
/// method until its switch is on. App-owned ("sync.settings").
/// </summary>
/// <param name="FolderOn">This PC writes and reads sync files in <paramref name="FolderPath"/>.</param>
/// <param name="FolderPath">The sync folder, e.g. inside Google Drive for desktop; null until one is picked.</param>
/// <param name="QrOn">The day's QR codes are offered for the phone to scan.</param>
/// <param name="ReminderAt">When the QR reminder comes; null = when the day plan's last Work part ends.</param>
/// <param name="SnoozeMinutes">How long "Later" puts the QR reminder off.</param>
/// <param name="FileShareOn">A sync file can be saved and opened by hand.</param>
public sealed record SyncSettings(
    bool FolderOn = false,
    string? FolderPath = null,
    SyncInterval Interval = SyncInterval.FifteenMinutes,
    bool QrOn = false,
    TimeOnly? ReminderAt = null,
    int SnoozeMinutes = 15,
    bool FileShareOn = false,
    bool DriveOn = false)
{
    public static SyncSettings Default { get; } = new();
}
