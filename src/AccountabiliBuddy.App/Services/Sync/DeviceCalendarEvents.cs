using System.Text.Json;
using System.Text.Json.Nodes;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The calendar events the user's other devices get (the visible calendars, yesterday to two weeks ahead), each with its
/// calendar's colour, in the sync file (ADR 26). An event that carries a day plan category through the classifications and
/// rules in force (ADR 58; never a pending change) also gets its <c>categoryId</c>, its <c>knownSince</c> (when this
/// computer first saw it at its current time and category, by the trusted clock; left out while not stamped) and its
/// <c>seriesId</c> and <c>occurrenceKey</c> (when it has them), so the phone can find the same event in its own calendar and
/// follow the category. Every other event is sent as it always was. Rules, keywords and rule names never go.
/// </summary>
public static class DeviceCalendarEvents
{
    private const int DaysAhead = 14;

    /// <param name="plan">The stored day plan (the one the sync file carries), whose categories the phone knows.</param>
    /// <param name="overlay">The classifications, rules and first-known stamps kept on this computer.</param>
    public static List<JsonObject> Build(CalendarService calendars, DateOnly today, DayPlan plan, CalendarOverlayStore overlay)
    {
        var entries = new List<CalendarEntry>();
        var seen = new HashSet<(string, string)>();
        for (int day = -1; day <= DaysAhead; day++)
        {
            foreach (CalendarEntry entry in calendars.EventsOn(today.AddDays(day)))
            {
                if (seen.Add((entry.Event.CalendarId, entry.Event.Id)))
                {
                    entries.Add(entry);
                }
            }
        }

        // Read once: each is an immutable snapshot, so a change while this runs can't mix two states.
        IReadOnlyList<EventClassification> classifications = overlay.Classifications;
        CalendarRuleSet rules = overlay.Rules.Active;
        Dictionary<(string, string), DayCategory> categories = EventCategoryResolver
            .Classified(entries.Select(entry => entry.Event), plan, classifications, rules)
            .ToDictionary(classified => (classified.Event.CalendarId, classified.Event.Id), classified => classified.Category);
        Dictionary<(string, string), FirstKnownEntry> firstKnown = FirstKnownTimes.ByEvent(overlay.FirstKnown);

        var events = new List<JsonObject>();
        foreach (CalendarEntry entry in entries)
        {
            if (categories.TryGetValue((entry.Event.CalendarId, entry.Event.Id), out DayCategory? category))
            {
                events.Add(Classified(entry, category, firstKnown));
            }
            else
            {
                // The series and occurrence keys stay on this computer unless the event is classified (ADR 58).
                CalendarEvent shared = entry.Event with { SeriesId = null, OccurrenceKey = null };
                JsonObject json = JsonSerializer.SerializeToNode(shared, SyncJson.Options)!.AsObject();
                json["color"] = entry.Calendar.Color;
                events.Add(json);
            }
        }

        return events;
    }

    // The record writes seriesId and occurrenceKey itself when they are set (and leaves them out when they are null).
    private static JsonObject Classified(CalendarEntry entry, DayCategory category, Dictionary<(string, string), FirstKnownEntry> firstKnown)
    {
        JsonObject json = JsonSerializer.SerializeToNode(entry.Event, SyncJson.Options)!.AsObject();
        json["color"] = entry.Calendar.Color;
        json["categoryId"] = JsonSerializer.SerializeToNode(category.Id, SyncJson.Options);
        if (FirstKnownTimes.KnownAt(firstKnown, entry.Event, category) is DateTimeOffset knownSince)
        {
            json["knownSince"] = JsonSerializer.SerializeToNode(knownSince, SyncJson.Options);
        }

        return json;
    }
}
