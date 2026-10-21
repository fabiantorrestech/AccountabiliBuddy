using System.Collections.ObjectModel;
using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Exceptions tab: a month calendar with your calendar events (for planning; see <see cref="CalendarService"/>) and
/// the dates planned differently from the day plan. A one-off day (a day off, a half day) gets its own plan, edited
/// with the same bar and list as the Day plan tab, starting from that date's usual plan. Applies at once (ADR 18);
/// locked during a focus session the user started, like the Day plan tab.
/// </summary>
public sealed partial class ExceptionsViewModel : ObservableObject
{
    public const int MaxLinesPerDay = 3;

    private static readonly IBrush OneOffBrush = new SolidColorBrush(Color.FromRgb(42, 157, 143));

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
        SelectedDayPlanText = string.Empty;
        EditingDateTitle = string.Empty;
        NewEventStart = TimeSpan.FromHours(9);
        NewEventEnd = TimeSpan.FromHours(10);

        _configuration.Changed += (_, _) => Refresh();
        _configuration.PendingChanged += (_, _) => Refresh();
        _calendars.Changed += (_, _) => Refresh();
        _sessions.StateChanged += (_, _) => IsLocked = _sessions.IsEnforcing;
        IsLocked = _sessions.IsEnforcing;
        Refresh();
    }

    /// <summary>Monday-first, matching the grid.</summary>
    public static IReadOnlyList<string> WeekdayHeaders { get; } = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    public PendingChangesViewModel PendingChanges { get; }

    /// <summary>Every one-off day, by date; days that have passed are greyed out.</summary>
    public ObservableCollection<OneOffDayRowViewModel> OneOffDays { get; } = new();

    /// <summary>E.g. "Default day plan (Workdays)" or "Adjusted plan for this day".</summary>
    [ObservableProperty]
    public partial string SelectedDayPlanText { get; set; }

    /// <summary>The selected date has its own plan, different from the default it would follow.</summary>
    [ObservableProperty]
    public partial bool SelectedIsAdjusted { get; set; }

    /// <summary>The selected date's plan, one part per row in time order (its own plan or the default it follows).</summary>
    public ObservableCollection<AgendaRow> SelectedDayAgenda { get; } = new();

    [ObservableProperty]
    public partial bool SelectedHasOwnPlan { get; set; }

    /// <summary>The one-off day being edited; null when none is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingOneOff))]
    public partial DayPlanEditorViewModel? OneOffEditor { get; set; }

    public bool IsEditingOneOff => OneOffEditor is not null;

    [ObservableProperty]
    public partial string EditingDateTitle { get; set; }

    [ObservableProperty]
    public partial string? OneOffNote { get; set; }

    /// <summary>A focus session the user started is running: one-off days can't be changed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlanSelectedDayCommand), nameof(SaveOneOffDayCommand), nameof(ClearSelectedDayCommand))]
    public partial bool IsLocked { get; set; }

    private bool CanEditPlan => !IsLocked;

    // The date being edited, which stays put while another day is selected.
    private DateOnly _editingDate;

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

    /// <summary>Shows the month of <paramref name="date"/> with that day selected (e.g. opened from search).</summary>
    public void ShowDate(DateOnly date)
    {
        Month = new DateOnly(date.Year, date.Month, 1);
        SelectedDate = date;
        Refresh();
    }

    /// <summary>Starts editing the selected date's own plan, from its one-off day or its usual template.</summary>
    [RelayCommand(CanExecute = nameof(CanEditPlan))]
    private void PlanSelectedDay()
    {
        StartEditing(SelectedDate, note: null);
    }

    [RelayCommand(CanExecute = nameof(CanEditPlan))]
    private async Task SaveOneOffDayAsync()
    {
        if (OneOffEditor is not { } editor)
        {
            return;
        }

        DayPlan plan = _configuration.Current.DayPlan;
        string? problem = editor.ProblemNow();
        if (problem is not null)
        {
            StatusMessage = $"Not saved: {problem}";
            return;
        }

        DateOnly date = _editingDate;
        DayTemplate template = editor.ToTemplate();
        string? note = Trimmed(OneOffNote);
        if (note is null && DayAgenda.SameParts(template, DayAgenda.DefaultFor(plan, date)))
        {
            // Planned just like its default: it stays a normal day (no one-off day, nothing on the calendar).
            await _configuration.UpdateAsync(document => document with
            {
                DayPlan = document.DayPlan with
                {
                    OneOffDays = document.DayPlan.OneOffDays.Where(other => other.Date != date).ToList(),
                },
            });
            OneOffEditor = null;
            StatusMessage = $"Same as the default day plan, so {date:ddd d MMM} stays a normal day.";
            return;
        }

        Guid id = plan.OneOffDays.FirstOrDefault(day => day.Date == date)?.Id ?? Guid.NewGuid();
        var day = new OneOffDay(id, date, template, note);
        await _configuration.UpdateAsync(document => document with
        {
            DayPlan = document.DayPlan with
            {
                OneOffDays = document.DayPlan.OneOffDays.Where(other => other.Date != date).Append(day).OrderBy(other => other.Date).ToList(),
            },
        });
        OneOffEditor = null;
        StatusMessage = $"{date:ddd d MMM} has its own plan.";
    }

    [RelayCommand]
    private void CancelOneOffDay()
    {
        OneOffEditor = null;
        StatusMessage = null;
    }

    /// <summary>The selected date goes back to its usual plan.</summary>
    [RelayCommand(CanExecute = nameof(CanEditPlan))]
    private Task ClearSelectedDayAsync()
    {
        return RemoveOneOffDayAsync(SelectedDate);
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

    /// <summary>Starts planning the event's day differently, with the event as the note.</summary>
    [RelayCommand]
    private void PlanDayForEvent(EventRowViewModel row)
    {
        if (IsLocked)
        {
            return;
        }

        DateOnly date = DateOnly.FromDateTime(row.Entry.Event.LocalStart);
        StartEditing(date, row.Entry.Event.Title);
    }

    private void StartEditing(DateOnly date, string? note)
    {
        DayPlan plan = _configuration.Current.DayPlan;
        OneOffDay? existing = plan.OneOffDays.FirstOrDefault(day => day.Date == date);
        _editingDate = date;
        EditingDateTitle = $"Plan for {date.ToString("dddd d MMMM", CultureInfo.CurrentCulture)}";
        OneOffNote = existing?.Note ?? note;
        OneOffEditor = new DayPlanEditorViewModel(plan.TemplateFor(date), plan.Categories);
        StatusMessage = existing is null
            ? "Starts from the default day plan for that day. Change it, then Save."
            : null;
    }

    private void EditOneOffDay(OneOffDayRowViewModel row)
    {
        if (IsLocked)
        {
            return;
        }

        SelectedDate = row.Day.Date;
        Month = new DateOnly(row.Day.Date.Year, row.Day.Date.Month, 1);
        StartEditing(row.Day.Date, null);
        Refresh();
    }

    private async Task RemoveOneOffDayAsync(DateOnly date)
    {
        // The list's ✕ has no command guard of its own; a focus session the user started locks one-off days.
        if (IsLocked)
        {
            StatusMessage = "One-off days are locked while a focus session you started is running.";
            return;
        }

        await _configuration.UpdateAsync(document => document with
        {
            DayPlan = document.DayPlan with
            {
                OneOffDays = document.DayPlan.OneOffDays.Where(day => day.Date != date).ToList(),
            },
        });
        if (OneOffEditor is not null && _editingDate == date)
        {
            OneOffEditor = null;
        }

        StatusMessage = $"{date:ddd d MMM} follows the default day plan again.";
    }

    private void Refresh()
    {
        ProfileDocument document = _configuration.Editing;

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
                IsAdjusted: IsAdjusted(document.DayPlan, date),
                lines.Take(MaxLinesPerDay).ToList(),
                lines.Count > MaxLinesPerDay ? $"+{lines.Count - MaxLinesPerDay} more" : null));
        }

        Days = days;

        DayPlan plan = document.DayPlan;
        OneOffDay? own = plan.OneOffDays.FirstOrDefault(day => day.Date == SelectedDate);
        SelectedHasOwnPlan = own is not null;
        SelectedIsAdjusted = IsAdjusted(plan, SelectedDate);
        string usual = plan.Workdays.Contains(SelectedDate.DayOfWeek) ? "Workdays" : "Other days";
        SelectedDayPlanText = SelectedIsAdjusted ? "Adjusted plan for this day" : $"Default day plan ({usual})";
        SelectedDayAgenda.Clear();
        foreach (AgendaRow row in DayAgenda.For(plan, SelectedDate))
        {
            SelectedDayAgenda.Add(row);
        }
        OneOffDays.Clear();
        foreach (OneOffDay day in plan.OneOffDays.OrderBy(day => day.Date))
        {
            OneOffDays.Add(new OneOffDayRowViewModel(day, plan, today,
                EditOneOffDay,
                row => _ = RemoveOneOffDayAsync(row.Day.Date)));
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
    }

    private static IReadOnlyList<CalendarDayLine> LinesFor(ProfileDocument document, IReadOnlyList<CalendarEntry> events, DateOnly date)
    {
        var lines = new List<CalendarDayLine>();
        // A day planned like its default shows no "Own plan" (only its note, if it has one).
        if (document.DayPlan.OneOffDays.FirstOrDefault(day => day.Date == date) is { } own
            && (DayAgenda.DiffersFromDefault(document.DayPlan, own) || !string.IsNullOrWhiteSpace(own.Note)))
        {
            lines.Add(new CalendarDayLine(string.IsNullOrWhiteSpace(own.Note) ? "Own plan" : own.Note, OneOffBrush));
        }

        foreach (CalendarEntry entry in events)
        {
            string text = entry.Event.IsAllDay ? entry.Event.Title : $"{entry.Event.LocalStart:HH\\:mm} {entry.Event.Title}";
            lines.Add(new CalendarDayLine(text, new SolidColorBrush(Color.Parse(entry.Calendar.Color))));
        }

        return lines;
    }

    /// <summary>The date has a one-off day whose parts differ from its default plan.</summary>
    private static bool IsAdjusted(DayPlan plan, DateOnly date)
    {
        return plan.OneOffDays.FirstOrDefault(day => day.Date == date) is { } own && DayAgenda.DiffersFromDefault(plan, own);
    }

    private static string? Trimmed(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
