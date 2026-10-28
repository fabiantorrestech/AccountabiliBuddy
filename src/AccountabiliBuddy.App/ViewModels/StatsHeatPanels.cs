using System.Globalization;
using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Builds the two heatmap views of the Stats page: Consistency (a cell per day, weeks across) and Intensity (a cell per
/// weekday and hour). Each returns null when its range has no focus, so the page says so instead of drawing a grid of zeros.
/// </summary>
internal static class StatsHeatPanels
{
    private const int IntensityHourStep = 3;

    public static StatsHeatPanel? Consistency(StatsData data, StatsLookups lookups, StatsRange range)
    {
        (DateOnly from, DateOnly to) = StatsFormat.Bounds(range, data.Today);
        IReadOnlyList<DeepWorkDay> days = data.DaysBetween(from, to);
        if (days.All(day => day.Focused <= TimeSpan.Zero))
        {
            return null;
        }

        // History keeps 90 days: the grid starts at the first week it has, so the cells fill the width instead of leaving a blank year.
        if (from < data.FirstDay)
        {
            from = data.FirstDay;
        }

        DateOnly gridStart = StatsFormat.MondayOf(from);
        int columns = (StatsFormat.MondayOf(to).DayNumber - gridStart.DayNumber) / 7 + 1;
        var cells = new List<HeatCell>();
        for (int offset = 0; offset < columns * 7; offset++)
        {
            DateOnly date = gridStart.AddDays(offset);
            DeepWorkDay? day = date >= from && date <= to && date <= data.Today ? data.DayOf(date) : null;
            string tip = day is null
                ? string.Empty
                : $"{date.ToString("ddd, MMM d", CultureInfo.CurrentCulture)} · {StatsFormat.Duration(day.Focused)}{(day.Focused >= data.Goal ? " · goal met" : string.Empty)}";
            cells.Add(new HeatCell(offset % 7, offset / 7, day is null ? -1 : ConsistencyStats.Level(day.Focused, data.Goal), tip, day is null ? null : date));
        }

        ConsistencySummary summary = ConsistencyStats.Summarize(days, data.Goal, lookups.IsWorkday, data.Today);
        var cards = new List<StatsCard>
        {
            new("Current streak", summary.CurrentStreak.ToString(CultureInfo.CurrentCulture),
                $"{(summary.CurrentStreak == 1 ? "workday" : "workdays")} in a row · best: {summary.BestStreak}"),
            new("Goal met", $"{summary.MetPercent}%", $"{summary.WorkdaysMet} of {summary.WorkdaysCounted} workdays"),
        };
        if (summary.MostReliableDay is { } reliable)
        {
            string name = StatsFormat.WeekdayName(reliable);
            cards.Add(new StatsCard("Most reliable", name, $"{summary.MostReliablePercent}% of {name}s"));
        }

        if (summary.HasOffDays)
        {
            cards.Add(new StatsCard("Off days", "skipped", "not counted against you"));
        }

        string note = "Rows are Mon–Sun. Brighter = more deep work that day.";
        if (range == StatsRange.Year)
        {
            note += $" History keeps {data.Days.Count} days.";
        }

        return new StatsHeatPanel(
            cells, 7, columns, range == StatsRange.Year ? null : WeekdayNames(), range == StatsRange.Year ? MonthLabels(gridStart, columns) : null, cards, note);
    }

    public static StatsHeatPanel? Intensity(StatsData data, StatsRange range)
    {
        DateOnly to = data.Today;
        DateOnly from = range switch
        {
            StatsRange.Week => to.AddDays(-6),
            StatsRange.Month => to.AddDays(-55),
            _ => to.AddDays(-364),
        };
        if (from < data.FirstDay)
        {
            from = data.FirstDay;
        }

        IReadOnlyList<DateTime> driftStarts = data.DriftBetween(from, to)
            .Select(visit => TimeZoneInfo.ConvertTime(visit.Start, data.Zone).DateTime).ToList();
        IntensityGrid grid = IntensityGrid.Build(data.Windows, from, to, driftStarts);
        if (grid.TotalMinutes <= 0)
        {
            return null;
        }

        string[] names = WeekdayNames();
        var cells = new List<HeatCell>();
        for (int row = 0; row < IntensityGrid.Rows; row++)
        {
            for (int hour = grid.FirstHour; hour <= grid.LastHour; hour++)
            {
                double minutes = grid.Minutes[row, hour];
                string tip = $"{names[row]} {StatsFormat.HourRange(hour, hour + 1)} · {(minutes <= 0 ? "no focus" : StatsFormat.Duration(TimeSpan.FromMinutes(Math.Round(minutes))) + " of focus")}";
                cells.Add(new HeatCell(row, hour - grid.FirstHour, grid.Level(row, hour), tip));
            }
        }

        var cards = new List<StatsCard>();
        if (grid.Peak is { } peak)
        {
            cards.Add(new StatsCard("Peak window", StatsFormat.HourRange(peak.StartHour, peak.EndHour), $"{peak.Share:P0} of deep work"));
        }

        if (grid.SecondWind is { } second)
        {
            cards.Add(new StatsCard("Second wind", StatsFormat.HourRange(second.StartHour, second.EndHour), $"{second.Share:P0} of deep work"));
        }

        if (grid.Weakest is { } weakest)
        {
            string detail = weakest.DriftCount > 0 ? $"least focus · drift ×{weakest.DriftCount}" : "least focus in your day";
            cards.Add(new StatsCard("Weakest", StatsFormat.HourRange(weakest.StartHour, weakest.EndHour), detail));
        }

        int weeks = (int)Math.Ceiling(((to.DayNumber - from.DayNumber) + 1) / 7.0);
        string note = $"Focus time added up over the last {weeks} {(weeks == 1 ? "week" : "weeks")}. Brighter = more.";
        string? helper = grid.Peak is { } best ? $"Your best hours are {StatsFormat.HourRange(best.StartHour, best.EndHour)}: keep deep work there." : null;
        var hourLabels = new List<string?>();
        for (int hour = grid.FirstHour; hour <= grid.LastHour; hour++)
        {
            hourLabels.Add(hour % IntensityHourStep == 0 ? StatsFormat.HourShort(hour) : null);
        }

        return new StatsHeatPanel(cells, IntensityGrid.Rows, grid.LastHour - grid.FirstHour + 1, names, hourLabels, cards, note, helper);
    }

    /// <summary>The number of weeks the Intensity view covers, for its subtitle.</summary>
    public static int IntensityWeeks(StatsData data, StatsRange range)
    {
        int days = range switch
        {
            StatsRange.Week => 7,
            StatsRange.Month => 56,
            _ => 365,
        };
        days = Math.Min(days, data.Today.DayNumber - data.FirstDay.DayNumber + 1);
        return (int)Math.Ceiling(days / 7.0);
    }

    private static string[] WeekdayNames()
    {
        return Enumerable.Range(0, 7)
            .Select(row => CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)((row + 1) % 7)))
            .ToArray();
    }

    // A month's name over the column its first day falls in (and over the first column).
    private static IReadOnlyList<string?> MonthLabels(DateOnly gridStart, int columns)
    {
        var labels = new List<string?>();
        for (int column = 0; column < columns; column++)
        {
            DateOnly monday = gridStart.AddDays(column * 7);
            DateOnly? first = Enumerable.Range(0, 7).Select(offset => monday.AddDays(offset)).Where(date => date.Day == 1)
                .Select(date => (DateOnly?)date).FirstOrDefault();
            labels.Add(first is { } month ? month.ToString("MMM", CultureInfo.CurrentCulture) : column == 0 ? monday.ToString("MMM", CultureInfo.CurrentCulture) : null);
        }

        return labels;
    }
}
