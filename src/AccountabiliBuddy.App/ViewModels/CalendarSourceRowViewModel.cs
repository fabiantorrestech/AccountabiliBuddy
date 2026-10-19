using System.Globalization;
using AccountabiliBuddy.App.Services;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One source in the Exceptions tab's Calendars panel (in-app, an iCal link or a Google account) with its calendars.</summary>
public sealed class CalendarSourceRowViewModel
{
    public CalendarSourceRowViewModel(
        CalendarSource source, CalendarSourceStatus status, bool isFetching, IReadOnlyList<CalendarRowViewModel> calendars, TimeZoneInfo zone)
    {
        Id = source.Id;
        Name = source.Name;
        Calendars = calendars;
        CanRemove = source.Kind != CalendarSourceKind.Local;
        RemoveLabel = source.Kind == CalendarSourceKind.Google ? "Disconnect" : "Remove";
        StatusText = Describe(source, status, isFetching, zone);
        HasError = status.Error is not null && !isFetching;
    }

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyList<CalendarRowViewModel> Calendars { get; }

    public bool CanRemove { get; }

    public string RemoveLabel { get; }

    public string StatusText { get; }

    public bool HasError { get; }

    private static string Describe(CalendarSource source, CalendarSourceStatus status, bool isFetching, TimeZoneInfo zone)
    {
        if (source.Kind == CalendarSourceKind.Local)
        {
            return "Events you add here";
        }

        if (isFetching)
        {
            return "Updating…";
        }

        if (status.Error is { } error)
        {
            return $"Couldn't update: {error}";
        }

        if (status.LastSuccessAt is DateTimeOffset success)
        {
            DateTime local = TimeZoneInfo.ConvertTime(success, zone).DateTime;
            string when = local.Date == TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).Date
                ? local.ToString("HH:mm", CultureInfo.CurrentCulture)
                : local.ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
            return $"Updated {when}";
        }

        return "Not updated yet";
    }
}
