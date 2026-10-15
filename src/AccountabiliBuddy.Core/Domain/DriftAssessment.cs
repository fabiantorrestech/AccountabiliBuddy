namespace AccountabiliBuddy.Core.Domain;

/// <param name="Duration">How long the monitor has continuously had this classification.</param>
/// <param name="NeedsClassification">True once Unknown activity outlasts the profile's grace period.</param>
public sealed record DriftAssessment(
    string MonitorId,
    Classification Classification,
    TimeSpan Duration,
    EscalationLevel Level,
    bool NeedsClassification,
    Rule? MatchedRule);
