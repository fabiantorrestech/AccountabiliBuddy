namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// What is visible on one monitor at one moment.
/// <see cref="MonitorId"/> and <see cref="WindowId"/> are opaque platform identifiers.
/// </summary>
public sealed record ActivitySnapshot(
    DateTimeOffset Timestamp,
    string MonitorId,
    string WindowId,
    string ProcessName,
    string WindowTitle,
    string? Url = null,
    string? OcrText = null);
