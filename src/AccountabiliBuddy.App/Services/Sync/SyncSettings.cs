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
/// <param name="DriveOn">This PC syncs through the app's own Google Drive folder (ADR 26).</param>
/// <param name="RelayOn">This PC syncs through the user's own relay at <paramref name="RelayUrl"/> (ADR 46).</param>
/// <param name="RelayUrl">The relay's https address as typed; null until one is entered.</param>
/// <param name="RelayPin">The relay certificate's SHA-256 fingerprint (base64url) to pin; null for a relay with a normal certificate.</param>
/// <param name="RelayAdmissionProtected">The relay's admission key (a secret that lets this PC start a new group on that relay), DPAPI-protected as base64; null when the relay has none (read it with <see cref="SyncSettingsService.ReadRelayAdmission"/>).</param>
/// <param name="HostRelayOn">This PC runs the relay program itself while the app runs (ADR 46); off by default.</param>
/// <param name="HostRelayPort">The port the hosted relay listens on.</param>
/// <param name="HostRelayPublic">The hosted relay's firewall rule also covers Public networks (coffee shops, someone else's Wi-Fi).</param>
public sealed record SyncSettings(
    bool FolderOn = false,
    string? FolderPath = null,
    SyncInterval Interval = SyncInterval.FifteenMinutes,
    bool QrOn = false,
    TimeOnly? ReminderAt = null,
    int SnoozeMinutes = 15,
    bool FileShareOn = false,
    bool DriveOn = false,
    bool RelayOn = false,
    string? RelayUrl = null,
    string? RelayPin = null,
    string? RelayAdmissionProtected = null,
    bool HostRelayOn = false,
    int HostRelayPort = 8743,
    bool HostRelayPublic = true)
{
    public static SyncSettings Default { get; } = new();
}
