namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// What is visible on one monitor at one moment.
/// <see cref="MonitorId"/> and <see cref="WindowId"/> are opaque platform identifiers.
/// An empty <see cref="WindowId"/> means no window is visible (only the desktop).
/// </summary>
public sealed record ActivitySnapshot(
    DateTimeOffset Timestamp,
    string MonitorId,
    string WindowId,
    string ProcessName,
    string WindowTitle,
    string? Url = null,
    string? OcrText = null)
{
    public bool IsEmptyDesktop => WindowId.Length == 0;

    public static ActivitySnapshot ForEmptyDesktop(DateTimeOffset timestamp, string monitorId)
    {
        return new ActivitySnapshot(timestamp, monitorId, string.Empty, string.Empty, string.Empty);
    }
}
