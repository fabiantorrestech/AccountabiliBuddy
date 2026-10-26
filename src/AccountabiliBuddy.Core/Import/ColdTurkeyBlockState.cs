namespace AccountabiliBuddy.Core.Import;

/// <summary>
/// One Cold Turkey block as its settings say right now: switched on or off and, for a scheduled block, the weekly
/// times it applies (local time, as Cold Turkey keeps them). Its lock (ADR 39): a timed lock until
/// <see cref="LockedUntil"/>, a password lock (<see cref="PasswordLocked"/>), or another kind the user set in Cold
/// Turkey (<see cref="OtherLock"/>, e.g. random text or restart; the app can't hold such a block).
/// </summary>
/// <param name="UnblocksWhenLockEnds">Cold Turkey switches the block off when its lock ends ("lockUnblock").</param>
public sealed record ColdTurkeyBlockState(
    string Name,
    bool IsSwitchedOn,
    bool IsScheduled,
    IReadOnlyList<WeekRange> Schedule,
    DateTime? LockedUntil = null,
    bool PasswordLocked = false,
    string? OtherLock = null,
    bool UnblocksWhenLockEnds = true)
{
    /// <summary>True when the block blocks at <paramref name="localTime"/>: switched on and, when scheduled, inside its schedule.</summary>
    public bool IsOnAt(DateTime localTime)
    {
        if (!IsSwitchedOn)
        {
            return false;
        }

        if (!IsScheduled)
        {
            return true;
        }

        int minute = ((int)localTime.DayOfWeek * WeekRange.MinutesPerDay) + (localTime.Hour * 60) + localTime.Minute;
        return Schedule.Any(range => range.Start <= minute && minute < range.End);
    }

    /// <summary>True when a lock holds the block at <paramref name="localTime"/>.</summary>
    public bool IsLockedAt(DateTime localTime) => PasswordLocked || OtherLock is not null || LockedUntil > localTime;
}
