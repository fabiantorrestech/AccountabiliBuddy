using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// "Google Calendar" goal sources on this PC (round 2 R5.7, plan §3b 7): the time in calendar events of each day plan category, as ADR 58
/// already classifies them (a hand choice on the event or its series, else the calendar's rule), as local windows per category id.
/// Read-only from the events this PC already keeps (ADR 9); nothing is fetched here. These windows reach <see cref="Core.Goals.GoalCounting"/>
/// only: never the focus totals, Stats, the goal unlock or a guard (those read counted sessions through <see cref="DeepWorkService"/>).
/// All-day events and events longer than ADR 58's longest are left out (they would read as days of time).
/// </summary>
public static class GoalCalendarWindows
{
    /// <summary>The classified events' windows, grouped by their category's id (merged per category so an overlap counts once).</summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyList<LocalWindow>> From(
        IEnumerable<CalendarEvent> events, DayPlan plan, IReadOnlyList<EventClassification> classifications, CalendarRuleSet rules)
    {
        var byCategory = new Dictionary<Guid, List<LocalWindow>>();
        foreach ((CalendarEvent calendarEvent, DayCategory category) in EventCategoryResolver.Classified(events, plan, classifications, rules))
        {
            if (calendarEvent.IsAllDay || calendarEvent.LocalEnd <= calendarEvent.LocalStart
                || calendarEvent.LocalEnd - calendarEvent.LocalStart > EventCategoryResolver.LongestEvent)
            {
                continue;
            }

            if (!byCategory.TryGetValue(category.Id, out List<LocalWindow>? windows))
            {
                windows = [];
                byCategory[category.Id] = windows;
            }

            windows.Add(new LocalWindow(calendarEvent.LocalStart, calendarEvent.LocalEnd));
        }

        return byCategory.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<LocalWindow>)Merged(pair.Value));
    }

    // Overlapping or touching windows become one, oldest first.
    private static List<LocalWindow> Merged(List<LocalWindow> windows)
    {
        var merged = new List<LocalWindow>();
        foreach (LocalWindow window in windows.OrderBy(item => item.Start))
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
}
