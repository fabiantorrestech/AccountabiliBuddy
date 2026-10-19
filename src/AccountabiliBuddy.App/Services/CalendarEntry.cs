using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>An event together with the calendar and source it belongs to, as the Exceptions tab shows it.</summary>
public sealed record CalendarEntry(CalendarEvent Event, CalendarInfo Calendar, CalendarSource Source)
{
    public bool IsEditable => Source.Kind == CalendarSourceKind.Local;
}
