using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Focus mode (redesign phase 1): what the Today page shows while a session runs, and nothing else. The countdown,
/// the phase line, End session, the focus so far today, the current task in front with the checklist under it, and
/// Step away / Interrupted (an "Away · 4:12" badge and "I'm back" while away). Refreshed every second.
/// </summary>
public sealed partial class FocusViewModel : ObservableObject
{
    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly StepAwayService? _stepAway;
    private readonly DayBarViewModel? _day;
    private readonly Func<Task> _endSession;

    // What "+10 min" adds to the running focus phase.
    private static readonly TimeSpan ExtendBy = TimeSpan.FromMinutes(10);

    /// <param name="checklist">The running session's checklist (Live), shared with the rest of the Today page.</param>
    /// <param name="day">Today's bar, for the focus so far; null without one.</param>
    /// <param name="endSession">The app's end flow (wait and reason when the session needs them).</param>
    public FocusViewModel(
        SessionController sessions,
        ConfigurationService configuration,
        StepAwayService? stepAway,
        ChecklistViewModel? checklist,
        DayBarViewModel? day,
        Func<Task> endSession)
    {
        _sessions = sessions;
        _configuration = configuration;
        _stepAway = stepAway;
        _day = day;
        _endSession = endSession;
        Checklist = checklist;
        TimeLeftText = PhaseText = AwayText = NowText = CurrentTaskText = CurrentSubtaskText = FocusedTodayText = string.Empty;
        StepAwayText = "Step away";

        _sessions.StateChanged += (_, _) => Refresh();
        _sessions.Ticked += (_, _) => Refresh();
        if (checklist is not null)
        {
            checklist.PropertyChanged += (_, _) => RefreshTasks();
        }

        if (_day is not null)
        {
            _day.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DayBarViewModel.SummaryText))
                {
                    FocusedTodayText = _day.SummaryText;
                }
            };
        }

        Refresh();
    }

    private PlanFirstService? _planFirst;

    /// <summary>Plan first: "Planning · 4:12 left" and "I'm ready" while it lasts. Set by the app.</summary>
    public PlanFirstService? PlanFirst
    {
        get => _planFirst;
        set
        {
            _planFirst = value;
            if (value is not null)
            {
                value.Changed += (_, _) => RefreshPlanning();
            }

            RefreshPlanning();
        }
    }

    /// <summary>A session's planning stage, or a first session's learning: unknown sites aren't asked about yet.</summary>
    [ObservableProperty]
    public partial bool IsPlanning { get; set; }

    /// <summary>The planning stage before the focus countdown (not learning): the timer and the bar show in the planning colour.</summary>
    [ObservableProperty]
    public partial bool InPlanningStage { get; set; }

    /// <summary>"I'm ready" shows: planning, not a first session's learning (which only the session's end ends).</summary>
    [ObservableProperty]
    public partial bool CanReady { get; set; }

    /// <summary>A fresh install's first session: it learns instead of planning, so ticking a task doesn't begin anything.</summary>
    [ObservableProperty]
    public partial bool IsLearning { get; set; }

    /// <summary>"or tick the first task to begin": in the planning stage while nobody is away (ticking the first task ends planning like "I'm ready").</summary>
    [ObservableProperty]
    public partial bool ShowsTickHint { get; set; }

    /// <summary>E.g. "5:40 left to plan · nothing will nag you yet".</summary>
    [ObservableProperty]
    public partial string PlanningText { get; set; } = string.Empty;

    /// <summary>The numeral at the planning row's right in the planning stage: the focus length that follows it ("50:00"); empty with no set end.</summary>
    [ObservableProperty]
    public partial string FocusLengthText { get; set; } = string.Empty;

    [RelayCommand]
    private void Ready()
    {
        _planFirst?.Ready();
    }

    private void RefreshPlanning()
    {
        IsPlanning = _planFirst is { IsPlanning: true };
        InPlanningStage = _planFirst is { InStage: true };
        IsLearning = _planFirst is { IsLearning: true };
        CanReady = InPlanningStage;
        RefreshTickHint();
        PlanningText = !IsPlanning ? string.Empty
            : IsLearning ? "First session · nothing new is asked; sort what you used at the end"
            : _planFirst!.IsTimeUp ? "Planning time is up · start when you're ready"
            : $"{_planFirst.LeftText} left to plan · nothing will nag you yet";
    }

    // Ticking the first task ends planning like "I'm ready", which the engine refuses while away; so the hint waits for them to be back.
    private void RefreshTickHint()
    {
        ShowsTickHint = InPlanningStage && !_sessions.State.IsAway;
    }

    /// <summary>The running session's checklist; null when the app doesn't offer guided sessions.</summary>
    public ChecklistViewModel? Checklist { get; }

    /// <summary>Hide or show the on-screen timer: an item in the ⋯ menu (set by the app); null hides it.</summary>
    public TimerToggleViewModel? TimerToggle { get; set; }

    /// <summary>The capture line (redesign phase 5): a note for this session, or a come-back with ⚑ or "!"; null hides it.</summary>
    public CaptureBoxViewModel? Capture { get; set; }

    public bool HasStepAway => _stepAway is not null;

    /// <summary>True while a session runs (the page shows this instead of the start card).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EndSessionCommand))]
    public partial bool IsActive { get; set; }

    /// <summary>The end button: "End test" for a test session (it ends at once), else "End session…".</summary>
    [ObservableProperty]
    public partial string EndSessionText { get; set; } = "End session…";

    /// <summary>"41:12" left, or the time in for a session with no set end; frozen while away.</summary>
    [ObservableProperty]
    public partial string TimeLeftText { get; set; }

    /// <summary>"left · Coding · until 1:17 PM", "Break · until 1:22 PM" or "so far · Coding · since 9:00 AM".</summary>
    [ObservableProperty]
    public partial string PhaseText { get; set; }

    /// <summary>E.g. "1h 10m focused · 9h 00m of work planned" (the day bar's summary).</summary>
    [ObservableProperty]
    public partial string FocusedTodayText { get; set; }

    /// <summary>"Phone: not picked up · down 34m", from the phone's sync file (ADR 26); empty when none came.</summary>
    [ObservableProperty]
    public partial string PhoneText { get; set; } = string.Empty;

    /// <summary>The phone's line for the running session; set by the app.</summary>
    public Func<string?>? PhoneLine { get; set; }

    /// <summary>The running card's chip: "Focus · 1 of 3", "Focus" without tasks, "Break" or "Test session".</summary>
    [ObservableProperty]
    public partial string ChipText { get; set; } = string.Empty;

    /// <summary>The running card's line beside the chip: "Coding profile · ends 2:20 PM" (or "since 9:00 AM" with no set end).</summary>
    [ObservableProperty]
    public partial string SessionLineText { get; set; } = string.Empty;

    /// <summary>"Now · 1 of 3 done"; empty without tasks.</summary>
    [ObservableProperty]
    public partial string NowText { get; set; }

    [ObservableProperty]
    public partial string CurrentTaskText { get; set; }

    /// <summary>The running card's title: the task in front, or the profile's name when the session has no tasks.</summary>
    [ObservableProperty]
    public partial string TitleText { get; set; } = string.Empty;

    /// <summary>"+10 min" is offered: a timed manual session that isn't a test (Pomodoro and "No set end" never show it).</summary>
    [ObservableProperty]
    public partial bool ShowsExtend { get; set; }

    /// <summary>"+10 min" works right now (not while away; never in planning or a break).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExtendFocusCommand))]
    public partial bool CanExtend { get; set; }

    [ObservableProperty]
    public partial string CurrentSubtaskText { get; set; }

    public bool HasTasks => Checklist is { HasTasks: true };

    public bool HasSubtask => CurrentSubtaskText.Length > 0;

    [ObservableProperty]
    public partial bool IsAway { get; set; }

    /// <summary>"Away · 4:12" while away, "Side task · 6:12" (in amber) during a side task.</summary>
    [ObservableProperty]
    public partial string AwayText { get; set; }

    /// <summary>A side task is running: its time shows in amber next to the paused focus time.</summary>
    [ObservableProperty]
    public partial bool IsSideTask { get; set; }

    /// <summary>Stepped away (not on a side task): the plain badge.</summary>
    [ObservableProperty]
    public partial bool IsPlainAway { get; set; }

    /// <summary>"Step away", or "I'm back" while away.</summary>
    [ObservableProperty]
    public partial string StepAwayText { get; set; }

    /// <summary>Step away (a break), or back while away.</summary>
    [RelayCommand]
    private void StepAway()
    {
        _stepAway?.Toggle();
        Refresh();
    }

    /// <summary>"+10 min": the running timed focus phase ends 10 minutes later, at once (the stricter direction; see the engine's ExtendFocus).</summary>
    [RelayCommand(CanExecute = nameof(CanExtend))]
    private void ExtendFocus()
    {
        _sessions.ExtendFocus(ExtendBy);
        Refresh();
    }

    /// <summary>Someone at the desk or a call: the other flavour of Step away.</summary>
    [RelayCommand]
    private void Interrupted()
    {
        if (!IsAway)
        {
            _stepAway?.StepAway(AwayKind.Interrupted);
            Refresh();
        }
    }

    /// <summary>A small errand at the desk: asks for 5, 10 or 15 minutes; the focus clock pauses, enforcement keeps running.</summary>
    [RelayCommand]
    private void SideTask()
    {
        _stepAway?.AskSideTaskLength();
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(IsActive))]
    private Task EndSessionAsync()
    {
        return _endSession();
    }

    /// <summary>Reads the session again (also every second).</summary>
    public void Refresh()
    {
        SessionState state = _sessions.State;
        IsActive = state.IsActive;
        EndSessionText = state.IsTest ? "End test" : "End session…";
        if (!state.IsActive)
        {
            TimeLeftText = PhaseText = AwayText = SessionLineText = string.Empty;
            IsAway = false;
            IsSideTask = IsPlainAway = false;
            StepAwayText = "Step away";
            FocusLengthText = string.Empty;
            ShowsExtend = CanExtend = false;
            RefreshTickHint();
            RefreshTasks();
            return;
        }

        DateTimeOffset now = _sessions.Now;
        DateTimeOffset clock = state.CountdownTime(now);
        string profile = ProfileOptions.NameOf(_configuration.Current.Profiles, state.ProfileId) ?? "Focus";
        string who = state.IsTest ? "Test session" : state.IsPlanning ? $"Planning · {profile}" : state.IsEnforcing ? profile : "Break";
        TimeZoneInfo zone = _sessions.TimeProvider.LocalTimeZone;
        string what = state.IsTest ? "Test session" : state.IsPlanning ? $"Planning · {profile}" : state.IsEnforcing ? $"{profile} profile" : "Break";
        string awayText = _stepAway?.AwayText ?? string.Empty;
        SessionLineText = state.IsAway && awayText.Length > 0
            ? $"{awayText} · {profile} profile"
            : state.PhaseEndsAt is DateTimeOffset phaseEnd
                ? $"{what} · ends {TimeZoneInfo.ConvertTime(phaseEnd, zone).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)}"
                : $"{what} · since {TimeZoneInfo.ConvertTime(state.PhaseStartedAt ?? now, zone).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)}";
        if (state.PhaseEndsAt is DateTimeOffset end)
        {
            TimeLeftText = SessionStatusFormatter.FormatDuration(end - clock);
            PhaseText = state.IsPlanning
                ? $"left · {who} · not focus time"
                : $"left · {who} · until {TimeZoneInfo.ConvertTime(end, zone):t}";
        }
        else
        {
            DateTimeOffset since = state.PhaseStartedAt ?? now;
            TimeLeftText = SessionStatusFormatter.FormatDuration(clock - since);
            PhaseText = $"so far · {who} · since {TimeZoneInfo.ConvertTime(since, zone):t}";
        }

        FocusLengthText = state.IsPlanning && state.FocusLengthAfterPlanning is TimeSpan focusLength
            ? SessionStatusFormatter.FormatDuration(focusLength)
            : string.Empty;
        ShowsExtend = state.OffersExtendFocus;
        CanExtend = state.CanExtendFocus;
        RefreshTickHint();

        IsAway = state.IsAway;
        IsSideTask = state.Away == AwayKind.SideTask;
        IsPlainAway = state.IsAway && !IsSideTask;
        AwayText = awayText;
        StepAwayText = state.IsAway ? "I'm back" : "Step away";
        FocusedTodayText = _day?.SummaryText ?? string.Empty;
        PhoneText = PhoneLine?.Invoke() ?? string.Empty;
        RefreshTasks();
    }

    // "Focus · 2 of 3": the task in front among all of them, done ones first (ProgressText is "done/total",
    // CurrentTaskNumber counts the open tasks only, so the done ones come before it).
    private void RefreshChip(ChecklistViewModel? checklist)
    {
        SessionState state = _sessions.State;
        string label = state.IsTest ? "Test session"
            : state.Away == AwayKind.SideTask ? "Side task"
            : state.Away == AwayKind.Interrupted ? "Interrupted"
            : state.IsAway ? "Stepped away"
            : state.IsEnforcing ? "Focus" : "Break";
        string[] counts = checklist?.ProgressText.Split('/') ?? [];
        bool hasCurrentTask = label == "Focus" && checklist is { CurrentTaskNumber: > 0 } && counts.Length == 2
            && int.TryParse(counts[0], out int done) && int.TryParse(counts[1], out int total) && done < total;
        ChipText = hasCurrentTask ? $"{label} · {int.Parse(counts[0]) + checklist!.CurrentTaskNumber} of {counts[1]}" : label;
    }

    private void RefreshTasks()
    {
        ChecklistViewModel? checklist = Checklist;
        CurrentTaskText = checklist is { CurrentTaskNumber: > 0 }
            ? $"{checklist.CurrentTaskNumber}. {checklist.CurrentTaskText}"
            : checklist?.CurrentTaskText ?? string.Empty;
        CurrentSubtaskText = checklist?.CurrentSubtaskText ?? string.Empty;
        TitleText = checklist is { HasTasks: true } && checklist.CurrentTaskText.Length > 0
            ? checklist.CurrentTaskText
            : (ProfileOptions.NameOf(_configuration.Current.Profiles, _sessions.State.ProfileId) ?? "Focus");
        NowText = checklist is { HasTasks: true } ? $"Now · {checklist.ProgressText.Replace("/", " of ")} done" : string.Empty;
        RefreshChip(checklist);
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(HasSubtask));
    }
}
