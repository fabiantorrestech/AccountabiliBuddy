using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The quiet lines on Today for what calendar events changed in the day's guarded windows (ADR 58): one line per category that
/// has event-made parts today ("Comms from calendar: 2:00-3:00 PM") and, on a Budget day with a calendar credit, "Work from
/// calendar counted: 45 min". They name the category and the times and never an event's title. Read from the effective plan
/// against the stored one, so nothing here depends on how the events were chosen.
/// </summary>
public static class CalendarTodayText
{
    private const int MostTimesPerLine = 3;
    private const int MostLines = 3;

    /// <param name="effective">The plan the guards read (events laid over the stored plan).</param>
    /// <param name="stored">The stored plan, to tell a credit the calendar gave from the stored one (always none).</param>
    public static IReadOnlyList<string> Lines(
        DayPlan effective, DayPlan stored, DateOnly today, DateTime now, Func<DateOnly, TimeSpan>? focusedOn, Func<DateOnly, SleepTimes?>? nightFallback)
    {
        DateTime dayStart = today.ToDateTime(TimeOnly.MinValue);
        var day = new LocalWindow(dayStart, dayStart.AddDays(1));
        var lines = new List<string>();

        IEnumerable<IGrouping<Guid, PlannedPart>> byCategory = DayWorkWindows.PartsIn(effective, day, now, focusedOn, nightFallback)
            .Where(part => part.Part.FromEvent)
            .GroupBy(part => part.Category.Id);
        foreach (IGrouping<Guid, PlannedPart> group in byCategory)
        {
            List<LocalWindow> windows = Merged(group.Select(part => part.Window));
            lines.Add($"{group.First().Category.Name} from calendar: {Times(windows)}");
        }

        TimeSpan credit = effective.TemplateFor(today).BudgetCredit - stored.TemplateFor(today).BudgetCredit;
        if (credit >= TimeSpan.FromMinutes(1))
        {
            string work = effective.Categories.FirstOrDefault(category => category.Behavior == CategoryBehavior.Watch)?.Name ?? "Work";
            lines.Add($"{work} from calendar counted: {Minutes(credit)}");
        }

        if (lines.Count > MostLines)
        {
            int more = lines.Count - (MostLines - 1);
            lines = [.. lines.Take(MostLines - 1), $"+{more} more from the calendar"];
        }

        return lines;
    }

    // Pieces that touch (an event cut by a stricter one) read as one time.
    private static List<LocalWindow> Merged(IEnumerable<LocalWindow> windows)
    {
        var merged = new List<LocalWindow>();
        foreach (LocalWindow window in windows.OrderBy(window => window.Start))
        {
            if (merged.Count > 0 && window.Start <= merged[^1].End)
            {
                LocalWindow last = merged[^1];
                merged[^1] = new LocalWindow(last.Start, window.End > last.End ? window.End : last.End);
            }
            else
            {
                merged.Add(window);
            }
        }

        return merged;
    }

    private static string Times(List<LocalWindow> windows)
    {
        string shown = string.Join(", ", windows.Take(MostTimesPerLine).Select(window => ClockRangeText.Of(window.Start, window.End)));
        return windows.Count > MostTimesPerLine ? $"{shown} +{windows.Count - MostTimesPerLine} more" : shown;
    }

    private static string Minutes(TimeSpan span)
    {
        int minutes = (int)Math.Floor(span.TotalMinutes);
        int hours = minutes / 60;
        int rest = minutes % 60;
        return hours == 0 ? $"{rest} min" : rest == 0 ? $"{hours} h" : $"{hours} h {rest} min";
    }
}
