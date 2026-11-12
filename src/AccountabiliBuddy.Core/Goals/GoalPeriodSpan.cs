namespace AccountabiliBuddy.Core.Goals;

/// <summary>One period of a goal: <see cref="Start"/> to <see cref="End"/>, both days included.</summary>
public readonly record struct GoalPeriodSpan(DateOnly Start, DateOnly End)
{
    /// <summary>How many days it has.</summary>
    public int Length => End.DayNumber - Start.DayNumber + 1;

    public bool Contains(DateOnly day)
    {
        return day >= Start && day <= End;
    }

    /// <summary>Its days, first to last.</summary>
    public IEnumerable<DateOnly> Days()
    {
        for (DateOnly day = Start; day <= End; day = day.AddDays(1))
        {
            yield return day;
        }
    }
}
