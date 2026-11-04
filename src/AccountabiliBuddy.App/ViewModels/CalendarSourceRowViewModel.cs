using System.Globalization;
using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One source in the Exceptions tab's Calendars panel (in-app, an iCal link or a Google account) with its calendars.</summary>
public sealed class CalendarSourceRowViewModel : ObservableObject
{
    private string _statusText;
    private bool _hasError;

    public CalendarSourceRowViewModel(
        CalendarSource source, CalendarSourceStatus status, bool isFetching, IReadOnlyList<CalendarRowViewModel> calendars, TimeZoneInfo zone)
    {
        Id = source.Id;
        Name = source.Name;
        Calendars = calendars;
        CanRemove = source.Kind != CalendarSourceKind.Local;
        RemoveLabel = source.Kind == CalendarSourceKind.Google ? "Disconnect" : "Remove";
        _statusText = Describe(source, status, isFetching, zone);
        _hasError = status.Error is not null && !isFetching;
    }

    /// <summary>Takes the new status and the calendars' colours and visibility in place, so the row, its open boxes and its focus stay.</summary>
    public void Update(CalendarSource source, CalendarSourceStatus status, bool isFetching, TimeZoneInfo zone)
    {
        StatusText = Describe(source, status, isFetching, zone);
        HasError = status.Error is not null && !isFetching;
        for (int index = 0; index < Calendars.Count && index < source.Calendars.Count; index++)
        {
            Calendars[index].Update(source.Calendars[index]);
        }
    }

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyList<CalendarRowViewModel> Calendars { get; }

    public bool CanRemove { get; }

    public string RemoveLabel { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

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
                ? OverlayTimeText.Clock(local)
                : $"{local.ToString("d MMM", CultureInfo.CurrentCulture)} {OverlayTimeText.Clock(local)}";
            return $"Updated {when}";
        }

        return "Not updated yet";
    }
}
