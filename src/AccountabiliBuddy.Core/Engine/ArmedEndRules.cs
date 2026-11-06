using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// The rules for armed ends (ADR 66). An end can be armed only while its wait still runs (after it, the caller ends at once as
/// before), its wait always counts from when the window opened, and it applies once, at its ready time: there is no later use.
/// </summary>
public static class ArmedEndRules
{
    /// <summary>
    /// The armed end for a wait of <paramref name="wait"/> asked at <paramref name="requestedAt"/>; null when the wait is
    /// already over at <paramref name="now"/> (end at once instead) or <paramref name="requestedAt"/> is in the future.
    /// </summary>
    public static ArmedEnd? Arm(
        ArmedEndKind kind, DateTimeOffset requestedAt, TimeSpan wait, DateTimeOffset now, string sealedReason,
        Guid? profileId = null, ArmedWorkEnd? workEnd = null)
    {
        DateTimeOffset readyAt = requestedAt + (wait < TimeSpan.Zero ? TimeSpan.Zero : wait);
        if (requestedAt > now || readyAt <= now)
        {
            return null;
        }

        return new ArmedEnd(Guid.NewGuid(), kind, requestedAt, readyAt, sealedReason, profileId, workEnd);
    }

    public static bool IsDue(ArmedEnd end, DateTimeOffset now) => now >= end.ReadyAt;

    /// <summary>The wait left, never below zero.</summary>
    public static TimeSpan Left(ArmedEnd end, DateTimeOffset now) => end.ReadyAt > now ? end.ReadyAt - now : TimeSpan.Zero;

    /// <summary>The one that applies first; null for none.</summary>
    public static ArmedEnd? Next(IEnumerable<ArmedEnd> ends) => ends.OrderBy(end => end.ReadyAt).FirstOrDefault();

    /// <summary>"0:42" under an hour, else "1:05:00": a countdown for a line.</summary>
    public static string Countdown(TimeSpan left)
    {
        int seconds = (int)Math.Ceiling(Math.Max(0, left.TotalSeconds));
        return seconds >= 3600
            ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";
    }
}
