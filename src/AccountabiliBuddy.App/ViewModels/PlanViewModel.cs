using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Plan page (design 5o): a week strip and the selected day's timeline, the day types and coming one-off days, and the
/// Plan helper. Editing happens in two sheets over the page that hold the existing editors: the month view ("Change a day",
/// <see cref="ExceptionsViewModel"/>) and the week cycle, day types and categories (<see cref="DayPlanViewModel"/>). Every
/// edit, the helper's "Move it" included, goes through <see cref="ConfigurationService"/> like it always did, so the lock
/// during a focus session the user started and the wait for a change that frees today's work hours are unchanged.
/// Nothing runs on a timer here: the page refreshes when the plan, the calendar, the day's focus or the helper's dismissals
/// change, and when the page is shown.
/// </summary>
public sealed partial class PlanViewModel : ObservableObject
{
    private const int MostSuggestions = 3;
    private const int MostOneOffDays = 5;

    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;
    private readonly CalendarService _calendars;
    private readonly PlanHelperDismissals _dismissals;
    private readonly Func<TimeSpan> _goal;
    private readonly Func<DateOnly, TimeSpan> _focusedOn;
    private readonly Func<DateOnly, SleepTimes?> _nightFallback;

    public PlanViewModel(
        ConfigurationService configuration,
        SessionController sessions,
        CalendarService calendars,
        DayPlanViewModel dayPlan,
        ExceptionsViewModel exceptions,
        PlanHelperDismissals dismissals,
        Func<TimeSpan> goal,
        Func<DateOnly, TimeSpan> focusedOn,
        Func<DateOnly, SleepTimes?> nightFallback)
    {
        _configuration = configuration;
        _sessions = sessions;
        _calendars = calendars;
        DayPlan = dayPlan;
        Exceptions = exceptions;
        _dismissals = dismissals;
        _goal = goal;
        _focusedOn = focusedOn;
        _nightFallback = nightFallback;

        SelectedDate = Today;
        WeekStart = WeekSwitch.MondayOf(SelectedDate);
        _configuration.Changed += (_, _) => Refresh();
        _configuration.PendingChanged += (_, _) => Refresh();
        _calendars.Changed += (_, _) => Refresh();
        _dismissals.Changed += (_, _) => Refresh();
        _sessions.StateChanged += (_, _) => Refresh();
        dayPlan.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DayPlanViewModel.IsLocked) or nameof(DayPlanViewModel.HasUnsavedChanges))
            {
                Refresh();
            }
        };
        Refresh();
    }

    /// <summary>The editor for the week cycle, day types and categories (shown in the "Day types" sheet).</summary>
    public DayPlanViewModel DayPlan { get; }

    /// <summary>The month view and one-off day editor (shown in the "Change a day" sheet).</summary>
    public ExceptionsViewModel Exceptions { get; }

    /// <summary>Raised after a part was opened from the timeline: the sheet should scroll its editor into view.</summary>
    public event EventHandler? EditorRequested;

    /// <summary>Opens the Setup DP Assistant (locked during a session like the editors); set by the app.</summary>
    public ICommand? OpenDpAssistant { get; set; }

    /// <summary>Opens the Today page ("Start" on a goal that doesn't fit); set by the app.</summary>
    public Action? ShowToday { get; set; }

    private DateOnly Today => DateOnly.FromDateTime(LocalNow);

    private DateTime LocalNow => TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;

    /// <summary>The Monday of the week the strip shows.</summary>
    [ObservableProperty]
    public partial DateOnly WeekStart { get; set; }

    /// <summary>The day the timeline shows.</summary>
    [ObservableProperty]
    public partial DateOnly SelectedDate { get; set; }

    public ObservableCollection<PlanDayCellViewModel> Days { get; } = new();

    /// <summary>"Tuesday · Workday".</summary>
    [ObservableProperty]
    public partial string TitleText { get; set; } = string.Empty;

    /// <summary>What applies to a change right now: locked during a session, or when one waits.</summary>
    [ObservableProperty]
    public partial string RuleText { get; set; } = string.Empty;

    /// <summary>A focus session the user started is running: nothing in the plan can be changed.</summary>
    [ObservableProperty]
    public partial bool IsLocked { get; set; }

    /// <summary>"Change only Tue Oct 27": the day types sheet's way to give just the selected date its own plan.</summary>
    [ObservableProperty]
    public partial string OnlyThisDayText { get; set; } = string.Empty;

    /// <summary>The week strip isn't on today's week, or another day than today is selected.</summary>
    [ObservableProperty]
    public partial bool CanGoToToday { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoRows))]
    public partial IReadOnlyList<PlanTimelineRowViewModel> Rows { get; set; } = Array.Empty<PlanTimelineRowViewModel>();

    public bool HasNoRows => Rows.Count == 0;

    public ObservableCollection<PlanDayTypeRowViewModel> DayTypes { get; } = new();

    public ObservableCollection<PlanOneOffRowViewModel> OneOffDays { get; } = new();

    public bool HasOneOffDays => OneOffDays.Count > 0;

    /// <summary>"+2 more" under the one-off days when the list is cut short.</summary>
    [ObservableProperty]
    public partial string? MoreOneOffText { get; set; }

    /// <summary>The day plan editor has changes that aren't saved yet (it keeps them while its sheet is closed).</summary>
    [ObservableProperty]
    public partial bool HasUnsavedPlan { get; set; }

    public ObservableCollection<PlanSuggestionViewModel> Suggestions { get; } = new();

    /// <summary>What the last "Move it" did (moved, waits, refused), until the page is shown again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHelperMessage), nameof(ShowsHelper))]
    public partial string? HelperMessage { get; set; }

    public bool HasHelperMessage => HelperMessage is not null;

    /// <summary>The Plan helper card shows only when it has something to say.</summary>
    public bool ShowsHelper => Suggestions.Count > 0 || HelperMessage is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSheetOpen), nameof(IsChangeADayOpen), nameof(IsDayTypesOpen), nameof(SheetTitle))]
    public partial PlanSheet Sheet { get; set; }

    public bool IsSheetOpen => Sheet != PlanSheet.None;

    public bool IsChangeADayOpen => Sheet == PlanSheet.ChangeADay;

    public bool IsDayTypesOpen => Sheet == PlanSheet.DayTypes;

    public string SheetTitle => Sheet switch
    {
        PlanSheet.ChangeADay => "Change a day",
        PlanSheet.DayTypes => "Day types",
        _ => string.Empty,
    };

    /// <summary>Called when the page is shown: today may have changed since it last refreshed, and an old helper message is stale.</summary>
    public void OnShown()
    {
        HelperMessage = null;
        Refresh();
    }

    /// <summary>Today's focus changed (a session ended, a device synced): the helper's goal check and the "done" times read it again.</summary>
    public void NotifyFocusChanged() => Refresh();

    /// <summary>Refreshes what depends on the clock (the "Now" line moves); the page calls it each minute while it is on screen.</summary>
    public void Tick()
    {
        if (SelectedDate == Today)
        {
            Refresh();
        }
    }

    [RelayCommand]
    private void SelectDate(DateOnly date)
    {
        SelectedDate = date;
        WeekStart = WeekSwitch.MondayOf(date);
        Refresh();
    }

    [RelayCommand]
    private void PreviousWeek() => SelectDate(SelectedDate.AddDays(-7));

    [RelayCommand]
    private void NextWeek() => SelectDate(SelectedDate.AddDays(7));

    [RelayCommand]
    private void GoToToday() => SelectDate(Today);

    /// <summary>Opens the month view, on <paramref name="date"/> when given (a date from search, a one-off day, a suggestion).</summary>
    public void OpenChangeADay(DateOnly? date = null)
    {
        if (date is DateOnly day)
        {
            Exceptions.ShowDate(day);
        }

        Sheet = PlanSheet.ChangeADay;
    }

    /// <summary>Opens the day types editor on the template at <paramref name="templateIndex"/> (a search result) when given.</summary>
    public void OpenDayTypes(int? templateIndex = null)
    {
        if (templateIndex is int index)
        {
            DayPlan.SelectedTemplateIndex = index;
        }

        Sheet = PlanSheet.DayTypes;
    }

    [RelayCommand]
    private void OpenChangeADayHere() => OpenChangeADay(SelectedDate);

    [RelayCommand]
    private void OpenDayTypesSheet() => OpenDayTypes();

    [RelayCommand]
    private void OpenDayType(Guid templateId)
    {
        OpenDayTypes(DayPlan.Templates.ToList().FindIndex(template => template.Id == templateId));
        EditorRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenOneOffDay(DateOnly date) => OpenChangeADay(date);

    /// <summary>"Only this day" in the day types sheet: the selected date's own plan, in the month view.</summary>
    [RelayCommand]
    private void ChangeOnlySelectedDay()
    {
        OpenChangeADay(SelectedDate);
        if (Exceptions.PlanSelectedDayCommand.CanExecute(null))
        {
            Exceptions.PlanSelectedDayCommand.Execute(null);
        }
    }

    [RelayCommand]
    public void CloseSheet()
    {
        Sheet = PlanSheet.None;
    }

    // A timeline row was clicked: the editor for the day opens on that part. A day with its own plan opens in the month view,
    // any other in the day type it follows (where the note says that changes every such day).
    private void OpenPart(PlanTimelineRowViewModel row)
    {
        DayPlan plan = _configuration.Editing.DayPlan;
        if (plan.OneOffDays.Any(day => day.Date == SelectedDate))
        {
            OpenChangeADay(SelectedDate);
            if (Exceptions.PlanSelectedDayCommand.CanExecute(null))
            {
                Exceptions.PlanSelectedDayCommand.Execute(null);
            }

            SelectPart(Exceptions.OneOffEditor, row.PartId);
            EditorRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        Guid? templateId = plan.Cycle.TemplateIdFor(SelectedDate);
        int index = templateId is Guid id ? DayPlan.Templates.ToList().FindIndex(template => template.Id == id) : -1;
        OpenDayTypes(index >= 0 ? index : null);
        SelectPart(DayPlan.SelectedTemplate?.Editor, row.PartId);
        EditorRequested?.Invoke(this, EventArgs.Empty);
    }

    private static void SelectPart(DayPlanEditorViewModel? editor, Guid? partId)
    {
        if (editor is not null)
        {
            editor.Selected = partId is Guid id ? editor.Parts.FirstOrDefault(part => part.Id == id) : null;
        }
    }

    private void Refresh()
    {
        DayPlan plan = _configuration.Editing.DayPlan;
        DateOnly today = Today;
        DateTime now = LocalNow;

        IsLocked = _sessions.IsEnforcing;
        HasUnsavedPlan = DayPlan.HasUnsavedChanges;
        RuleText = IsLocked
            ? "Locked while a focus session you started runs."
            : $"A change that frees today's work hours waits {(int)_configuration.Current.Locks.LooseningDelay.TotalMinutes} min; any other applies at once.";
        CanGoToToday = SelectedDate != today;
        OnlyThisDayText = $"Change only {SelectedDate:ddd MMM d}";

        TitleText = $"{SelectedDate.DayOfWeek} · {FullTypeName(plan, SelectedDate)}";

        Days.Clear();
        for (int offset = 0; offset < 7; offset++)
        {
            DateOnly date = WeekStart.AddDays(offset);
            Days.Add(new PlanDayCellViewModel(
                date, date.DayOfWeek.ToString()[..3], ShortTypeName(plan, date), date == today, date == SelectedDate, picked => SelectDateCommand.Execute(picked)));
        }

        Rows = PlanTimelineBuilder.Build(
            plan, SelectedDate, now, _calendars.EventsOn(SelectedDate), _focusedOn(SelectedDate), _goal(), _nightFallback, OpenPart);

        RefreshDayTypes(plan);
        RefreshOneOffDays(plan, today);
        RefreshSuggestions(plan, now, today);
    }

    private void RefreshDayTypes(DayPlan plan)
    {
        Guid? followed = plan.OneOffDays.Any(day => day.Date == SelectedDate) ? null : plan.Cycle.TemplateIdFor(SelectedDate);
        DayTypes.Clear();
        foreach (PlanTemplate template in plan.Templates)
        {
            DayOfWeek[] days = plan.Cycle.Weeks
                .SelectMany(week => week)
                .Where(entry => entry.TemplateId == template.Id)
                .Select(entry => entry.Day)
                .Distinct()
                .OrderBy(day => ((int)day + 6) % 7)
                .ToArray();
            string text = days.Length == 0 ? "No weekday follows it" : string.Join(", ", days.Select(day => day.ToString()[..3]));
            DayTypes.Add(new PlanDayTypeRowViewModel(template.Id, template.Name, text, template.Id == followed, picked => OpenDayTypeCommand.Execute(picked)));
        }
    }

    private void RefreshOneOffDays(DayPlan plan, DateOnly today)
    {
        List<OneOffDay> coming = plan.OneOffDays.Where(day => day.Date >= today).OrderBy(day => day.Date).ToList();
        OneOffDays.Clear();
        foreach (OneOffDay day in coming.Take(MostOneOffDays))
        {
            string kind = day.Template.Mode == DayMode.Free ? "Free day" : plan.IsWorkday(day.Date) ? "Workday" : "Off day";
            string text = string.IsNullOrWhiteSpace(day.Note) ? kind : $"{day.Note.Trim()} · {kind}";
            OneOffDays.Add(new PlanOneOffRowViewModel(day.Date, day.Date.ToString("MMM d", CultureInfo.CurrentCulture), text, picked => OpenOneOffDayCommand.Execute(picked)));
        }

        MoreOneOffText = coming.Count > MostOneOffDays ? $"+{coming.Count - MostOneOffDays} more" : null;
        OnPropertyChanged(nameof(HasOneOffDays));
    }

    private void RefreshSuggestions(DayPlan plan, DateTime now, DateOnly today)
    {
        var events = new List<CalendarEvent>();
        for (int ahead = 0; ahead <= PlanHelper.DaysAhead; ahead++)
        {
            events.AddRange(_calendars.EventsOn(today.AddDays(ahead)).Select(entry => entry.Event));
        }

        IReadOnlyList<PlanSuggestion> found = PlanHelper.Suggest(plan, events.Distinct().ToList(), now, _goal(), _focusedOn(today), _nightFallback);
        Suggestions.Clear();
        foreach (PlanSuggestion suggestion in found.Where(suggestion => !_dismissals.IsDismissed(suggestion.Key)).Take(MostSuggestions))
        {
            Suggestions.Add(new PlanSuggestionViewModel(suggestion, ActionTextFor(suggestion), ActAsync, LeaveAsync));
        }

        OnPropertyChanged(nameof(ShowsHelper));
    }

    private static string ActionTextFor(PlanSuggestion suggestion) => suggestion.Kind switch
    {
        PlanSuggestionKind.MoveWork => suggestion.Edit is null ? "Show day" : "Move it",
        PlanSuggestionKind.GoalDoesNotFit => "Start",
        _ => "Change a day",
    };

    private async Task ActAsync(PlanSuggestionViewModel row)
    {
        PlanSuggestion suggestion = row.Suggestion;
        await _dismissals.MarkUsedAsync();
        switch (suggestion.Kind)
        {
            case PlanSuggestionKind.MoveWork when suggestion.Edit is PlanEdit edit:
                await MoveAsync(suggestion, edit);
                break;
            case PlanSuggestionKind.MoveWork:
                SelectDate(suggestion.Date);
                break;
            case PlanSuggestionKind.GoalDoesNotFit:
                ShowToday?.Invoke();
                break;
            default:
                OpenChangeADay(suggestion.Date);
                break;
        }
    }

    // "Move it": the same edit path as a manual one. Refused while a session the user started runs; otherwise saved through the
    // configuration, which waits when the change frees today's work hours (it never does for a coming day, but the path is the same).
    private async Task MoveAsync(PlanSuggestion suggestion, PlanEdit edit)
    {
        if (_sessions.IsEnforcing)
        {
            HelperMessage = "Not moved: the day plan is locked while a focus session you started runs.";
            return;
        }

        if (_configuration.IsReadOnly)
        {
            HelperMessage = "Not moved: settings are read-only (see banner).";
            return;
        }

        if (DayPlanValidator.Validate(edit.ApplyTo(_configuration.Editing.DayPlan)) is string problem)
        {
            HelperMessage = $"Not moved: {problem}";
            return;
        }

        await _configuration.UpdateAsync(document => document with { DayPlan = edit.ApplyTo(document.DayPlan) });
        HelperMessage = _configuration.DayPlanWaitText ?? $"Moved: {suggestion.Date.DayOfWeek} has its own plan now, with the work in its new place.";
        Refresh();
    }

    private async Task LeaveAsync(PlanSuggestionViewModel row)
    {
        HelperMessage = null;
        await _dismissals.DismissAsync(row.Suggestion.Key);
    }

    // "Tuesday · Workday": the day type's own name, "One-off day" for a date with its own plan, "No day type" for none.
    private static string FullTypeName(DayPlan plan, DateOnly date)
    {
        if (plan.OneOffDays.Any(day => day.Date == date))
        {
            return "Own plan";
        }

        return plan.Cycle.TemplateIdFor(date) is Guid id && plan.Templates.FirstOrDefault(template => template.Id == id) is PlanTemplate named
            ? named.Name
            : "No day type";
    }

    // The week strip's short name: "Work", "Light", "Off"; a name's trailing "day" is dropped.
    private static string ShortTypeName(DayPlan plan, DateOnly date)
    {
        if (plan.OneOffDays.Any(day => day.Date == date))
        {
            return "Own plan";
        }

        if (plan.Cycle.TemplateIdFor(date) is not Guid id || plan.Templates.FirstOrDefault(template => template.Id == id) is not PlanTemplate named)
        {
            return "None";
        }

        string name = named.Name.Trim();
        foreach (string suffix in new[] { " days", " day", "days", "day" })
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^suffix.Length];
            }
        }

        return name;
    }
}
