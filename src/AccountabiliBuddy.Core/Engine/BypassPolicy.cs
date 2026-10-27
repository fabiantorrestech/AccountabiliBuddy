using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Friction before ending a session early or quitting: the user must wait
/// <see cref="BypassSettings.WaitDuration"/> after asking, then type a reason. Only a session that counts down to a
/// set end has it: a scheduled block and a session with no set end end on command (<see cref="EndsOnCommand"/>).
/// </summary>
public sealed class BypassPolicy
{
    private readonly BypassSettings _settings;

    public BypassPolicy(BypassSettings settings)
    {
        _settings = settings;
    }

    /// <summary>The friction that applies to ending <paramref name="state"/> early.</summary>
    public static BypassSettings ForSession(BypassSettings settings, SessionState state)
    {
        return EndsOnCommand(state) ? BypassSettings.None : settings;
    }

    /// <summary>
    /// True for a scheduled block (blockers like Cold Turkey are the guardrails; AccountabiliBuddy doesn't hold the user
    /// in it), for a session with no set end (count-up Quick Start, "No set end") and for a test session: nothing there
    /// ends "early".
    /// </summary>
    public static bool EndsOnCommand(SessionState state)
    {
        return state.IsTest || state.Mode == SessionMode.Scheduled || (state.Mode == SessionMode.Manual && state.PhaseEndsAt is null);
    }

    public TimeSpan RemainingWait(DateTimeOffset requestedAt, DateTimeOffset now)
    {
        TimeSpan remaining = requestedAt + _settings.WaitDuration - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public BypassCheckResult Check(DateTimeOffset requestedAt, DateTimeOffset now, string? reason)
    {
        if (RemainingWait(requestedAt, now) > TimeSpan.Zero)
        {
            return BypassCheckResult.WaitNotElapsed;
        }

        int reasonLength = reason?.Trim().Length ?? 0;
        if (reasonLength < _settings.MinReasonLength)
        {
            return BypassCheckResult.ReasonTooShort;
        }

        return BypassCheckResult.Allowed;
    }
}
