using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// What a stretch with the PC off, asleep or the app closed does to a running session (ADR 60). Time off is never focus, whatever
/// happens to the timer, and the cause never changes the rules: a real power cut and a held power button look the same.
/// </summary>
public static class SessionGapPolicy
{
    /// <summary>Under this it carries on without a notice.</summary>
    public static readonly TimeSpan ShortGap = TimeSpan.FromMinutes(2);

    /// <summary>From this on (or on a new local day, once past the short gap) the session closes when the PC went off.</summary>
    public static readonly TimeSpan LongGap = TimeSpan.FromHours(4);

    /// <param name="session">The session as it was saved, with its end not moved.</param>
    public static SessionGapOutcome Decide(SessionState session, DateTimeOffset lastAliveAt, DateTimeOffset now, TimeZoneInfo zone, PcOffSettings settings)
    {
        TimeSpan gap = now > lastAliveAt ? now - lastAliveAt : TimeSpan.Zero;
        if (gap >= LongGap || (gap >= ShortGap && LocalDate(lastAliveAt, zone) != LocalDate(now, zone)))
        {
            return SessionGapOutcome.ClosedLongGap;
        }

        if (settings.KeepOriginalEnd)
        {
            // A Pomodoro catches up through its phases; a step away or planning has no end running out.
            bool passed = session is { Mode: SessionMode.Manual, CountdownFrozen: false, IsPlanning: false } && session.PhaseEndsAt is DateTimeOffset end && now >= end;
            if (passed)
            {
                return SessionGapOutcome.EndPassed;
            }
        }

        if (gap < ShortGap)
        {
            return SessionGapOutcome.Quiet;
        }

        return settings.KeepOriginalEnd ? SessionGapOutcome.KeptEnd : SessionGapOutcome.Paused;
    }

    private static DateOnly LocalDate(DateTimeOffset time, TimeZoneInfo zone)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, zone).DateTime);
    }
}
