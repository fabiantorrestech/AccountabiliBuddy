using System.Collections.ObjectModel;
using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Settings › Tasks (Tasks redesign Phase 7, spec 03 §H, 04 §I, docs/Settings Restructure Plan): Reminders, Labels, During a session,
/// New tasks, Shelf, Keyboard shortcuts, the end-of-day link and Reset. Every row edits its service at once; this page keeps no copy
/// to save. The per-mode settings (<see cref="TaskSettingsService"/>) show and edit the active mode's copy and follow its rules: a
/// stricter value applies at once, a looser one waits the looser-change wait (shown under the row) and is refused while a session runs.
/// The "During a session" rows and Reset also rest during a session the user started, like the other locked cards (<see cref="IsLocked"/>).
/// </summary>
public sealed partial class TaskSettingsViewModel : ObservableObject
{
    private const string RefusedText = "Not during a session: that would loosen it. Stricter changes apply at once.";

    private static readonly int[] TimeLimitPresets = [0, 15, 25, 50];
    private static readonly string[] NewReminderRules = [TaskReminderSettings.NoNewReminder, TaskReminder.AtStart, TaskReminder.TenMinutesBefore];
    private static readonly DayOfWeek[] Days =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private readonly TaskSettingsService _settings;
    private readonly TaskReminderSettingsService _reminders;
    private readonly TaskCalendarExportService? _export;
    private readonly TaskViewService? _views;
    private readonly TaskAccountSettingsService? _account;
    private readonly TimeProvider _time;

    // True while the page reads the services: the properties' change handlers then save nothing.
    private bool _loading;

    // Custom was picked for the default time limit: it stays picked even while its minutes match a preset (25 stays "Custom 25").
    private bool _customTimeLimit;

    public TaskSettingsViewModel(
        TaskSettingsService settings,
        TaskReminderSettingsService reminders,
        TaskCalendarExportService? export,
        TaskViewService? views,
        TaskAccountSettingsService? account,
        TimeProvider time)
    {
        _settings = settings;
        _reminders = reminders;
        _export = export;
        _views = views;
        _account = account;
        _time = time;
        RingingSummary = ExportStatus = PickedStatus = TimeLimitHint = DefaultViewHint = StatusText = ResetNote = string.Empty;
        CompactPending = ShelfPending = ActionsPending = SessionViewPending = StepAwayPending = SideTaskPending = string.Empty;
        NewMarkerOpen = NewMarkerClose = MarkerProblem = string.Empty;

        _settings.Changed += (_, _) => Dispatcher.UIThread.Post(Load);
        _reminders.Changed += (_, _) => Dispatcher.UIThread.Post(Load);
        if (_export is not null)
        {
            _export.StatusChanged += (_, _) => Dispatcher.UIThread.Post(Load);
        }

        if (_views is not null)
        {
            _views.Changed += (_, _) => Dispatcher.UIThread.Post(Load);
        }

        if (_account is not null)
        {
            _account.Changed += (_, _) => Dispatcher.UIThread.Post(Load);
        }

        Load();
    }

    // ---- set by the app ----

    /// <summary>Opens another Settings page (the link rows).</summary>
    public Action<SettingsSection>? OpenSection { get; init; }

    /// <summary>Opens Google's consent page in the browser (Allow).</summary>
    public Func<Uri, bool>? OpenBrowser { get; init; }

    /// <summary>Focus sessions › Pomodoro defaults › Focus, in minutes (the Default time limit's hint).</summary>
    public Func<int>? PomodoroFocusMinutes { get; init; }

    /// <summary>The start-a-session shortcut as set in Focus sessions › Starting (shown read only).</summary>
    public Func<string>? StartSessionKeys { get; init; }

    /// <summary>How long a looser change waits (Focus sessions › Looser changes).</summary>
    public Func<TimeSpan>? LooserWait { get; init; }

