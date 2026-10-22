namespace AccountabiliBuddy.Core.Domain;

/// <summary>A user-provided judgement of an activity. Future training data for the local classifier.</summary>
/// <param name="Reason">Why the user answered this way (stored encrypted, ADR 8); null when none was given.</param>
public sealed record ActivityLabel(
    DateTimeOffset Timestamp,
    Guid ProfileId,
    string ProcessName,
    string WindowTitle,
    string? Url,
    string? OcrText,
    RuleVerdict Verdict,
    LabelSource Source,
    LabelLifetime Lifetime = LabelLifetime.Permanent,
    string? Reason = null,
    bool KeepProtectedText = true);
