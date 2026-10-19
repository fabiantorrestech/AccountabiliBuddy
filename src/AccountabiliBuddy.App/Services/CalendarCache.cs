using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>Fetched events and fetch status (documents key "calendar.cache"), so the tab works offline.</summary>
public sealed record CalendarCache(
    IReadOnlyDictionary<string, IReadOnlyList<CalendarEvent>> EventsByCalendar,
    IReadOnlyDictionary<string, CalendarSourceStatus> StatusBySource)
{
    public static CalendarCache Empty { get; } = new(
        new Dictionary<string, IReadOnlyList<CalendarEvent>>(),
        new Dictionary<string, CalendarSourceStatus>());
}
