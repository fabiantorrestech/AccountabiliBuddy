using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Which stored event choices (ADR 58) can be forgotten without changing any guard. Only a "this one" choice is ever stale,
/// and only when its event is found in the calendar and ended long ago, far past the 8 days the overlay looks back
/// (<see cref="CalendarDayOverlay"/>), so no guard can read it any more. A choice for an event the calendar doesn't hold is
/// kept: the calendar may not be loaded yet, or may give the event back, and a dropped choice there could loosen a guard. A
/// series choice is kept too; it goes only with its calendar (<c>EffectiveDayPlanProvider.TryForgetCalendars</c>).
/// </summary>
public static class ClassificationPruning
{
    /// <summary>How long after an event ended its "this one" choice is kept.</summary>
    public static readonly TimeSpan KeepAfterEnd = TimeSpan.FromDays(30);

    /// <param name="choices">Stored or waiting choices.</param>
    /// <param name="eventsOf">The kept events of a calendar (empty when it has none).</param>
    /// <param name="localNow">Now, in the time zone event times are in.</param>
    /// <returns>The choices that can go, in the order given.</returns>
    public static IReadOnlyList<EventClassification> Stale(
        IEnumerable<EventClassification> choices, Func<string, IReadOnlyList<CalendarEvent>> eventsOf, DateTime localNow)
    {
        DateTime cutoff = localNow - KeepAfterEnd;
        var latestEndByCalendar = new Dictionary<string, Dictionary<string, DateTime>>();
        var stale = new List<EventClassification>();
        foreach (EventClassification choice in choices.Where(choice => choice.Scope == ClassificationScope.ThisEvent))
        {
            if (!latestEndByCalendar.TryGetValue(choice.CalendarId, out Dictionary<string, DateTime>? latestEnd))
            {
                latestEnd = LatestEndByKey(eventsOf(choice.CalendarId));
                latestEndByCalendar[choice.CalendarId] = latestEnd;
            }

            if (latestEnd.TryGetValue(choice.Key, out DateTime end) && end < cutoff)
            {
                stale.Add(choice);
            }
        }

        return stale;
    }

    // An event is found by its classification key; if two events share a key, the later end decides.
    private static Dictionary<string, DateTime> LatestEndByKey(IReadOnlyList<CalendarEvent> events)
    {
        var latest = new Dictionary<string, DateTime>();
        foreach (CalendarEvent calendarEvent in events)
        {
            string key = calendarEvent.ClassificationKey;
            if (!latest.TryGetValue(key, out DateTime end) || calendarEvent.LocalEnd > end)
            {
                latest[key] = calendarEvent.LocalEnd;
            }
        }

        return latest;
    }
}
