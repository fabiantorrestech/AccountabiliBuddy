using System.Globalization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The goal heatmap's grid (change note §4a): a column per week, a row per weekday, weeks starting on the user's week start
/// (<see cref="GoalWeekStart"/>), the newest week on the right. The range ends on <see cref="End"/> (today, or the last day of a
/// page paged back to) and starts its months (or a year) before that, the day after; the first column starts on that day's week
/// start, so the first column's days before <see cref="RangeStart"/> and the last column's days after <see cref="End"/> are in the
/// grid but not shown. Month labels are centred over each month's shown columns; every month after the first shown one gets a
/// staircase divider.
/// </summary>
/// <param name="First">The first day of the first column (a week start).</param>
/// <param name="RangeStart">The first shown day.</param>
/// <param name="End">The last shown day.</param>
/// <param name="Columns">Weeks across, the one holding <see cref="End"/> last.</param>
public sealed record GoalHeatmapGrid(
    GoalHeatmapRange Range,
    DateOnly First,
    DateOnly RangeStart,
    DateOnly End,
    int Columns,
    GoalWeekStart WeekStart,
    IReadOnlyList<GoalHeatmapMonth> Months)
{
    public const int Rows = 7;

    /// <summary>The grid of <paramref name="range"/> ending on <paramref name="end"/>.</summary>
    public static GoalHeatmapGrid For(GoalHeatmapRange range, DateOnly end, GoalWeekStart weekStart)
    {
        DateOnly rangeStart = RangeStartOf(range, end);
        DateOnly first = GoalPeriods.WeekStartOf(rangeStart, weekStart);
        return Make(range, first, rangeStart, end, weekStart);
    }

    /// <summary>The first day of <paramref name="range"/> ending on <paramref name="end"/>: its months (or a year) back, the day after.</summary>
    public static DateOnly RangeStartOf(GoalHeatmapRange range, DateOnly end)
    {
        return range == GoalHeatmapRange.Year ? end.AddYears(-1).AddDays(1) : end.AddMonths(-MonthsOf(range)).AddDays(1);
    }

    /// <summary>How many months a range spans (12 for a year).</summary>
    public static int MonthsOf(GoalHeatmapRange range)
    {
        return range switch
        {
            GoalHeatmapRange.OneMonth => 1,
            GoalHeatmapRange.ThreeMonths => 3,
            GoalHeatmapRange.SixMonths => 6,
            _ => 12,
        };
    }

    /// <summary>
    /// The last day of the page before (<paramref name="direction"/> below 0: the day before this page's first day) or after (above 0:
    /// the range's span later, never past <paramref name="today"/>) a page of <paramref name="range"/> ending on <paramref name="end"/>
    /// (‹ and ›, change note §4a).
    /// </summary>
    public static DateOnly Step(GoalHeatmapRange range, DateOnly end, int direction, DateOnly today)
    {
        if (direction < 0)
        {
            return RangeStartOf(range, end).AddDays(-1);
        }

        DateOnly next = range == GoalHeatmapRange.Year ? end.AddYears(1) : end.AddMonths(MonthsOf(range));
        return next > today ? today : next;
    }

    /// <summary>The row of <paramref name="day"/> (0 = the week start).</summary>
    public int RowOf(DateOnly day)
    {
        return Mod(day.DayNumber - First.DayNumber, 7);
    }

    /// <summary>The column of <paramref name="day"/> (negative before the grid).</summary>
    public int ColumnOf(DateOnly day)
    {
        return (int)Math.Floor((day.DayNumber - First.DayNumber) / 7.0);
    }

    /// <summary>The day in a cell.</summary>
    public DateOnly DayAt(int row, int column)
    {
        return First.AddDays((column * 7) + row);
    }

    /// <summary>Whether <paramref name="day"/> is shown (inside the range).</summary>
    public bool Shows(DateOnly day)
    {
        return day >= RangeStart && day <= End;
    }

    /// <summary>The same grid with its <paramref name="columns"/> oldest weeks cropped from the left (the newest week always stays).</summary>
    public GoalHeatmapGrid CropOldest(int columns)
    {
        int crop = Math.Clamp(columns, 0, Columns - 1);
        if (crop == 0)
        {
            return this;
        }

        DateOnly first = First.AddDays(7 * crop);
        DateOnly rangeStart = RangeStart > first ? RangeStart : first;
        return Make(Range, first, rangeStart, End, WeekStart);
    }

    private static GoalHeatmapGrid Make(GoalHeatmapRange range, DateOnly first, DateOnly rangeStart, DateOnly end, GoalWeekStart weekStart)
    {
        int columns = (int)Math.Floor((end.DayNumber - first.DayNumber) / 7.0) + 1;
        var grid = new GoalHeatmapGrid(range, first, rangeStart, end, Math.Max(1, columns), weekStart, []);
        return grid with { Months = grid.MonthsShown() };
    }

    private List<GoalHeatmapMonth> MonthsShown()
    {
        var months = new List<GoalHeatmapMonth>();
        var month = new DateOnly(RangeStart.Year, RangeStart.Month, 1);
        while (month <= End)
        {
            DateOnly firstShown = month < RangeStart ? RangeStart : month;
            DateOnly monthEnd = month.AddMonths(1).AddDays(-1);
            DateOnly lastShown = monthEnd > End ? End : monthEnd;
            string label = month.ToString("MMM", CultureInfo.InvariantCulture);
            bool divider = firstShown.Day == 1 && months.Count > 0;
            months.Add(new GoalHeatmapMonth(month.Year, month.Month, label, ColumnOf(firstShown), ColumnOf(lastShown), RowOf(firstShown), divider));
            month = month.AddMonths(1);
        }

        return months;
    }

    private static int Mod(int value, int by)
    {
        return ((value % by) + by) % by;
    }
}