    /// <summary>The labels editor (the Tasks page's; the Labels rework comes next).</summary>
    public CategoryEditorViewModel? Labels { get; init; }

    // ---- page state ----

    /// <summary>A session the user started is running: the During a session rows and Reset rest (view only).</summary>
    [ObservableProperty]
    public partial bool IsLocked { get; set; }

    /// <summary>What the last change couldn't do (a refusal during a session, a failed Allow).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusText { get; set; }

    public bool HasStatus => StatusText.Length > 0;

    public void SetLocked(bool locked)
    {
        IsLocked = locked;
    }

    // ---- Reminders ----

    /// <summary>"Volume 70% · rising · dismiss waits 5 s · toast".</summary>
    [ObservableProperty]
    public partial string RingingSummary { get; set; }

    public IReadOnlyList<string> AddRemindersToChoices { get; } = ["Google Calendar", "Nowhere"];

    /// <summary>0 = Google Calendar (the app's "AB Reminders"), 1 = Nowhere. Windows Calendar isn't built (ADR 78).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesGoogle))]
    public partial int AddRemindersToIndex { get; set; }

    public bool UsesGoogle => AddRemindersToIndex == 0 && _export is not null;

    /// <summary>Where export stands: why it isn't working, that Google needs an OK, or which account it writes to.</summary>
    [ObservableProperty]
    public partial string ExportStatus { get; set; }

    /// <summary>The Allow button shows (Google needs the OK, or export has a problem the OK may fix).</summary>
    [ObservableProperty]
    public partial bool ShowsAllow { get; set; }

    /// <summary>Google's page is open in the browser: the Allow buttons wait.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AllowExportCommand), nameof(UsePickedCalendarCommand))]
    public partial bool IsAllowing { get; set; }

    public IReadOnlyList<string> DateOnlyChoices { get; } = ["Timed at 9:00 AM", "All-day event"];

    [ObservableProperty]
    public partial int DateOnlyIndex { get; set; }

