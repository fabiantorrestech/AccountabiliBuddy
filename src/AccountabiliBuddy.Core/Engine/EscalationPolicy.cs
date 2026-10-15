using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>Maps how long the user has been drifting to an escalation level.</summary>
public sealed class EscalationPolicy
{
    private readonly EscalationSettings _settings;

    public EscalationPolicy(EscalationSettings settings)
    {
        if (settings.NudgeAfter < TimeSpan.Zero
            || settings.OverlayAfter < settings.NudgeAfter
            || settings.EnforceAfter < settings.OverlayAfter)
        {
            throw new ArgumentException(
                "Escalation thresholds must be non-negative and in order: nudge <= overlay <= enforce.",
                nameof(settings));
        }

        _settings = settings;
    }

    public EscalationLevel Decide(TimeSpan driftDuration)
    {
        if (driftDuration >= _settings.EnforceAfter)
        {
            return EscalationLevel.Enforce;
        }

        if (driftDuration >= _settings.OverlayAfter)
        {
            return EscalationLevel.Overlay;
        }

        if (driftDuration >= _settings.NudgeAfter)
        {
            return EscalationLevel.Nudge;
        }

        return EscalationLevel.None;
    }
}
