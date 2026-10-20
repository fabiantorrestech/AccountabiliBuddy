namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The day bar at the top of the main window (and the on-screen timeline's rows). App-owned (not part of the shared
/// profile schema) and display only, so changing it never loosens anything.
/// </summary>
/// <param name="DayEnd">Not after <paramref name="DayStart"/> = the day ends on the next date (e.g. 06:00 to 02:00).</param>
/// <param name="Rows">The rows top to bottom and whether each shows; null = the default (see <see cref="RowsInOrder"/>).</param>
/// <param name="CalendarsOff">Ids of calendars left off the Calendar row; any other calendar shows, new ones too.</param>
/// <param name="ShowColdTurkey">Whether Cold Turkey's blocks show on the Blockers row.</param>
public sealed record DayBarSettings(
    bool Show,
    DayBarRange Range,
    TimeOnly DayStart,
    TimeOnly DayEnd,
    IReadOnlyList<TimelineRowChoice>? Rows = null,
    IReadOnlyList<string>? CalendarsOff = null,
    bool ShowColdTurkey = true)
{
    private static readonly TimelineRow[] DefaultOrder = { TimelineRow.Calendar, TimelineRow.Plan, TimelineRow.Focus, TimelineRow.Blockers };

    public static DayBarSettings Default { get; } = new(true, DayBarRange.FitToDay, new TimeOnly(6, 0), TimeOnly.MinValue);

    /// <summary>
    /// Every row exactly once: the saved order first (unknown and repeated rows dropped), then any row missing from
    /// it, shown, in the default order (Calendar, Plan, Focus, Blockers).
    /// </summary>
    public IReadOnlyList<TimelineRowChoice> RowsInOrder()
    {
        var rows = new List<TimelineRowChoice>();
        foreach (TimelineRowChoice choice in Rows ?? Array.Empty<TimelineRowChoice>())
        {
            if (Enum.IsDefined(choice.Row) && rows.All(row => row.Row != choice.Row))
            {
                rows.Add(choice);
            }
        }

        foreach (TimelineRow row in DefaultOrder.Where(row => rows.All(choice => choice.Row != row)))
        {
            rows.Add(new TimelineRowChoice(row, true));
        }

        return rows;
    }

    public bool ShowsCalendar(string calendarId)
    {
        return CalendarsOff?.Contains(calendarId) != true;
    }

    /// <summary>By value, lists included (row order matters, calendar order doesn't), so Settings can tell whether anything changed.</summary>
    public bool Equals(DayBarSettings? other)
    {
        return other is not null
               && Show == other.Show
               && Range == other.Range
               && DayStart == other.DayStart
               && DayEnd == other.DayEnd
               && ShowColdTurkey == other.ShowColdTurkey
               && RowsInOrder().SequenceEqual(other.RowsInOrder())
               && new HashSet<string>(CalendarsOff ?? Array.Empty<string>()).SetEquals(other.CalendarsOff ?? Array.Empty<string>());
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Show, Range, DayStart, DayEnd, ShowColdTurkey);
    }
}
