using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Friction before ending a session early or quitting: the user must wait
/// <see cref="BypassSettings.WaitDuration"/> after asking, then type a reason.
/// Ending a scheduled block early waits at least as long as the profile's shortest unlock option
/// (<see cref="ForSession"/>), so ending the block is never a cheaper way out than unlocking.
/// </summary>
public sealed class BypassPolicy
{
    private readonly BypassSettings _settings;

    public BypassPolicy(BypassSettings settings)
    {
        _settings = settings;
    }

    /// <summary>The friction that applies to ending <paramref name="state"/> early.</summary>
    /// <param name="profile">The session's profile, or null if it no longer exists.</param>
    public static BypassSettings ForSession(BypassSettings settings, SessionState state, FocusProfile? profile)
    {
        if (state.Mode != SessionMode.Scheduled || profile is null || profile.UnlockOptions.Count == 0)
        {
            return settings;
        }

        TimeSpan shortestUnlock = profile.UnlockOptions.Min(option => option.Wait);
        return shortestUnlock > settings.WaitDuration ? settings with { WaitDuration = shortestUnlock } : settings;
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
