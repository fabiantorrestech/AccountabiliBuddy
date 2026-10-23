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

    /// <summary>A session's planning minutes: unknown sites aren't asked about yet.</summary>
    [ObservableProperty]
    public partial bool IsPlanning { get; set; }

    /// <summary>"I'm ready" shows: planning, not a first session's learning (which only the session's end ends).</summary>
    [ObservableProperty]
    public partial bool CanReady { get; set; }

    /// <summary>E.g. "Planning · 4:12 left · new sites are asked about after".</summary>
    [ObservableProperty]
    public partial string PlanningText { get; set; } = string.Empty;

    [RelayCommand]
    private void Ready()
    {
        _planFirst?.Ready();
    }

    private void RefreshPlanning()
    {
        IsPlanning = _planFirst is { IsPlanning: true };
        bool learning = _planFirst is { IsLearning: true };
        CanReady = IsPlanning && !learning;
        PlanningText = !IsPlanning ? string.Empty
            : learning ? "First session: nothing new is asked · sort what you used on Today at the end"
            : $"Planning · {_planFirst!.LeftText} left · new sites are asked about after";
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

    /// <summary>"41:12" left, or the time in for a session with no set end; frozen while away.</summary>
    [ObservableProperty]
    public partial string TimeLeftText { get; set; }

    /// <summary>"left · Coding · until 1:17 PM", "Break · until 1:22 PM" or "so far · Coding · since 9:00 AM".</summary>
    [ObservableProperty]
    public partial string PhaseText { get; set; }

    /// <summary>E.g. "1h 10m focused · 9h 00m of work planned" (the day bar's summary).</summary>
    [ObservableProperty]
    public partial string FocusedTodayText { get; set; }

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

    /// <summary>"Away · 4:12" while away.</summary>
    [ObservableProperty]
    public partial string AwayText { get; set; }

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
        if (!state.IsActive)
        {
            TimeLeftText = PhaseText = AwayText = string.Empty;
            IsAway = false;
            StepAwayText = "Step away";
            RefreshTasks();
            return;
        }

        DateTimeOffset now = _sessions.Now;
        DateTimeOffset clock = state.CountdownTime(now);
        string profile = ProfileOptions.NameOf(_configuration.Current.Profiles, state.ProfileId) ?? "Focus";
        string who = state.IsEnforcing ? profile : "Break";
        TimeZoneInfo zone = _sessions.TimeProvider.LocalTimeZone;
        if (state.PhaseEndsAt is DateTimeOffset end)
        {
            TimeLeftText = SessionStatusFormatter.FormatDuration(end - clock);
            PhaseText = $"left · {who} · until {TimeZoneInfo.ConvertTime(end, zone):t}";
        }
        else
        {
            DateTimeOffset since = state.PhaseStartedAt ?? now;
            TimeLeftText = SessionStatusFormatter.FormatDuration(clock - since);
            PhaseText = $"so far · {who} · since {TimeZoneInfo.ConvertTime(since, zone):t}";
        }

        IsAway = state.IsAway;
        AwayText = _stepAway?.AwayText ?? string.Empty;
        StepAwayText = state.IsAway ? "I'm back" : "Step away";
        FocusedTodayText = _day?.SummaryText ?? string.Empty;
        RefreshTasks();
    }

    private void RefreshTasks()
    {
        ChecklistViewModel? checklist = Checklist;
        CurrentTaskText = checklist?.CurrentTaskText ?? string.Empty;
        CurrentSubtaskText = checklist?.CurrentSubtaskText ?? string.Empty;
        NowText = checklist is { HasTasks: true } ? $"Now · {checklist.ProgressText.Replace("/", " of ")} done" : string.Empty;
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(HasSubtask));
    }
}
