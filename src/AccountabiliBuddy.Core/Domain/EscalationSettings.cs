namespace AccountabiliBuddy.Core.Domain;

/// <summary>How long continuous drift lasts before each escalation level kicks in.</summary>
public sealed record EscalationSettings(TimeSpan NudgeAfter, TimeSpan OverlayAfter, TimeSpan EnforceAfter)
{
    public static EscalationSettings Default { get; } = new(
        NudgeAfter: TimeSpan.FromSeconds(10),
        OverlayAfter: TimeSpan.FromSeconds(45),
        EnforceAfter: TimeSpan.FromSeconds(120));
}
