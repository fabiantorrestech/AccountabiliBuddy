using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Friction before ending a session early or quitting: the user must wait
/// <see cref="BypassSettings.WaitDuration"/> after asking, then type a reason.
/// </summary>
public sealed class BypassPolicy
{
    private readonly BypassSettings _settings;

    public BypassPolicy(BypassSettings settings)
    {
        _settings = settings;
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
