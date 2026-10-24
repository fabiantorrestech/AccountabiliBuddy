using System.Text.Json;
using System.Text.Json.Nodes;
using AccountabiliBuddy.Core.Link;

namespace AccountabiliBuddy.App.Services.Link;

/// <summary>
/// The calendar events the user's other devices get (the visible calendars, yesterday to two weeks ahead), each with its
/// calendar's colour: the same list for the link's settings message and the sync file (ADR 26), so the phone reads one
/// shape.
/// </summary>
public static class DeviceCalendarEvents
{
    private const int DaysAhead = 14;

    public static List<JsonObject> Build(CalendarService calendars, DateOnly today)
    {
        var events = new List<JsonObject>();
        var seen = new HashSet<(string, string)>();
        for (int day = -1; day <= DaysAhead; day++)
        {
            foreach (CalendarEntry entry in calendars.EventsOn(today.AddDays(day)))
            {
                if (seen.Add((entry.Event.CalendarId, entry.Event.Id)))
                {
                    JsonObject json = JsonSerializer.SerializeToNode(entry.Event, LinkFraming.JsonOptions)!.AsObject();
                    json["color"] = entry.Calendar.Color;
                    events.Add(json);
                }
            }
        }

        return events;
    }
}
