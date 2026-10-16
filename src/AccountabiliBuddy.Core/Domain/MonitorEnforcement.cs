namespace AccountabiliBuddy.Core.Domain;

/// <summary>The enforcement decision for one monitor.</summary>
/// <param name="Scope">Set only when <paramref name="Classification"/> is Distracting.</param>
/// <param name="IsRepeatOffense">True when this block was already escalated earlier in the same focus phase,
/// so the response skipped straight to its earlier level.</param>
/// <param name="IsRecovering">True while the user's "Navigate away" window is running on this monitor.</param>
/// <param name="NeedsClassification">True when unknown activity outlasted the profile's grace period
/// and the user has not deferred the question.</param>
public sealed record MonitorEnforcement(
    ActivitySnapshot Snapshot,
    Classification Classification,
    EnforcementAction Action,
    BlockScope? Scope,
    Rule? MatchedRule,
    bool IsRepeatOffense,
    bool IsRecovering,
    bool NeedsClassification)
{
    public string MonitorId => Snapshot.MonitorId;

    /// <summary>Nothing to do: no focus phase, or nothing distracting.</summary>
    public static MonitorEnforcement Relaxed(ActivitySnapshot snapshot, Classification classification)
    {
        return new MonitorEnforcement(snapshot, classification, EnforcementAction.None, null, null, false, false, false);
    }
}