    /// <summary>The experimental picked calendar is on (set for the account) or being chosen.</summary>
    [ObservableProperty]
    public partial bool PickedCalendarOn { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<TaskPickableCalendar> PickableCalendars { get; set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UsePickedCalendarCommand))]
    public partial TaskPickableCalendar? ChosenCalendar { get; set; }

    /// <summary>"Tasks with a due date go to “Work”." or what is needed first.</summary>
    [ObservableProperty]
    public partial string PickedStatus { get; set; }

    public IReadOnlyList<string> NewReminderChoices { get; } = ["None", "At start time", "10 min before"];

    /// <summary>"New reminders start at"; -1 when the saved rule is another one (1 hour before, the day before).</summary>
    [ObservableProperty]
    public partial int NewReminderIndex { get; set; }

    // ---- Labels ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LabelsButtonText))]
    public partial bool ShowsLabelsEditor { get; set; }

    public bool HasLabels => Labels is not null;

    public string LabelsButtonText => ShowsLabelsEditor ? "Done" : "Edit labels";

    public IReadOnlyList<string> LabelStyleChoices { get; } = ["Dot", "Pill before", "Pill in details"];

    [ObservableProperty]
    public partial int LabelStyleIndex { get; set; }

    // ---- During a session ----

    [ObservableProperty]
    public partial bool CompactInSession { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCompactPending))]
    public partial string CompactPending { get; set; }

    public bool HasCompactPending => CompactPending.Length > 0;

    [ObservableProperty]
    public partial bool HideShelfInSession { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShelfPending))]
    public partial string ShelfPending { get; set; }

    public bool HasShelfPending => ShelfPending.Length > 0;

    public IReadOnlyList<string> ActionChoices { get; } = ["2", "3", "4", "5"];

    /// <summary>Actions in a session, 2 to 5 (index 0 = 2).</summary>
    [ObservableProperty]
    public partial int ActionsIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionsPending))]
    public partial string ActionsPending { get; set; }

    public bool HasActionsPending => ActionsPending.Length > 0;

    public IReadOnlyList<string> SessionViewChoices { get; } = ["Quiet", "Grouped"];

    [ObservableProperty]
    public partial int SessionViewIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSessionViewPending))]
    public partial string SessionViewPending { get; set; }

    public bool HasSessionViewPending => SessionViewPending.Length > 0;

    [ObservableProperty]
    public partial bool NextStepTakesSlot { get; set; }

    public IReadOnlyList<string> TimerChoices { get; } = ["Keep the timer running", "Pause the timer"];

    [ObservableProperty]
    public partial int StepAwayIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStepAwayPending))]
    public partial string StepAwayPending { get; set; }

    public bool HasStepAwayPending => StepAwayPending.Length > 0;

    [ObservableProperty]
    public partial int SideTaskIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSideTaskPending))]
    public partial string SideTaskPending { get; set; }

    public bool HasSideTaskPending => SideTaskPending.Length > 0;

    // ---- New tasks ----

    public IReadOnlyList<string> TimeLimitChoices { get; } = ["None", "15", "25", "50", "Custom"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomTimeLimit))]
    public partial int TimeLimitIndex { get; set; }

    public bool IsCustomTimeLimit => TimeLimitIndex == TimeLimitChoices.Count - 1;

    /// <summary>The Custom box's minutes.</summary>
    [ObservableProperty]
    public partial decimal? CustomTimeLimit { get; set; }

    /// <summary>"Minutes for a task typed in the add box. Pomodoro focus is 25 (Focus sessions › Pomodoro defaults)."</summary>
    [ObservableProperty]
    public partial string TimeLimitHint { get; set; }

    [ObservableProperty]
    public partial bool SuggestShorterTitles { get; set; }

    public ObservableCollection<ProjectMarkerRowViewModel> Markers { get; } = new();

    [ObservableProperty]
    public partial bool HasMarkers { get; set; }

    [ObservableProperty]
    public partial string NewMarkerOpen { get; set; }

    [ObservableProperty]
    public partial string NewMarkerClose { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMarkerProblem))]
    public partial string MarkerProblem { get; set; }

    public bool HasMarkerProblem => MarkerProblem.Length > 0;

    [ObservableProperty]
    public partial IReadOnlyList<TaskDefaultViewChoice> ViewChoices { get; set; } = [];

    [ObservableProperty]
    public partial TaskDefaultViewChoice? DefaultView { get; set; }

    public bool HasViews => _views is not null;

    [ObservableProperty]
    public partial string DefaultViewHint { get; set; }

    // ---- Shelf ----

    [ObservableProperty]
    public partial bool PinHighlights { get; set; }

    public IReadOnlyList<string> UntouchedChoices { get; } = ["1 wk", "2 wk", "3 wk", "4 wk"];

    [ObservableProperty]
    public partial int UntouchedIndex { get; set; }

    [ObservableProperty]
    public partial bool ShowAgeOnRows { get; set; }

    [ObservableProperty]
    public partial bool TidyUpOn { get; set; }

    public IReadOnlyList<string> DayChoices { get; } = Days.Select(day => CultureInfo.InvariantCulture.DateTimeFormat.GetDayName(day)).ToList();

    [ObservableProperty]
    public partial int TidyUpDayIndex { get; set; }

    // ---- Keyboard shortcuts ----

    /// <summary>The start-a-session shortcut, owned by Focus sessions › Starting.</summary>
    public string StartSessionText => StartSessionKeys?.Invoke() is { Length: > 0 } keys ? keys : "Ctrl+Shift+S";

    public IReadOnlyList<TaskShortcutRow> Shortcuts { get; } =
    [
        new("Ctrl+\\", "Show or hide the Shelf"),
        new("Ctrl+J", "Jot it down"),
        new("Ctrl+F", "Search"),
        new("Ctrl+G", "Go to a task by its ID"),
        new("Ctrl+Z", "Undo (the last 10 actions)"),
        new("Esc", "Close or clear: a popover, the selection, the open task, the search"),
        new("Del", "Delete"),
        new("F2", "Rename"),
        new("H", "Highlight"),
        new("Enter", "Open the task"),
        new("Space", "Finish the task"),
        new("↑ / ↓", "Move within a list (each list is one Tab stop)"),
        new("Alt+↑ / Alt+↓", "Move the task up or down"),
    ];

    // ---- Reset ----

    [ObservableProperty]
    public partial bool IsConfirmingReset { get; set; }

    /// <summary>What the last reset left waiting or refused.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResetNote))]
    public partial string ResetNote { get; set; }

    public bool HasResetNote => ResetNote.Length > 0;

    /// <summary>Reads every value from its service again (after any change, here or elsewhere: a mode switch, a wait that ended, sync).</summary>
    public void Load()
    {
        _loading = true;
        try
        {
            TaskSettings settings = _settings.Current;
            TaskReminderSettings reminders = _reminders.Current;

            RingingSummary = RingingSummaryOf(reminders);
            AddRemindersToIndex = reminders.NewRemindersTo == TaskCalendarDestination.Nowhere ? 1 : 0;
            DateOnlyIndex = reminders.DateOnlyEvent == TaskDateOnlyEvent.AllDay ? 1 : 0;
            NewReminderIndex = Array.IndexOf(NewReminderRules, reminders.NewReminderRule);
            LoadExport();

            LabelStyleIndex = (int)settings.LabelStyle;

            CompactInSession = settings.CompactInSession;
            CompactPending = PendingText(settings.PendingCompactInSession == false, settings.PendingCompactFrom, "Compact rows off");
            HideShelfInSession = settings.HideShelfInSession;
            ShelfPending = PendingText(settings.PendingHideShelfInSession == false, settings.PendingHideShelfFrom, "The Shelf shown");
            ActionsIndex = SessionActions.ClampCap(settings.SessionActions) - SessionActions.MinCap;
            ActionsPending = PendingText(settings.PendingSessionActions is not null, settings.PendingFrom, $"{settings.PendingSessionActions} actions");
            SessionViewIndex = (int)settings.SessionView;
            SessionViewPending = PendingText(settings.PendingSessionView is not null, settings.PendingSessionViewFrom, $"{settings.PendingSessionView}");
            NextStepTakesSlot = settings.NextStepTakesSlot;
            StepAwayIndex = settings.StepAway == StepAwayTimer.PauseTimer ? 1 : 0;
            StepAwayPending = PendingText(settings.PendingStepAway is not null, settings.PendingStepAwayFrom, "Pause the timer");
            SideTaskIndex = settings.SideTask == StepAwayTimer.PauseTimer ? 1 : 0;
            SideTaskPending = PendingText(settings.PendingSideTask is not null, settings.PendingSideTaskFrom, "Pause the timer");

            int preset = Array.IndexOf(TimeLimitPresets, settings.DefaultTimeLimitMinutes);
            TimeLimitIndex = preset >= 0 && !_customTimeLimit ? preset : TimeLimitChoices.Count - 1;
            CustomTimeLimit = settings.DefaultTimeLimitMinutes > 0 ? settings.DefaultTimeLimitMinutes : TaskSettings.StandardTimeLimitMinutes;
            int focus = PomodoroFocusMinutes?.Invoke() ?? TaskSettings.StandardTimeLimitMinutes;
            TimeLimitHint = $"Minutes for a task typed in an add box (not a jotted one). Pomodoro focus is {focus} (Focus sessions › Pomodoro defaults).";
            SuggestShorterTitles = settings.SuggestShorterTitles;
            LoadMarkers(settings);
            LoadViews();

            PinHighlights = settings.PinHighlights;
            UntouchedIndex = TaskUntouched.ClampWeeks(settings.UntouchedWeeks) - TaskUntouched.MinWeeks;
            ShowAgeOnRows = settings.ShowAgeOnRows;
            TidyUpOn = settings.TidyUpOn;
            TidyUpDayIndex = Array.IndexOf(Days, settings.TidyUpDay);
            OnPropertyChanged(nameof(StartSessionText));
        }
        finally
        {
            _loading = false;
        }
    }

    // ---- loading ----

    private static string RingingSummaryOf(TaskReminderSettings reminders)
    {
        string dismiss = reminders.DismissProtect ? $"dismiss waits {reminders.DismissProtectSeconds} s" : "dismiss at once";
        string display = reminders.Display == TaskReminderDisplay.Toast ? "toast" : "full screen";
        return $"Volume {reminders.Volume}% · {(reminders.RisingVolume ? "rising" : "steady")} · {dismiss} · {display}";
    }

    private void LoadExport()
    {
        if (_export is null)
        {
            ExportStatus = "Reminders go to Google Calendar once the task list is in use.";
            ShowsAllow = false;
            PickedCalendarOn = false;
            return;
        }

        string? account = _export.ExportAccountName;
        if (account is null)
        {
            ExportStatus = "Connect a Google account first (Settings › Connections).";
            ShowsAllow = false;
        }
        else if (_export.Problem is { Length: > 0 } problem)
        {
            ExportStatus = problem;
            ShowsAllow = true;
        }
        else if (_export.NeedsConsent)
        {
            ExportStatus = "Google needs your OK once before reminders go to “" + TaskCalendarExportService.AppCalendarName + "”.";
            ShowsAllow = true;
        }
        else
        {
            ExportStatus = "Into “" + TaskCalendarExportService.AppCalendarName + "” in " + account + ". Allow again if Google stopped taking them.";
            ShowsAllow = true;
        }

        PickableCalendars = _export.PickableCalendars();
        string? picked = _account?.PickedCalendarId;
        if (picked is not null)
        {
            PickedCalendarOn = true;
            ChosenCalendar = PickableCalendars.FirstOrDefault(calendar => calendar.GoogleId == picked);
            PickedStatus = "Tasks with a due date go to “" + (ChosenCalendar?.Name ?? picked) + "”, on every device of this account.";
        }
        else
        {
            ChosenCalendar ??= PickableCalendars.FirstOrDefault();
            PickedStatus = PickableCalendars.Count == 0
                ? "No calendars read from the Google account yet; they show after the next calendar check."
                : "Pick a calendar, then Use it (Google asks to allow changing your calendars' events).";
        }
    }

    private void LoadMarkers(TaskSettings settings)
    {
        Markers.Clear();
        foreach (ProjectMarkerPattern marker in settings.CustomMarkers ?? [])
        {
            Markers.Add(new ProjectMarkerRowViewModel(marker, false, new AsyncRelayCommand(() => RemoveMarkerAsync(marker))));
        }

        foreach (ProjectMarkerPattern marker in settings.DeclinedMarkers ?? [])
        {
            Markers.Add(new ProjectMarkerRowViewModel(marker, true, new AsyncRelayCommand(() => _settings.ForgetDeclinedMarkerAsync(marker))));
        }

        HasMarkers = Markers.Count > 0;
    }

    private void LoadViews()
    {
        if (_views is null)
        {
            ViewChoices = [];
            DefaultView = null;
            DefaultViewHint = "Views come with the task list.";
            return;
        }

        ViewChoices = _views.Views.Select(view => new TaskDefaultViewChoice(view.Id, view.Name)).ToList();
        Guid current = _views.DefaultViewId;
        DefaultView = ViewChoices.FirstOrDefault(choice => choice.Id == current);
        DefaultViewHint = "The view the Shelf opens with, on every device of this account.";
    }

    // "Compact rows off at 4:15 PM (looser changes wait; not while a session runs)." Empty when nothing waits.
    private string PendingText(bool waiting, DateTimeOffset? from, string change)
    {
        if (!waiting || from is not DateTimeOffset at)
        {
            return string.Empty;
        }

        DateTime local = TimeZoneInfo.ConvertTime(at, _time.LocalTimeZone).DateTime;
        DateTime today = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).Date;
        string clock = local.Date == today
            ? local.ToString("h:mm tt", CultureInfo.InvariantCulture)
            : local.ToString("ddd M/d h:mm tt", CultureInfo.InvariantCulture);
        string wait = LooserWait?.Invoke() is TimeSpan length && length > TimeSpan.Zero ? $" (looser changes wait {Minutes(length)}" : " (looser changes wait";
        return $"{change} at {clock}{wait}; never while a session runs).";
    }

    private static string Minutes(TimeSpan length)
    {
        return length.TotalMinutes >= 120 && length.TotalMinutes % 60 == 0
            ? $"{length.TotalHours:0} h"
            : $"{Math.Round(length.TotalMinutes):0} min";
    }

    // ---- saving: Reminders ----

    partial void OnAddRemindersToIndexChanged(int value)
    {
        if (_loading || value < 0)
        {
            return;
        }

        TaskCalendarDestination destination = value == 1 ? TaskCalendarDestination.Nowhere : TaskCalendarDestination.GoogleCalendar;
        _ = _reminders.UpdateAsync(current => current with { NewRemindersTo = destination });
    }

    partial void OnDateOnlyIndexChanged(int value)
    {
        if (_loading || value < 0)
        {
            return;
        }

        TaskDateOnlyEvent kind = value == 1 ? TaskDateOnlyEvent.AllDay : TaskDateOnlyEvent.TimedAtNine;
        _ = _reminders.UpdateAsync(current => current with { DateOnlyEvent = kind });
    }

    partial void OnNewReminderIndexChanged(int value)
    {
        if (_loading || value < 0 || value >= NewReminderRules.Length)
        {
            return;
        }

        string rule = NewReminderRules[value];
        _ = _reminders.UpdateAsync(current => current with { NewReminderRule = rule });
    }

    partial void OnPickedCalendarOnChanged(bool value)
    {
        if (_loading || value || _account?.PickedCalendarId is null)
        {
            return;
        }

        // Off: the account's tasks go back to "AB Reminders" (the events made in the picked calendar are deleted).
        _ = TurnPickedCalendarOffAsync();
    }

    private async Task TurnPickedCalendarOffAsync()
    {
        if (_export is not null)
        {
            await _export.SetPickedCalendarAsync(null);
        }

        Load();
    }

    [RelayCommand(CanExecute = nameof(CanAllow))]
    private async Task AllowExportAsync()
    {
        if (_export is null || OpenBrowser is null)
        {
            return;
        }

        IsAllowing = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            string? problem = await _export.AllowExportAsync(OpenBrowser, timeout.Token);
            StatusText = problem ?? string.Empty;
        }
        finally
        {
            IsAllowing = false;
            Load();
        }
    }

    private bool CanAllow() => !IsAllowing;

    [RelayCommand(CanExecute = nameof(CanUsePicked))]
    private async Task UsePickedCalendarAsync()
    {
        if (_export is null || OpenBrowser is null || ChosenCalendar is not TaskPickableCalendar chosen)
        {
            return;
        }

        IsAllowing = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            string? problem = await _export.AllowPickedCalendarAsync(OpenBrowser, timeout.Token);
            if (problem is null)
            {
                await _export.SetPickedCalendarAsync(chosen.GoogleId);
                StatusText = string.Empty;
            }
            else
            {
                StatusText = problem;
            }
        }
        finally
        {
            IsAllowing = false;
            Load();
        }
    }

    private bool CanUsePicked() => !IsAllowing && ChosenCalendar is not null;

    [RelayCommand]
    private void OpenAlerts() => OpenSection?.Invoke(SettingsSection.AlertsAndOnScreen);

    // ---- saving: Labels ----

    [RelayCommand]
    private void ToggleLabels() => ShowsLabelsEditor = !ShowsLabelsEditor;

    [RelayCommand]
    private void ShowLabels() => ShowsLabelsEditor = true;

    partial void OnLabelStyleIndexChanged(int value)
    {
        if (_loading || !Enum.IsDefined((TaskLabelStyle)value))
        {
            return;
        }

        _ = _settings.SetLabelStyleAsync((TaskLabelStyle)value);
    }

    // ---- saving: During a session ----

    partial void OnCompactInSessionChanged(bool value)
    {
        if (!_loading)
        {
            _ = ApplyRuledAsync(() => _settings.SetCompactInSessionAsync(value));
        }
    }

    partial void OnHideShelfInSessionChanged(bool value)
    {
        if (!_loading)
        {
            _ = ApplyRuledAsync(() => _settings.SetHideShelfInSessionAsync(value));
        }
    }

    partial void OnActionsIndexChanged(int value)
    {
        if (!_loading && value >= 0 && value < ActionChoices.Count)
        {
            _ = ApplyRuledAsync(() => _settings.SetSessionActionsAsync(SessionActions.MinCap + value));
        }
    }

    partial void OnSessionViewIndexChanged(int value)
    {
        if (!_loading && Enum.IsDefined((SessionTaskView)value))
        {
            _ = ApplyRuledAsync(() => _settings.SetSessionViewAsync((SessionTaskView)value));
        }
    }

    partial void OnNextStepTakesSlotChanged(bool value)
    {
        if (!_loading)
        {
            _ = _settings.SetNextStepTakesSlotAsync(value);
        }
    }

    partial void OnStepAwayIndexChanged(int value)
    {
        if (!_loading && value is 0 or 1)
        {
            _ = ApplyRuledAsync(() => _settings.SetStepAwayAsync(value == 1 ? StepAwayTimer.PauseTimer : StepAwayTimer.KeepTimerRunning));
        }
    }

    partial void OnSideTaskIndexChanged(int value)
    {
        if (!_loading && value is 0 or 1)
        {
            _ = ApplyRuledAsync(() => _settings.SetSideTaskAsync(value == 1 ? StepAwayTimer.PauseTimer : StepAwayTimer.KeepTimerRunning));
        }
    }

    // A per-mode session setting: stricter at once, looser waits (shown under its row) or, during a session, is refused (said here).
    // The page reads the service again either way, so a looser pick shows the value in effect plus its wait.
    private async Task ApplyRuledAsync(Func<Task<bool>> change)
    {
        bool taken = await change();
        StatusText = taken ? string.Empty : RefusedText;
        Load();
    }

    [RelayCommand]
    private void OpenFocusSessions() => OpenSection?.Invoke(SettingsSection.FocusSessions);

    [RelayCommand]
    private void OpenYourDay() => OpenSection?.Invoke(SettingsSection.YourDay);

    // ---- saving: New tasks ----

    partial void OnTimeLimitIndexChanged(int value)
    {
        if (_loading || value < 0 || value >= TimeLimitChoices.Count)
        {
            return;
        }

        _customTimeLimit = value >= TimeLimitPresets.Length;
        if (_customTimeLimit)
        {
            // Custom shows the box with the minutes in effect; typing in it saves.
            return;
        }

        _ = _settings.SetDefaultTimeLimitAsync(TimeLimitPresets[value]);
    }

    partial void OnCustomTimeLimitChanged(decimal? value)
    {
        if (_loading || !IsCustomTimeLimit || value is not decimal minutes || minutes < 1)
        {
            return;
        }

        _ = _settings.SetDefaultTimeLimitAsync((int)minutes);
    }

    partial void OnSuggestShorterTitlesChanged(bool value)
    {
        if (!_loading)
        {
            _ = _settings.SetSuggestShorterTitlesAsync(value);
        }
    }

    [RelayCommand]
    private async Task AddMarkerAsync()
    {
        string open = NewMarkerOpen.Trim();
        string close = NewMarkerClose.Trim();
        if (close.Length == 0 || close.Length > 3 || open.Length > 3 || (open + close).Any(char.IsLetterOrDigit))
        {
            MarkerProblem = "A marker is up to 3 symbols before the name (or none) and 1 to 3 after it, no letters or digits: ⟦ ⟧, or › alone.";
            return;
        }

        var marker = new ProjectMarkerPattern(open, close);
        IReadOnlyList<ProjectMarkerPattern> taught = _settings.Current.CustomMarkers ?? [];
        MarkerProblem = string.Empty;
        NewMarkerOpen = NewMarkerClose = string.Empty;
        if (!taught.Contains(marker))
        {
            await _settings.SetCustomMarkersAsync([.. taught, marker]);
        }
    }

    private Task RemoveMarkerAsync(ProjectMarkerPattern marker)
    {
        IReadOnlyList<ProjectMarkerPattern> taught = _settings.Current.CustomMarkers ?? [];
        return _settings.SetCustomMarkersAsync(taught.Where(held => held != marker).ToList());
    }

    partial void OnDefaultViewChanged(TaskDefaultViewChoice? value)
    {
        if (_loading || value is null || _views is null || value.Id == _views.DefaultViewId)
        {
            return;
        }

        _ = _views.SetDefaultAsync(value.Id);
    }

    // ---- saving: Shelf ----

    partial void OnPinHighlightsChanged(bool value)
    {
        if (!_loading)
        {
            _ = _settings.SetPinHighlightsAsync(value);
        }
    }

    partial void OnUntouchedIndexChanged(int value)
    {
        if (!_loading && value >= 0 && value < UntouchedChoices.Count)
        {
            _ = _settings.SetUntouchedWeeksAsync(TaskUntouched.MinWeeks + value);
        }
    }

    partial void OnShowAgeOnRowsChanged(bool value)
    {
        if (!_loading)
        {
            _ = _settings.SetShowAgeOnRowsAsync(value);
        }
    }

    partial void OnTidyUpOnChanged(bool value)
    {
        if (!_loading)
        {
            _ = _settings.SetTidyUpAsync(value, _settings.Current.TidyUpDay);
        }
    }

    partial void OnTidyUpDayIndexChanged(int value)
    {
        if (!_loading && value >= 0 && value < Days.Length)
        {
            _ = _settings.SetTidyUpAsync(_settings.Current.TidyUpOn, Days[value]);
        }
    }

    // ---- Reset ----

    [RelayCommand]
    private void AskReset()
    {
        ResetNote = string.Empty;
        IsConfirmingReset = true;
    }

    [RelayCommand]
    private void CancelReset() => IsConfirmingReset = false;

    /// <summary>
    /// Puts this PC's Tasks settings back to their defaults (the active mode's copy, and the device-wide Reminders rows of this page),
    /// through the services: a default looser than now waits, and during a session it is refused. The account's default view and picked
    /// calendar, the taught markers and the labels stay.
    /// </summary>
    [RelayCommand]
    private async Task ResetAsync()
    {
        IsConfirmingReset = false;
        string? note = await _settings.ResetAsync();
        TaskReminderSettings defaults = TaskReminderSettings.Default;
        await _reminders.UpdateAsync(current => current with
        {
            NewRemindersTo = defaults.NewRemindersTo,
            DateOnlyEvent = defaults.DateOnlyEvent,
            NewReminderRule = defaults.NewReminderRule,
        });
        ResetNote = note ?? "Tasks settings are back to their defaults.";
        Load();
    }
}
