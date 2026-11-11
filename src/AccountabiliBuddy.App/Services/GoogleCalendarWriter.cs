using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The one write path to Google Calendar (ADR 78), over <see cref="CalendarNetwork"/>: finds or makes the app's own calendar and
/// creates, changes and deletes task events by their fixed ids (<see cref="TaskCalendarMapping.EventId"/>). An event carries only
/// the task's title and times and its reminder overrides; it is marked free, so it never blocks the user's time.
/// </summary>
public sealed class GoogleCalendarWriter
{
    private readonly CalendarNetwork _network;
    private readonly GoogleEndpoints _endpoints;

    public GoogleCalendarWriter(CalendarNetwork network, GoogleEndpoints endpoints)
    {
        _network = network;
        _endpoints = endpoints;
    }

    /// <returns>The id of a calendar the account owns with that name (one the app made before, e.g. on an earlier install); null when none.</returns>
    public async Task<string?> FindOwnedCalendarAsync(string accessToken, string name, CancellationToken cancellationToken)
    {
        string? pageToken = null;
        do
        {
            string query = "minAccessRole=owner&maxResults=250" + (pageToken is null ? string.Empty : "&pageToken=" + Uri.EscapeDataString(pageToken));
            using JsonDocument page = await _network.GetJsonAsync(
                new Uri(new Uri(_endpoints.CalendarApi, "users/me/calendarList") + "?" + query), accessToken, cancellationToken);
            if (page.RootElement.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in items.EnumerateArray())
                {
                    if (Text(item, "summary") == name && Text(item, "id") is { } id)
                    {
                        return id;
                    }
                }
            }

            pageToken = Text(page.RootElement, "nextPageToken");
        }
        while (pageToken is not null);

        return null;
    }

    /// <returns>The new calendar's id.</returns>
    public async Task<string> CreateCalendarAsync(string accessToken, string name, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["summary"] = name };
        using JsonDocument created = await _network.SendJsonAsync(
            HttpMethod.Post, new Uri(_endpoints.CalendarApi, "calendars"), accessToken, Json(body), cancellationToken);
        return Text(created.RootElement, "id") ?? throw new JsonException("Google's answer has no 'id'.");
    }

    /// <summary>
    /// Writes the event under its fixed id. A new one is inserted; if the id is taken (an event this app made before, also a deleted
    /// one, which Google keeps as cancelled) it is replaced instead. A known one is replaced; if Google no longer has it, it is inserted.
    /// </summary>
    /// <exception cref="HttpRequestException">404 when the calendar itself is gone; any other failure.</exception>
    public async Task PutEventAsync(
        string accessToken, string calendarId, string eventId, TaskCalendarEvent calendarEvent, bool isNew, CancellationToken cancellationToken)
    {
        if (isNew)
        {
            try
            {
                await InsertAsync(accessToken, calendarId, eventId, calendarEvent, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                await ReplaceAsync(accessToken, calendarId, eventId, calendarEvent, cancellationToken);
            }

            return;
        }

        try
        {
            await ReplaceAsync(accessToken, calendarId, eventId, calendarEvent, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            await InsertAsync(accessToken, calendarId, eventId, calendarEvent, cancellationToken);
        }
    }

    /// <summary>Deletes the event; one that is already gone (404, or 410 for an event deleted before) is fine.</summary>
    public async Task DeleteEventAsync(string accessToken, string calendarId, string eventId, CancellationToken cancellationToken)
    {
        try
        {
            await _network.DeleteAsync(EventUri(calendarId, eventId), accessToken, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Gone)
        {
            // Already deleted.
        }
    }

    /// <summary>
    /// The event as Google's JSON: id, title, start and end (a date for an all-day event, a time with its offset otherwise), the
    /// reminder overrides (no default reminders), free time, confirmed. Nothing else about the task.
    /// </summary>
    public static JsonObject EventBody(string eventId, TaskCalendarEvent calendarEvent)
    {
        var overrides = new JsonArray();
        foreach (int minutes in calendarEvent.ReminderMinutes)
        {
            overrides.Add(new JsonObject { ["method"] = "popup", ["minutes"] = minutes });
        }

        return new JsonObject
        {
            ["id"] = eventId,
            ["summary"] = calendarEvent.Title,
            ["start"] = Time(calendarEvent, start: true),
            ["end"] = Time(calendarEvent, start: false),
            ["reminders"] = new JsonObject { ["useDefault"] = false, ["overrides"] = overrides },
            ["transparency"] = "transparent",
            ["status"] = "confirmed",
        };
    }

    private async Task InsertAsync(
        string accessToken, string calendarId, string eventId, TaskCalendarEvent calendarEvent, CancellationToken cancellationToken)
    {
        Uri uri = new(_endpoints.CalendarApi, $"calendars/{Uri.EscapeDataString(calendarId)}/events");
        using JsonDocument _ = await _network.SendJsonAsync(HttpMethod.Post, uri, accessToken, Json(EventBody(eventId, calendarEvent)), cancellationToken);
    }

    private async Task ReplaceAsync(
        string accessToken, string calendarId, string eventId, TaskCalendarEvent calendarEvent, CancellationToken cancellationToken)
    {
        using JsonDocument _ = await _network.SendJsonAsync(
            HttpMethod.Put, EventUri(calendarId, eventId), accessToken, Json(EventBody(eventId, calendarEvent)), cancellationToken);
    }

    private Uri EventUri(string calendarId, string eventId)
    {
        return new Uri(_endpoints.CalendarApi, $"calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}");
    }

    private static JsonObject Time(TaskCalendarEvent calendarEvent, bool start)
    {
        if (calendarEvent.Day is DateOnly day)
        {
            DateOnly date = start ? day : day.AddDays(1);
            return new JsonObject { ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        }

        DateTimeOffset moment = (start ? calendarEvent.Start : calendarEvent.End)
                                ?? throw new InvalidOperationException("A timed event needs a start and an end.");
        return new JsonObject { ["dateTime"] = moment.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture) };
    }

    private static StringContent Json(JsonObject body)
    {
        return new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
    }

    private static string? Text(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(property, out JsonElement value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
