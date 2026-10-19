namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// Extra blocked time on one date with a profile (schema v4), e.g. Saturday 09:00–12:00 exam prep. Runs like a
/// weekly block. If <see cref="End"/> is not after <see cref="Start"/>, it runs past midnight into the next day.
/// </summary>
public sealed record OneOffBlock(Guid Id, DateOnly Date, TimeOnly Start, TimeOnly End, Guid ProfileId)
{
    /// <summary>The local start (inclusive) and end (exclusive) of this block.</summary>
    public LocalWindow GetWindow()
    {
        DateTime start = Date.ToDateTime(Start);
        DateTime end = Date.ToDateTime(End);
        return new LocalWindow(start, end > start ? end : end.AddDays(1));
    }

    /// <summary>The same block in weekly form, so a running one-off block looks like any scheduled block.</summary>
    public ScheduleBlock ToScheduleBlock()
    {
        return new ScheduleBlock(Id, Date.DayOfWeek, Start, End, ProfileId);
    }
}
