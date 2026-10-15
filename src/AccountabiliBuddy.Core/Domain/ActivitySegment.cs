namespace AccountabiliBuddy.Core.Domain;

/// <summary>A continuous stretch of the same activity on one monitor, for the local usage log.</summary>
public sealed record ActivitySegment(
    DateTimeOffset Start,
    DateTimeOffset End,
    string MonitorId,
    string ProcessName,
    string WindowTitle,
    string? Url,
    Classification Classification,
    Guid? ProfileId,
    bool InFocusSession);
