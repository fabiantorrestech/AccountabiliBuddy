using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The calendars connected to this PC (Settings › Connections): the in-app calendar, iCal links and Google accounts, with
/// show/hide, colour, adding a link, signing in to Google and fetching now. They used to sit in the Calendar tab's
/// "Calendars" panel; the Plan page's month view only reads what they bring in.
/// </summary>
public sealed partial class CalendarFeedsViewModel : ObservableObject
{
    private readonly CalendarService _calendars;
    private readonly CalendarSyncService _sync;
    private readonly SessionController _sessions;
    private readonly Func<Uri, bool> _openBrowser;

    /// <param name="openBrowser">Opens Google's sign-in page in the browser.</param>
    public CalendarFeedsViewModel(CalendarService calendars, CalendarSyncService sync, SessionController sessions, Func<Uri, bool> openBrowser)
    {
        _calendars = calendars;
        _sync = sync;
        _sessions = sessions;
        _openBrowser = openBrowser;
        _calendars.Changed += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<CalendarSourceRowViewModel> CalendarSources { get; } = new();

    [ObservableProperty]
    public partial string? NewLinkAddress { get; set; }

    [ObservableProperty]
    public partial string? NewLinkGroup { get; set; }

    [ObservableProperty]
    public partial string? CalendarMessage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCalendarsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectGoogleCommand))]
    public partial bool IsCalendarBusy { get; set; }

    private bool CanUseCalendars => !IsCalendarBusy;

    /// <summary>Fetches every event of every connected calendar now, hidden ones included.</summary>
    [RelayCommand(CanExecute = nameof(CanUseCalendars))]
    private async Task RefreshCalendarsAsync()
    {
        IsCalendarBusy = true;
        CalendarMessage = "Refreshing calendars…";
        try
        {
            await _sync.RefreshAllAsync();
            CalendarMessage = "Calendars refreshed.";
        }
        finally
        {
            IsCalendarBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddLinkAsync()
    {
        if (string.IsNullOrWhiteSpace(NewLinkAddress))
        {
            CalendarMessage = "Paste the calendar's iCal address first.";
            return;
        }

        CalendarMessage = "Adding the calendar…";
        string? error = await _sync.AddLinkAsync(NewLinkAddress, NewLinkGroup);
        if (error is null)
        {
            NewLinkAddress = null;
            CalendarMessage = "Calendar added.";
        }
        else
        {
            CalendarMessage = error;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseCalendars))]
    private async Task ConnectGoogleAsync()
    {
        IsCalendarBusy = true;
        CalendarMessage = "Finish signing in to Google in your browser…";
        try
        {
            string? error = await _sync.ConnectGoogleAsync(_openBrowser, CancellationToken.None);
            CalendarMessage = error ?? "Google account connected.";
        }
        finally
        {
            IsCalendarBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveSourceAsync(CalendarSourceRowViewModel row)
    {
        await _sync.RemoveSourceAsync(row.Id);
        CalendarMessage = $"{row.Name} removed.";
    }

    private void Refresh()
    {
        CalendarSources.Clear();
        foreach (CalendarSource source in _calendars.Sources)
        {
            CalendarSources.Add(new CalendarSourceRowViewModel(
                source,
                _calendars.StatusOf(source.Id),
                _sync.IsFetching && source.Kind != CalendarSourceKind.Local && _calendars.StatusOf(source.Id).LastAttemptAt is null,
                source.Calendars.Select(calendar => new CalendarRowViewModel(_calendars, source, calendar)).ToList(),
                _sessions.TimeProvider.LocalTimeZone));
        }
    }
}
