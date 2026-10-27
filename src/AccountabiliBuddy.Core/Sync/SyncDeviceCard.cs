namespace AccountabiliBuddy.Core.Sync;

/// <summary>Who wrote a sync file, when, and the newest data it holds from the other devices (for the Devices tab).</summary>
/// <param name="Name">The device's own name, e.g. the PC's machine name. Never part of a file name (ADR 22).</param>
/// <param name="Kind"><see cref="KindPc"/>, <see cref="KindPhone"/> or <see cref="KindWatch"/>.</param>
/// <param name="WrittenAt">When the device wrote the file; a running session's end is taken as this time.</param>
/// <param name="Methods">The methods this device uses: <see cref="MethodLink"/> (old files only), <see cref="MethodFolder"/>, <see cref="MethodQr"/>, <see cref="MethodFile"/>, <see cref="MethodDrive"/>.</param>
/// <param name="HistoryOff">
/// Activity history is off on the device: the file carries no sessions or drift, and receivers delete the ones they
/// have from it (ADR 15).
/// </param>
/// <param name="Seen">For each other device (by id), the newest <see cref="WrittenAt"/> this device holds from it.</param>
/// <param name="Companions">
/// The devices linked to this one (the phone's watch, Task 18); null for none. Optional in the JSON: written only when
/// set, so a format 1 file without it reads as before, and older apps ignore it.
/// </param>
/// <param name="NextAlarm">
/// A phone's next alarm, which the user's PCs can take as the wake time (ADR 23); null for none. Optional in the JSON,
/// like <paramref name="Companions"/>.
/// </param>
public sealed record SyncDeviceCard(
    Guid DeviceId,
    string Name,
    string Kind,
    string AppVersion,
    DateTimeOffset WrittenAt,
    IReadOnlyList<string> Methods,
    bool HistoryOff,
    IReadOnlyDictionary<Guid, DateTimeOffset> Seen,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SyncCompanion>? Companions = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? NextAlarm = null)
{
    public const string KindPc = "pc";
    public const string KindPhone = "phone";
    public const string KindWatch = "watch";

    /// <summary>The network link (ADR 14), deleted in ADR 45; old files still name it and are read.</summary>
    public const string MethodLink = "link";

    /// <summary>A sync folder the user picks, e.g. inside Google Drive for desktop.</summary>
    public const string MethodFolder = "folder";

    /// <summary>QR codes the PC shows and the phone scans (or a picture of the phone's codes).</summary>
    public const string MethodQr = "qr";

    /// <summary>A sync file the user shares or imports by hand.</summary>
    public const string MethodFile = "file";

    /// <summary>The app's own Google Drive folder (ADR 26).</summary>
    public const string MethodDrive = "drive";
}
