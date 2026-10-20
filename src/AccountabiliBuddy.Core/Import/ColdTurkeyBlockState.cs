namespace AccountabiliBuddy.Core.Import;

/// <summary>
/// One Cold Turkey block as its settings say right now: switched on or off and, for a scheduled block, the weekly
/// times it applies (local time, as Cold Turkey keeps them).
/// </summary>
public sealed record ColdTurkeyBlockState(string Name, bool IsSwitchedOn, bool IsScheduled, IReadOnlyList<WeekRange> Schedule)
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
}
