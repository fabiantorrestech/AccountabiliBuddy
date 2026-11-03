using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Keeps when this device first saw each classified calendar event at its current start, end, category and what that
/// category does (ADR 58), so a looser change can be held to the notice. A change to any of them starts the notice over.
/// An event is forgotten at the end of the day after it ended (so a noticed Budget credit and past pieces don't snap back
/// the moment it ends) or when it is no longer classified or gone from the calendar. Pure: the app passes the trusted
/// clock's time.
/// </summary>
public static class FirstKnownTimes
{
    /// <summary>
    /// The entries after seeing <paramref name="classified"/> at <paramref name="now"/>: kept as they were when the event is
    /// unchanged, new (stamped <paramref name="now"/>) when it is new or changed, dropped once the day after the event's end
    /// is over or when it is no longer classified. Returns <paramref name="existing"/> itself when nothing changed.
    /// </summary>
    public static IReadOnlyList<FirstKnownEntry> Update(
        IReadOnlyList<FirstKnownEntry> existing,
        IEnumerable<(CalendarEvent Event, DayCategory Category)> classified,
        DateTimeOffset now,
        DateTime localNow)
    {
        Dictionary<(string, string), FirstKnownEntry> known = ByEvent(existing);
        var result = new List<FirstKnownEntry>();
        var seen = new HashSet<(string, string)>();
        bool changed = false;
        foreach ((CalendarEvent calendarEvent, DayCategory category) in classified)
        {
            (string, string) key = (calendarEvent.CalendarId, calendarEvent.Id);
            if (calendarEvent.LocalEnd.Date.AddDays(2) <= localNow || !seen.Add(key))
            {
                continue;
            }

            if (known.TryGetValue(key, out FirstKnownEntry? entry) && Matches(entry, calendarEvent, category))
            {
                result.Add(entry);
            }
            else
            {
                result.Add(new FirstKnownEntry(
                    calendarEvent.CalendarId, calendarEvent.Id, calendarEvent.LocalStart, calendarEvent.LocalEnd, category.Id, category.Behavior, now));
                changed = true;
            }
        }

        changed |= result.Count != existing.Count;
        return changed ? result : existing;
    }

    /// <summary>
    /// When the event was first seen at its current start, end, <paramref name="category"/> and behaviour; null when it wasn't
    /// (or has changed since, so the notice starts over).
    /// </summary>
    public static DateTimeOffset? KnownAt(IReadOnlyDictionary<(string, string), FirstKnownEntry> known, CalendarEvent calendarEvent, DayCategory category)
    {
        return known.TryGetValue((calendarEvent.CalendarId, calendarEvent.Id), out FirstKnownEntry? entry) && Matches(entry, calendarEvent, category)
            ? entry.KnownAt
            : null;
    }

    /// <summary>The entries by (calendar id, event id).</summary>
    public static Dictionary<(string, string), FirstKnownEntry> ByEvent(IEnumerable<FirstKnownEntry> entries)
    {
        var byEvent = new Dictionary<(string, string), FirstKnownEntry>();
        foreach (FirstKnownEntry entry in entries)
        {
            byEvent[(entry.CalendarId, entry.EventId)] = entry;
        }

        return byEvent;
    }

    private static bool Matches(FirstKnownEntry entry, CalendarEvent calendarEvent, DayCategory category)
    {
        return entry.LocalStart == calendarEvent.LocalStart
               && entry.LocalEnd == calendarEvent.LocalEnd
               && entry.CategoryId == category.Id
               && entry.Behavior == category.Behavior;
    }
}
