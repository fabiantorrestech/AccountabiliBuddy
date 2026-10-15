namespace AccountabiliBuddy.Core.Domain;

/// <summary>A user-provided judgement of an activity. Future training data for the local classifier.</summary>
public sealed record ActivityLabel(
    DateTimeOffset Timestamp,
    Guid ProfileId,
    string ProcessName,
    string WindowTitle,
    string? Url,
    string? OcrText,
    RuleVerdict Verdict,
    LabelSource Source);
