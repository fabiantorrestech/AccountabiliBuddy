using System.Collections.ObjectModel;
using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Exceptions tab: a month calendar of the weekly schedule, your calendar events (for planning; see
/// <see cref="CalendarService"/>), and the exceptions to the schedule on single dates.
/// <list type="bullet">
/// <item>Time off (all day or certain hours): no scheduled block runs then. Looser, so it waits for the countdown
/// (see <see cref="PendingChangesViewModel"/>) and the end of any running session.</item>
/// <item>One-off blocks: extra blocked time on one date. Stricter, so they apply at once.</item>
/// </list>
/// Never locked during focus: every change goes through the same stricter-now, looser-later rule.
/// </summary>
public sealed partial class ExceptionsViewModel : ObservableObject
{
    public const int MaxLinesPerDay = 3;

    private static readonly IBrush TimeOffBrush = new SolidColorBrush(Color.FromRgb(42, 157, 143));

    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;
    private readonly CalendarService _calendars;
    private readonly CalendarSyncService _sync;
    private readonly Func<Uri, bool> _openBrowser;

    /// <param name="openBrowser">Opens Google's sign-in page in the browser.</param>
    public ExceptionsViewModel(
        ConfigurationService configuration,
        SessionController sessions,
        CalendarService calendars,
        CalendarSyncService sync,
        Func<Uri, bool> openBrowser,
        PendingChangesViewModel pendingChanges)
    {
        _configuration = configuration;
        _sessions = sessions;
        _calendars = calendars;
        _sync = sync;
        _openBrowser = openBrowser;
        PendingChanges = pendingChanges;

        DateOnly today = Today;
        Month = new DateOnly(today.Year, today.Month, 1);
        SelectedDate = today;
        Days = Array.Empty<CalendarDayViewModel>();
        MonthTitle = string.Empty;
        SelectedDateTitle = string.Empty;
        NewTimeOffAllDay = true;
        NewTimeOffStart = TimeSpan.FromHours(12);
        NewTimeOffEnd = TimeSpan.FromHours(14);
        NewBlockStart = TimeSpan.FromHours(9);
        NewBlockEnd = TimeSpan.FromHours(12);
        NewEventStart = TimeSpan.FromHours(9);
        NewEventEnd = TimeSpan.FromHours(10);

        _configuration.Changed += (_, _) => Refresh();
        _configuration.PendingChanged += (_, _) => Refresh();
        _calendars.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Monday-first, matching the grid.</summary>
    public static IReadOnlyList<string> WeekdayHeaders { get; } = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    public PendingChangesViewModel PendingChanges { get; }

    public ObservableCollection<ProfileOption> Profiles { get; } = new();

    public ObservableCollection<TimeOffRowViewModel> SelectedTimeOff { get; } = new();

    public ObservableCollection<OneOffBlockRowViewModel> SelectedOneOffBlocks { get; } = new();

    public ObservableCollection<EventRowViewModel> SelectedEvents { get; } = new();

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

    [ObservableProperty]
    public partial string? NewEventTitle { get; set; }

    [ObservableProperty]
    public partial bool NewEventAllDay { get; set; }

    [ObservableProperty]
    public partial TimeSpan? NewEventStart { get; set; }

    [ObservableProperty]
    public partial TimeSpan? NewEventEnd { get; set; }

    /// <summary>The in-app event the event form is editing; null while the form adds a new one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingEvent))]
    [NotifyPropertyChangedFor(nameof(EventButtonText))]
    public partial string? EditingEventId { get; set; }

    public bool IsEditingEvent => EditingEventId is not null;

    public string EventButtonText => IsEditingEvent ? "Save event" : "Add event";

    // An edited event keeps its own date, even if another day is selected meanwhile.
    private DateOnly _editingEventDate;

    /// <summary>First day of the month on view.</summary>
    [ObservableProperty]
    public partial DateOnly Month { get; set; }

    [ObservableProperty]
    public partial string MonthTitle { get; set; }

    /// <summary>42 cells (6 weeks), Monday-first. Replaced, not mutated, so the grid redraws.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<CalendarDayViewModel> Days { get; set; }

    [ObservableProperty]
    public partial DateOnly SelectedDate { get; set; }

    [ObservableProperty]
    public partial string SelectedDateTitle { get; set; }

    [ObservableProperty]
    public partial bool NewTimeOffAllDay { get; set; }

    [ObservableProperty]
    public partial TimeSpan? NewTimeOffStart { get; set; }

    [ObservableProperty]
    public partial TimeSpan? NewTimeOffEnd { get; set; }

    [ObservableProperty]
    public partial string? NewTimeOffNote { get; set; }

    [ObservableProperty]
    public partial TimeSpan? NewBlockStart { get; set; }

    [ObservableProperty]
    public partial TimeSpan? NewBlockEnd { get; set; }

    [ObservableProperty]
    public partial ProfileOption? NewBlockProfile { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    private DateOnly Today => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime);

    private DateTime LocalNow => TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;

    [RelayCommand]
    private void PreviousMonth()
    {
        Month = Month.AddMonths(-1);
        Refresh();
    }

    [RelayCommand]
    private void NextMonth()
    {
        Month = Month.AddMonths(1);
        Refresh();
    }

    [RelayCommand]
    private void GoToToday()
    {
        DateOnly today = Today;
        Month = new DateOnly(today.Year, today.Month, 1);
        SelectedDate = today;
        Refresh();
    }

    [RelayCommand]
    private void SelectDay(CalendarDayViewModel day)
    {
        SelectedDate = day.Date;
        if (!day.IsInMonth)
        {
            Month = new DateOnly(day.Date.Year, day.Date.Month, 1);
        }

        StatusMessage = null;
        Refresh();
    }

    [RelayCommand]
    private async Task AddTimeOffAsync()
    {
        if (SelectedDate < Today)
        {
            StatusMessage = "That day is already over.";
            return;
        }

        TimeOff timeOff;
        if (NewTimeOffAllDay)
        {
            timeOff = new TimeOff(Guid.NewGuid(), SelectedDate, Note: Trimmed(NewTimeOffNote));
        }
        else
        {
            if (NewTimeOffStart is not TimeSpan start || NewTimeOffEnd is not TimeSpan end || start == end)
            {
                StatusMessage = "Choose a start and an end time (different from each other).";
                return;
            }

            timeOff = new TimeOff(Guid.NewGuid(), SelectedDate, TimeOnly.FromTimeSpan(start), TimeOnly.FromTimeSpan(end),
                Trimmed(NewTimeOffNote));
        }

        if (timeOff.GetWindow().End <= LocalNow)
        {
            StatusMessage = "That time is already over.";
            return;
        }

        PendingChanges? waiting = await _configuration.UpdateAsync(document => document with
        {
            Schedule = document.Schedule with { TimeOff = document.Schedule.TimeOff.Append(timeOff).ToList() },
        });
        NewTimeOffNote = null;
        StatusMessage = waiting is null
            ? "Time off added."
            : "Time off added. It's a looser change: it takes effect after the countdown above, and never during a session.";
    }

    [RelayCommand]
    private async Task RemoveTimeOffAsync(TimeOffRowViewModel row)
    {
        // Removing time off only makes the schedule stricter, so it applies at once.
        await _configuration.UpdateAsync(document => document with
        {
            Schedule = document.Schedule with
            {
                TimeOff = document.Schedule.TimeOff.Where(off => off.Id != row.TimeOff.Id).ToList(),
            },
        });
        StatusMessage = "Time off removed.";
    }

    [RelayCommand]
    private async Task AddOneOffBlockAsync()
    {
        if (NewBlockProfile is null)
        {
            StatusMessage = "Create a profile in the Profiles tab first.";
            return;
        }

        if (NewBlockStart is not TimeSpan start || NewBlockEnd is not TimeSpan end || start == end)
        {
            StatusMessage = "Choose a start and an end time (different from each other). An end earlier than the start runs past midnight.";
            return;
        }

        var block = new OneOffBlock(Guid.NewGuid(), SelectedDate, TimeOnly.FromTimeSpan(start), TimeOnly.FromTimeSpan(end),
            NewBlockProfile.Id);
        if (block.GetWindow().End <= LocalNow)
        {
            StatusMessage = "That time is already over.";
            return;
        }

        await _configuration.UpdateAsync(document => document with
        {
            Schedule = document.Schedule with { OneOffBlocks = document.Schedule.OneOffBlocks.Append(block).ToList() },
        });
        StatusMessage = "One-off block added.";
    }

    [RelayCommand]
    private async Task RemoveOneOffBlockAsync(OneOffBlockRowViewModel row)
    {
        PendingChanges? waiting = await _configuration.UpdateAsync(document => document with
        {
            Schedule = document.Schedule with
            {
                OneOffBlocks = document.Schedule.OneOffBlocks.Where(block => block.Id != row.Block.Id).ToList(),
            },
        });
        StatusMessage = waiting is null
            ? "One-off block removed."
            : "One-off block removed. It's a looser change: it takes effect after the countdown above.";
    }

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

    [RelayCommand]
    private void AddEvent()
    {
        string title = NewEventTitle?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            StatusMessage = "Give the event a title.";
            return;
        }

        DateTime day = (IsEditingEvent ? _editingEventDate : SelectedDate).ToDateTime(TimeOnly.MinValue);
        DateTime localStart;
        DateTime localEnd;
        if (NewEventAllDay)
        {
            localStart = day;
            localEnd = day.AddDays(1);
        }
        else
        {
            if (NewEventStart is not TimeSpan start || NewEventEnd is not TimeSpan end || start == end)
            {
                StatusMessage = "Choose a start and an end time (different from each other).";
                return;
            }

            localStart = day + start;
            localEnd = end > start ? day + end : day.AddDays(1) + end;
        }

        if (EditingEventId is string eventId)
        {
            _calendars.UpdateLocalEvent(eventId, title, localStart, localEnd, NewEventAllDay);
            StatusMessage = "Event saved.";
        }
        else
        {
            _calendars.AddLocalEvent(title, localStart, localEnd, NewEventAllDay);
            StatusMessage = "Event added.";
        }

        EditingEventId = null;
        NewEventTitle = null;
    }

    /// <summary>Loads an in-app event into the event form; Save event writes it back.</summary>
    [RelayCommand]
    private void EditEvent(EventRowViewModel row)
    {
        if (!row.IsEditable)
        {
            return;
        }

        CalendarEvent calendarEvent = row.Entry.Event;
        _editingEventDate = DateOnly.FromDateTime(calendarEvent.LocalStart);
        EditingEventId = calendarEvent.Id;
        NewEventTitle = calendarEvent.Title;
        NewEventAllDay = calendarEvent.IsAllDay;
        if (!calendarEvent.IsAllDay)
        {
            NewEventStart = calendarEvent.LocalStart.TimeOfDay;
            NewEventEnd = calendarEvent.LocalEnd.TimeOfDay;
        }

        StatusMessage = $"Editing '{calendarEvent.Title}'. Change it, then press Save event.";
    }

    [RelayCommand]
    private void CancelEventEdit()
    {
        EditingEventId = null;
        NewEventTitle = null;
        StatusMessage = null;
    }

    [RelayCommand]
    private void RemoveEvent(EventRowViewModel row)
    {
        if (row.IsEditable)
        {
            _calendars.RemoveLocalEvent(row.Entry.Event.Id);
            if (EditingEventId == row.Entry.Event.Id)
            {
                EditingEventId = null;
                NewEventTitle = null;
            }

            StatusMessage = "Event removed.";
        }
    }

    /// <summary>Fills in the time off form from an event; the user checks it and presses Add time off.</summary>
    [RelayCommand]
    private void TimeOffForEvent(EventRowViewModel row)
    {
        CalendarEvent calendarEvent = row.Entry.Event;
        bool sameDay = DateOnly.FromDateTime(calendarEvent.LocalStart) == SelectedDate
                       && calendarEvent.LocalEnd <= SelectedDate.ToDateTime(TimeOnly.MinValue).AddDays(1);
        NewTimeOffAllDay = calendarEvent.IsAllDay || !sameDay;
        if (!NewTimeOffAllDay)
        {
            NewTimeOffStart = calendarEvent.LocalStart.TimeOfDay;
            NewTimeOffEnd = calendarEvent.LocalEnd.TimeOfDay;
        }

        NewTimeOffNote = calendarEvent.Title;
        StatusMessage = $"Time off filled in from '{calendarEvent.Title}'. Check it, then press Add time off.";
    }

    private void Refresh()
    {
        ProfileDocument document = _configuration.Editing;
        NewBlockProfile = ProfileOptions.Refresh(Profiles, document.Profiles, NewBlockProfile?.Id);

        MonthTitle = Month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        SelectedDateTitle = SelectedDate.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture);

        DateOnly today = Today;
        int offset = ((int)Month.DayOfWeek + 6) % 7; // Monday-first
        DateOnly first = Month.AddDays(-offset);
        var days = new List<CalendarDayViewModel>(42);
        for (int i = 0; i < 42; i++)
        {
            DateOnly date = first.AddDays(i);
            IReadOnlyList<CalendarDayLine> lines = LinesFor(document, _calendars.EventsOn(date), date);
            days.Add(new CalendarDayViewModel(
                date,
                IsInMonth: date.Month == Month.Month,
                IsToday: date == today,
                IsSelected: date == SelectedDate,
                lines.Take(MaxLinesPerDay).ToList(),
                lines.Count > MaxLinesPerDay ? $"+{lines.Count - MaxLinesPerDay} more" : null));
        }

        Days = days;

        SelectedTimeOff.Clear();
        foreach (TimeOff timeOff in document.Schedule.TimeOff.Where(off => off.Date == SelectedDate)
                     .OrderBy(off => off.GetWindow().Start))
        {
            SelectedTimeOff.Add(new TimeOffRowViewModel(timeOff));
        }

        SelectedEvents.Clear();
        foreach (CalendarEntry entry in _calendars.EventsOn(SelectedDate))
        {
            SelectedEvents.Add(new EventRowViewModel(entry));
        }

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

        SelectedOneOffBlocks.Clear();
        foreach (OneOffBlock block in document.Schedule.OneOffBlocks.Where(block => block.Date == SelectedDate)
                     .OrderBy(block => block.Start))
        {
            SelectedOneOffBlocks.Add(new OneOffBlockRowViewModel(
                block, ProfileOptions.NameOf(document.Profiles, block.ProfileId) ?? "(deleted profile)"));
        }
    }

    private static IReadOnlyList<CalendarDayLine> LinesFor(ProfileDocument document, IReadOnlyList<CalendarEntry> events, DateOnly date)
    {
        var lines = new List<CalendarDayLine>();

        foreach (TimeOff timeOff in document.Schedule.TimeOff.Where(off => off.Date == date).OrderBy(off => off.GetWindow().Start))
        {
            string text = timeOff.IsAllDay ? "Off all day" : $"Off {timeOff.Start:HH\\:mm}–{timeOff.End:HH\\:mm}";
            lines.Add(new CalendarDayLine(text, TimeOffBrush));
        }

        foreach (CalendarEntry entry in events)
        {
            string text = entry.Event.IsAllDay ? entry.Event.Title : $"{entry.Event.LocalStart:HH\\:mm} {entry.Event.Title}";
            lines.Add(new CalendarDayLine(text, new SolidColorBrush(Color.Parse(entry.Calendar.Color))));
        }

        var blocks = document.Schedule.Blocks
            .Where(block => block.Day == date.DayOfWeek)
            .Select(block => (block.Start, block.End, block.ProfileId, Prefix: string.Empty))
            .Concat(document.Schedule.OneOffBlocks
                .Where(block => block.Date == date)
                .Select(block => (block.Start, block.End, block.ProfileId, Prefix: "+ ")))
            .OrderBy(block => block.Start);
        foreach ((TimeOnly start, TimeOnly end, Guid profileId, string prefix) in blocks)
        {
            string profile = ProfileOptions.NameOf(document.Profiles, profileId) ?? "deleted profile";
            lines.Add(new CalendarDayLine(
                $"{prefix}{start:HH\\:mm}–{end:HH\\:mm} {profile}",
                new SolidColorBrush(ProfileColors.For(profileId))));
        }

        return lines;
    }

    private static string? Trimmed(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
