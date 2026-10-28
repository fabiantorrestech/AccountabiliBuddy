using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Session tab. Idle: the same start controls as the quick-session popup (profile, length, optional break,
/// Start), sharing its remembered choices, plus Pomodoro and "until I end it" under "More ways to start".
/// With no profiles yet it offers the example profile. Running: the status with countdown, End session, the
/// on-screen timer toggle, and Unlock folded away.
/// </summary>
public sealed partial class SessionViewModel : ObservableObject
{
    /// <summary>The quiet "New since your last version" card (migration assistant); set by the app.</summary>
    [ObservableProperty]
    public partial WhatsNewCardViewModel? WhatsNew { get; set; }

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly SessionEndFlow _endFlow;
    private readonly Func<Window?> _ownerWindow;
    private readonly QuickSessionService _quickSession;

    public SessionViewModel(
        SessionController sessions,
        ConfigurationService configuration,
        SessionEndFlow endFlow,
        Func<Window?> ownerWindow,
        UnlockViewModel unlock,
        QuickSessionService quickSession)
    {
        Unlock = unlock;
        _sessions = sessions;
        _configuration = configuration;
        _endFlow = endFlow;
        _ownerWindow = ownerWindow;
        _quickSession = quickSession;

        PomodoroCycles = "4";
        StatusTitle = string.Empty;
        StatusDetail = string.Empty;
        PomodoroSummary = string.Empty;
        QuickStart = _quickSession.CreateStartControls();

        _sessions.StateChanged += (_, state) =>
        {
            RefreshStatus();
            if (!state.IsActive)
            {
                // Fresh controls with the choices just remembered.
                RenewStartControls();
            }
        };
        _sessions.Ticked += (_, _) => RefreshStatus();
        Tasks = _quickSession.Tasks is { } tasks ? new ChecklistViewModel(tasks, ChecklistMode.Live, _sessions.TimeProvider) : null;
        _configuration.Changed += (_, document) =>
        {
            RefreshFromConfiguration(document);
            RenewStartControls();
        };

        RefreshFromConfiguration(_configuration.Current);
        RefreshStatus();
    }

    /// <summary>Raised when the user asks to see the Profiles tab.</summary>
    public event EventHandler? OpenProfilesRequested;

    /// <summary>The start controls (shared with the quick-session popup).</summary>
    [ObservableProperty]
    public partial QuickSessionViewModel QuickStart { get; set; }

    /// <summary>Today's lunch card (flexible break); set by the app.</summary>
    [ObservableProperty]
    public partial LunchCardViewModel? Lunch { get; set; }

    /// <summary>"Day off on this computer · Undo" after the Pre-Work window's day off.</summary>
    [ObservableProperty]
    public partial DayOffCardViewModel? DayOff { get; set; }

    /// <summary>The Unlock card.</summary>
    public UnlockViewModel Unlock { get; }

    /// <summary>"Come back to": ideas parked with Ctrl+Alt+N or the work-hours gate (set by the app).</summary>
    public ComebacksViewModel? Comebacks { get; set; }

    /// <summary>Timers and alarms on this PC (set by the app); they keep running during a session.</summary>
    public TimersViewModel? Timers { get; set; }

    /// <summary>The noise player, a card in the side column under Timers &amp; lunch; null hides it (set by the app).</summary>
    public NoisePlayerViewModel? NoisePlayer { get; set; }

    /// <summary>The shutdown card after the day's last work part (redesign phase 5); null hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsShutdown), nameof(CardState))]
    public partial ShutdownCardViewModel? Shutdown { get; set; }

    /// <summary>The shutdown card takes the main card's place, unless a session runs (started from a hotkey, the tray or the popup).</summary>
    public bool ShowsShutdown => Shutdown is not null && !IsSessionActive;

    /// <summary>Today's deep work against the daily goal (redesign phase 4); null hides the card.</summary>
    public GoalCardViewModel? Goal { get; set; }

    /// <summary>The "This is week N of M" switch (ADR 47); shows only while the weeks repeat in a cycle of more than one.</summary>
    public WeekCardViewModel? Week { get; set; }

    /// <summary>The user's Beeminder goals (ADR 43, read only); null hides the card, as does having no token.</summary>
    public BeeminderViewModel? Beeminder { get; set; }

    /// <summary>The review card after a session (sites and apps with no rule); null when there's none.</summary>
    [ObservableProperty]
    public partial SessionReviewViewModel? Review { get; set; }

    /// <summary>Focus mode: what the page shows while a session runs (set by the app).</summary>
    public FocusViewModel? Focus
    {
        get => _focus;
        set
        {
            if (_focus is not null)
            {
                _focus.PropertyChanged -= OnFocusPropertyChanged;
            }

            _focus = value;
            if (_focus is not null)
            {
                _focus.PropertyChanged += OnFocusPropertyChanged;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(CardState));
        }
    }

    private FocusViewModel? _focus;

    /// <summary>What the main card shows now; the page scrolls back to its top when it changes.</summary>
    public TodayCardState CardState => ShowsShutdown ? TodayCardState.Shutdown
        : ShowsWelcome ? TodayCardState.Welcome
        : !IsSessionActive ? TodayCardState.Idle
        : Focus is { IsPlanning: true } ? TodayCardState.Planning
        : TodayCardState.Running;

    private void OnFocusPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FocusViewModel.IsPlanning))
        {
            OnPropertyChanged(nameof(CardState));
        }
    }

    /// <summary>Hide or show the on-screen timer for the running session.</summary>
    public TimerToggleViewModel? TimerToggle { get; init; }

    /// <summary>Today's plan, for the "next scheduled" line.</summary>
    public DayBarViewModel? Day
    {
        get => _day;
        init
        {
            _day = value;
            if (_day is not null)
            {
                _day.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(DayBarViewModel.StatusText))
                    {
                        OnPropertyChanged(nameof(IdleNextText));
                    }
                };
            }
        }
    }

    private readonly DayBarViewModel? _day;

    /// <summary>The line under "Nothing running.": the next block as the day bar's status says it, e.g. "Deep work at 3:00 PM."; empty without one.</summary>
    public string IdleNextText
    {
        get
        {
            string text = Day?.StatusText ?? string.Empty;
            const string next = "Next: ";
            if (text.StartsWith(next, StringComparison.Ordinal))
            {
                text = text[next.Length..];
            }

            return text.Length == 0 || text.EndsWith('.') ? text : text + ".";
        }
    }

    /// <summary>Today's "Next up" card: the next two plan parts (set by the app).</summary>
    public NextUpViewModel? NextUp { get; set; }

    /// <summary>"Seen while planning": the sites and apps with no rule noticed during the planning stage (set by the app).</summary>
    public SeenWhilePlanningViewModel? SeenWhilePlanning { get; set; }

    /// <summary>The count on the "More today" fold's header (set by the app, after the cards inside are).</summary>
    public MoreTodayViewModel? MoreToday { get; set; }

    /// <summary>Which Cold Turkey blocks are on.</summary>
    public ColdTurkeyStatusViewModel? ColdTurkey { get; init; }

    /// <summary>E.g. "Tip: press Ctrl+Alt+F anywhere to start a session from any app."</summary>
    public string HotkeyTip => _quickSession.HotkeyError is null
        ? $"Tip: press {_quickSession.Preferences.Hotkey} anywhere to start a session from any app."
        : string.Empty;

    /// <summary>Blank = runs until stopped.</summary>
    [ObservableProperty]
    public partial string PomodoroCycles { get; set; }

    [ObservableProperty]
    public partial string StatusTitle { get; set; }

    [ObservableProperty]
    public partial string StatusDetail { get; set; }

    /// <summary>The end button: "End test" for a test session, else "End session…".</summary>
    [ObservableProperty]
    public partial string EndSessionText { get; set; } = "End session…";

    [ObservableProperty]
    public partial string PomodoroSummary { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStart), nameof(ShowsWelcome), nameof(CardState))]
    public partial bool HasNoProfiles { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStart), nameof(ShowsWelcome), nameof(ShowsShutdown), nameof(CardState))]
    [NotifyCanExecuteChangedFor(nameof(EndSessionCommand))]
    public partial bool IsSessionActive { get; set; }

    /// <summary>The start controls show when nothing runs and there is a profile to start with.</summary>
    public bool ShowsStart => !IsSessionActive && !HasNoProfiles;

    /// <summary>True when nothing runs and there are no profiles yet: the welcome card shows instead.</summary>
    public bool ShowsWelcome => !IsSessionActive && HasNoProfiles;

    [RelayCommand]
    private void StartPomodoro()
    {
        if (QuickStart.SelectedProfile is not { } profile || IsSessionActive)
        {
            return;
        }

        if (!TryParseOptionalPositive(PomodoroCycles, out int? cycles))
        {
            ErrorMessage = "Cycles must be a whole number above 0, or blank to run until you stop it.";
            return;
        }

        ErrorMessage = null;
        _sessions.StartPomodoro(profile.Id, cycles);
    }

    /// <summary>A session with no set end: it runs until ended, which takes no wait or reason.</summary>
    [RelayCommand]
    private void StartOpenEnded()
    {
        if (QuickStart.SelectedProfile is not { } profile || IsSessionActive)
        {
            return;
        }

        ErrorMessage = null;
        _sessions.StartManual(profile.Id, null);
    }

    [RelayCommand(CanExecute = nameof(IsSessionActive))]
    private async Task EndSessionAsync()
    {
        await _endFlow.TryEndSessionAsync(_ownerWindow());
    }

    /// <summary>Adds the example "Coding" profile (blocks common distracting sites), ready to start with.</summary>
    [RelayCommand]
    private async Task AddExampleProfileAsync()
    {
        FocusProfile example = ExampleProfiles.Coding();
        await _configuration.UpdateAsync(document => document with { Profiles = document.Profiles.Append(example).ToList() });
        ErrorMessage = _configuration.IsReadOnly ? "Not added: settings are read-only (see banner)." : null;
    }

    [RelayCommand]
    private void OpenProfiles()
    {
        OpenProfilesRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RenewStartControls()
    {
        QuickSessionViewModel old = QuickStart;
        QuickStart = _quickSession.CreateStartControls();
        old.Dispose();
    }

    private void RefreshFromConfiguration(ProfileDocument document)
    {
        HasNoProfiles = document.Profiles.Count == 0;

        PomodoroSettings pomodoro = document.PomodoroDefaults;
        PomodoroSummary =
            $"{pomodoro.FocusLength.TotalMinutes:0} min focus · {pomodoro.ShortBreakLength.TotalMinutes:0} min break · " +
            $"{pomodoro.LongBreakLength.TotalMinutes:0} min long break every {pomodoro.CyclesBeforeLongBreak} cycles";
    }

    /// <summary>Guided sessions: the running session's checklist (null when the app doesn't offer it).</summary>
    public ChecklistViewModel? Tasks { get; }

    private void RefreshStatus()
    {
        Tasks?.RefreshEndAtOnce();
        SessionState state = _sessions.State;
        string? profileName = ProfileOptions.NameOf(_configuration.Current.Profiles, state.ProfileId);

        IsSessionActive = state.IsActive;
        EndSessionText = state.IsTest ? "End test" : "End session…";
        StatusTitle = SessionStatusFormatter.Title(state, profileName);
        StatusDetail = SessionStatusFormatter.Detail(state, _sessions.Now);
    }

    private static bool TryParseOptionalPositive(string text, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (int.TryParse(text.Trim(), out int parsed) && parsed > 0)
        {
            value = parsed;
            return true;
        }

        return false;
    }

    /// <summary>Opens "Plan a day" (ADR 37); set by the app.</summary>
    public Action? ShowPlanDay { get; set; }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void PlanDay() => ShowPlanDay?.Invoke();
}
