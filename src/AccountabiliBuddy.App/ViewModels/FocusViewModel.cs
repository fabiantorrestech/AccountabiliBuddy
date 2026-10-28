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

    /// <summary>E.g. "Planning · 4:12 left · new sites are asked about after".</summary>
    [ObservableProperty]
    public partial string PlanningText { get; set; } = string.Empty;

    /// <summary>The part of the planning stage still to go, 0 to 100 (empty once its time is up).</summary>
    [ObservableProperty]
    public partial double PlanningLeftPercent { get; set; }

    [RelayCommand]
    private void Ready()
    {
        _planFirst?.Ready();
    }

    private void RefreshPlanning()
    {
        IsPlanning = _planFirst is { IsPlanning: true };
        InPlanningStage = _planFirst is { InStage: true };
        bool learning = _planFirst is { IsLearning: true };
        CanReady = InPlanningStage;
        PlanningText = !IsPlanning ? string.Empty
            : learning ? "First session: nothing new is asked · sort what you used on Today at the end"
            : _planFirst!.IsTimeUp ? "Planning time is up · Start when you are ready · new sites are asked about after"
            : $"Planning · {_planFirst.LeftText} left · new sites are asked about after";
        PlanningLeftPercent = InPlanningStage ? PlanningLeft(_sessions.State, _sessions.Now) * 100 : 0;
    }

    // The planning stage's own bar: what is left of the stage's start-to-end span.
    private static double PlanningLeft(SessionState state, DateTimeOffset now)
    {
        if (state.PhaseStartedAt is not DateTimeOffset start || state.PhaseEndsAt is not DateTimeOffset end || end <= start)
        {
            return 0;
        }

        return Math.Clamp((end - state.CountdownTime(now)) / (end - start), 0, 1);
    }

    /// <summary>The running session's checklist; null when the app doesn't offer guided sessions.</summary>
    public ChecklistViewModel? Checklist { get; }

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
            RefreshTasks();
            return;
        }

        DateTimeOffset now = _sessions.Now;
        DateTimeOffset clock = state.CountdownTime(now);
        string profile = ProfileOptions.NameOf(_configuration.Current.Profiles, state.ProfileId) ?? "Focus";
        string who = state.IsTest ? "Test session" : state.IsPlanning ? $"Planning · {profile}" : state.IsEnforcing ? profile : "Break";
        TimeZoneInfo zone = _sessions.TimeProvider.LocalTimeZone;
        string what = state.IsTest ? "Test session" : state.IsPlanning ? $"Planning · {profile}" : state.IsEnforcing ? $"{profile} profile" : "Break";
        SessionLineText = state.PhaseEndsAt is DateTimeOffset phaseEnd
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

        IsAway = state.IsAway;
        IsSideTask = state.Away == AwayKind.SideTask;
        IsPlainAway = state.IsAway && !IsSideTask;
        AwayText = _stepAway?.AwayText ?? string.Empty;
        StepAwayText = state.IsAway ? "I'm back" : "Step away";
        FocusedTodayText = _day?.SummaryText ?? string.Empty;
        PhoneText = PhoneLine?.Invoke() ?? string.Empty;
        RefreshTasks();
    }

    // "Focus · 1 of 3": the task in front among all of them (ProgressText is "done/total").
    private void RefreshChip(ChecklistViewModel? checklist)
    {
        SessionState state = _sessions.State;
        string label = state.IsTest ? "Test session" : state.IsEnforcing ? "Focus" : "Break";
        string[] counts = checklist?.ProgressText.Split('/') ?? [];
        bool hasOpenTask = counts.Length == 2 && int.TryParse(counts[0], out int done) && int.TryParse(counts[1], out int total) && done < total;
        ChipText = hasOpenTask ? $"{label} · {int.Parse(counts[0]) + 1} of {counts[1]}" : label;
    }

    private void RefreshTasks()
    {
        ChecklistViewModel? checklist = Checklist;
        CurrentTaskText = checklist is { CurrentTaskNumber: > 0 }
            ? $"{checklist.CurrentTaskNumber}. {checklist.CurrentTaskText}"
            : checklist?.CurrentTaskText ?? string.Empty;
        CurrentSubtaskText = checklist?.CurrentSubtaskText ?? string.Empty;
        NowText = checklist is { HasTasks: true } ? $"Now · {checklist.ProgressText.Replace("/", " of ")} done" : string.Empty;
        RefreshChip(checklist);
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(HasSubtask));
    }
}
