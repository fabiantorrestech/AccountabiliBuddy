namespace AccountabiliBuddy.Core.Domain;

/// <summary>A continuous stretch of the same activity on one monitor, for the local usage log.</summary>
/// <param name="Classification">Only meaningful while <paramref name="InFocusSession"/>; Unknown otherwise.</param>
/// <param name="OcrText">Screen text read for this activity, if screen reading ran (stored encrypted).</param>
/// <param name="Channel">The page's channel as the browser extension reported it (a YouTube video's); only the site's
/// section derived from it is stored (ADR 17).</param>
public sealed record ActivitySegment(
    DateTimeOffset Start,
    DateTimeOffset End,
    string MonitorId,
    string ProcessName,
    string WindowTitle,
    string? Url,
    Classification Classification,
    Guid? ProfileId,
    bool InFocusSession,
    string? OcrText = null,
    string? Channel = null)
{
    public TimeSpan Duration => End - Start;
}
