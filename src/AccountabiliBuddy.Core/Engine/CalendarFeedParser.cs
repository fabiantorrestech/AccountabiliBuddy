using AccountabiliBuddy.Core.Domain;
using Ical.Net;
using Ical.Net.DataTypes;
using IcalCalendar = Ical.Net.Calendar;
using IcalEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Turns iCal (.ics) text into <see cref="CalendarEvent"/>s in local time (Ical.Net). Repeating events are expanded
/// within the requested range; events with a time zone are converted to <paramref name="zone"/>; floating times and
/// all-day dates are taken as local. At most <see cref="MaxEvents"/> occurrences are kept, so a broken feed can't
/// flood the cache.
/// </summary>
public static class CalendarFeedParser
{
    public const int MaxEvents = 5000;

    /// <exception cref="FormatException">The text is not an iCalendar file.</exception>
    public static CalendarFeed Parse(string icsText, string calendarId, DateTime fromLocal, DateTime toLocal, TimeZoneInfo zone)
    {
        IcalCalendar calendar;
        try
        {
            calendar = IcalCalendar.Load(icsText) ?? throw new FormatException("The calendar file is empty.");
        }
        catch (Exception ex) when (ex is not FormatException)
        {
            throw new FormatException($"Not an iCalendar file: {ex.Message}", ex);
        }

        string? name = calendar.Properties.FirstOrDefault(property => property.Name == "X-WR-CALNAME")?.Value as string;

        var from = new CalDateTime(TimeZoneInfo.ConvertTimeToUtc(fromLocal, zone), "UTC", hasTime: true);
        var to = new CalDateTime(TimeZoneInfo.ConvertTimeToUtc(toLocal, zone), "UTC", hasTime: true);

        var events = new List<CalendarEvent>();
        foreach (Occurrence occurrence in calendar.GetOccurrences(from).TakeWhileBefore(to))
        {
            if (occurrence.Source is not IcalEvent source)
            {
                continue;
            }

            CalDateTime start = occurrence.Period.StartTime;
            CalDateTime end = occurrence.Period.EndTime
                              ?? (occurrence.Period.EffectiveDuration is Duration duration ? start.Add(duration) : start);
            bool isAllDay = !start.HasTime;

            DateTime localStart = ToLocal(start, zone);
            DateTime localEnd = ToLocal(end, zone);
            if (localEnd <= localStart)
            {
                localEnd = isAllDay ? localStart.AddDays(1) : localStart;
            }

            string uid = string.IsNullOrEmpty(source.Uid) ? Guid.NewGuid().ToString("N") : source.Uid;
            events.Add(new CalendarEvent(
                $"{uid}@{localStart:yyyyMMddTHHmm}",
                calendarId,
                string.IsNullOrWhiteSpace(source.Summary) ? "(no title)" : source.Summary.Trim(),
                localStart,
                localEnd,
                isAllDay));

            if (events.Count >= MaxEvents)
            {
                break;
            }
        }

        return new CalendarFeed(name, events);
    }

    private static DateTime ToLocal(CalDateTime value, TimeZoneInfo zone)
    {
        if (!value.HasTime)
        {
            return value.Date.ToDateTime(TimeOnly.MinValue);
        }

        return value.IsFloating
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified)
            : DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(value.AsUtc, zone), DateTimeKind.Unspecified);
    }
}
