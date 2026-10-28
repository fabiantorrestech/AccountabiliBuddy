using Avalonia.Threading;
using AccountabiliBuddy.App.DevTools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Root of the main window: one view model per page, the title bar's sync light, the day bar, and banners for load errors and a changed clock.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(
        SessionViewModel session,
        ProfilesViewModel profiles,
        DayPlanViewModel dayPlan,
        ExceptionsViewModel exceptions,
        HistoryViewModel history,
        SetupViewModel setup,
        IntegrationsViewModel integrations,
        SettingsViewModel settings,
        ActivityDebugViewModel activity,
        bool showDevTools,
        string? loadError)
    {
        Session = session;
        Profiles = profiles;
        DayPlan = dayPlan;
        Exceptions = exceptions;
        History = history;
        Setup = setup;
        Integrations = integrations;
        Settings = settings;
        Activity = activity;
        ShowDevTools = showDevTools;
        LoadError = loadError;
        session.OpenProfilesRequested += (_, _) => OpenSettings(SettingsSection.Guards);
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionViewModel.IsSessionActive))
            {
                OnSessionActiveChanged(session.IsSessionActive);
            }
        };
        // The Setup DP Assistant is locked with the editor: its button follows the Day plan's lock.
        dayPlan.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DayPlanViewModel.IsLocked))
            {
                OpenDpAssistantCommand.NotifyCanExecuteChanged();
            }
        };
        // "N resting": the guard areas that are view-only right now.
        profiles.PropertyChanged += OnGuardLockChanged;
        dayPlan.PropertyChanged += OnGuardLockChanged;
        setup.PropertyChanged += OnGuardLockChanged;
        settings.PropertyChanged += OnGuardLockChanged;
        _sessionActive = session.IsSessionActive;
        RefreshFocusMode();
    }

    private bool _sessionActive;

    /// <summary>
    /// A focus session is running. Every lock stays where it was (Profiles and Day plan carry their own banners, Settings'
    /// guard parts are view-only); nothing is hidden from the rail any more.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFocusMode { get; set; }

    /// <summary>True while a session runs: the rail then shows how many guard areas are resting.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsResting))]
    public partial bool IsSessionRunning { get; set; }

    private void OnSessionActiveChanged(bool active)
    {
        _sessionActive = active;
        RefreshFocusMode();
    }

    private void RefreshFocusMode()
    {
        IsSessionRunning = _sessionActive;
        IsFocusMode = _sessionActive;
        OnPropertyChanged(nameof(RestingCount));
        OnPropertyChanged(nameof(RestingText));
        OnPropertyChanged(nameof(ShowsResting));
    }

    private void OnGuardLockChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProfilesViewModel.IsLocked))
        {
            OnPropertyChanged(nameof(RestingCount));
            OnPropertyChanged(nameof(RestingText));
            OnPropertyChanged(nameof(ShowsResting));
        }
    }

    /// <summary>How many guard areas are locked now: Profiles, Day plan, Setup's revokes and the guard settings.</summary>
    public int RestingCount => new[] { Profiles.IsLocked, DayPlan.IsLocked, Setup.IsLocked, Settings.IsLocked }.Count(locked => locked);

    public string RestingText => $"{RestingCount} resting";

    /// <summary>The rail's "N resting" shows only while a session runs and something is locked.</summary>
    public bool ShowsResting => IsSessionRunning && RestingCount > 0;

    public const int TodayTab = (int)MainTab.Today;
    public const int PlanTab = (int)MainTab.Plan;
    public const int StatsTab = (int)MainTab.Stats;
    public const int SettingsTab = (int)MainTab.Settings;

    /// <summary>The day bar shows above the page only on Stats (Today has its own under "More today").</summary>
    [ObservableProperty]
    public partial bool ShowsTimeline { get; set; } = MainTabs.ShowsTimelineStrip(MainTab.Today);

    partial void OnSelectedTabChanged(int oldValue, int newValue)
    {
        ShowsTimeline = MainTabs.ShowsTimelineStrip((MainTab)newValue);
        OnPropertyChanged(nameof(IsTodaySelected));
        OnPropertyChanged(nameof(IsTasksSelected));
        OnPropertyChanged(nameof(IsPlanSelected));
        OnPropertyChanged(nameof(IsStatsSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
        OnPropertyChanged(nameof(IsActivitySelected));
    }

    public bool IsTodaySelected => SelectedTab == (int)MainTab.Today;

    public bool IsTasksSelected => SelectedTab == (int)MainTab.Tasks;

    public bool IsPlanSelected => SelectedTab == (int)MainTab.Plan;

    public bool IsStatsSelected => SelectedTab == (int)MainTab.Stats;

    public bool IsSettingsSelected => SelectedTab == (int)MainTab.Settings;

    public bool IsActivitySelected => SelectedTab == (int)MainTab.Activity;

    /// <summary>A rail item was clicked.</summary>
    [RelayCommand]
    private void SelectTab(MainTab tab)
    {
        SelectedTab = (int)tab;
    }

    /// <summary>Settings, on the given row (the Guards row from "N resting", Devices &amp; sync from the sync light).</summary>
    public void OpenSettings(SettingsSection section)
    {
        SelectedTab = SettingsTab;
        Settings.SelectedRowIndex = (int)section;
    }

    [RelayCommand]
    private void OpenGuards()
    {
        OpenSettings(SettingsSection.Guards);
    }

    [RelayCommand]
    private void OpenDevicesAndSync()
    {
        OpenSettings(SettingsSection.DevicesAndSync);
    }

    /// <summary>The search flyout under the title bar is open (Ctrl+F or the magnifier); Esc closes it.</summary>
    [ObservableProperty]
    public partial bool IsSearchOpen { get; set; }

    [RelayCommand]
    private void OpenSearch()
    {
        IsSearchOpen = true;
    }

    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchOpen = false;
    }

    // Where the user was going when Settings had unsaved changes: a tab, or null = hiding the window.
    private int? _leaveTarget;

    /// <summary>Asks Save / Discard / Stay before leaving Settings with unsaved changes.</summary>
    [ObservableProperty]
    public partial bool ShowsLeavePrompt { get; set; }

    /// <summary>Raised when the window should hide (closing it with unsaved settings, after Save or Discard).</summary>
    public event EventHandler? HideRequested;

    /// <summary>
    /// False (and the prompt opens) when leaving Settings would drop unsaved changes; the view then stays on Settings.
    /// </summary>
    /// <param name="to">The tab being opened, or null when the window is being closed.</param>
    public bool MayLeaveTab(int from, int? to)
    {
        if (from != SettingsTab || to == SettingsTab || !Settings.HasUnsavedChanges)
        {
            return true;
        }

        _leaveTarget = to;
        ShowsLeavePrompt = true;
        return false;
    }

    [RelayCommand]
    private async Task SaveAndLeaveAsync()
    {
        if (!await Settings.SaveAllAsync())
        {
            ShowsLeavePrompt = false; // The header says what's wrong; stay to fix it.
            return;
        }

        Leave();
    }

    [RelayCommand]
    private void DiscardAndLeave()
    {
        Settings.Revert();
        Leave();
    }

    [RelayCommand]
    private void Stay()
    {
        ShowsLeavePrompt = false;
        _leaveTarget = null;
    }

    private void Leave()
    {
        ShowsLeavePrompt = false;
        if (_leaveTarget is int tab)
        {
            SelectedTab = tab;
        }
        else
        {
            HideRequested?.Invoke(this, EventArgs.Empty);
        }

        _leaveTarget = null;
    }

    /// <summary>Index of the page shown, in rail order (<see cref="MainTab"/>).</summary>
    [ObservableProperty]
    public partial int SelectedTab { get; set; }

    /// <summary>Opens the Setup DP Assistant; set by the app.</summary>
    public Action? ShowDpAssistant { get; set; }

    /// <summary>Locked during a focus session the user started, like the day plan editor.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenDpAssistant))]
    private void OpenDpAssistant()
    {
        ShowDpAssistant?.Invoke();
    }

    private bool CanOpenDpAssistant => !DayPlan.IsLocked;

    public SessionViewModel Session { get; }

    public ProfilesViewModel Profiles { get; }

    public DayPlanViewModel DayPlan { get; }

    public ExceptionsViewModel Exceptions { get; }

    public HistoryViewModel History { get; }

    /// <summary>The Notebook, shown on the Tasks page; set by the app.</summary>
    public NotebookViewModel? Notebook { get; init; }

    /// <summary>
    /// The top-right refresh: syncs now by every method that's on, reads calendars again and reloads the day bar and
    /// History. Returns what to say ("Up to date · 4:05 PM" or what failed). Set by the app.
    /// </summary>
    public Func<Task<string>>? RefreshAll { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshNowCommand))]
    public partial bool IsRefreshing { get; set; }

    partial void OnIsRefreshingChanged(bool value)
    {
        if (SyncLight is not null)
        {
            SyncLight.IsSyncing = value;
        }
    }

    /// <summary>The refresh button's tip: what it does, then the last result.</summary>
    [ObservableProperty]
    public partial string RefreshTip { get; set; } = "Refresh: sync with your other devices now and reload settings, calendars and history";

    private bool CanRefresh => !IsRefreshing;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshNowAsync()
    {
        if (RefreshAll is null)
        {
            return;
        }

        IsRefreshing = true;
        try
        {
            RefreshTip = await RefreshAll();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or Microsoft.Data.Sqlite.SqliteException)
        {
            RefreshTip = $"Refresh didn't finish: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public SetupViewModel Setup { get; }

    public IntegrationsViewModel Integrations { get; }

    public SettingsViewModel Settings { get; }

    public ActivityDebugViewModel Activity { get; }

    /// <summary>The search box in the flyout under the title bar; null hides it.</summary>
    public SearchViewModel? Search { get; init; }

    private readonly DevicesViewModel? _devices;

    /// <summary>The Devices &amp; sync page (ADR 22): this PC and the user's other devices.</summary>
    public DevicesViewModel? Devices
    {
        get => _devices;
        init
        {
            _devices = value;
            SyncLight = value is null ? null : new SyncLightViewModel(value);
        }
    }

    /// <summary>The title bar's sync light, read from the Devices page; null when there is no Devices page.</summary>
    public SyncLightViewModel? SyncLight { get; private set; }

    /// <summary>Opens today's sync QR codes (ADR 22); set by the app.</summary>
    public Action? ShowSyncQr { get; set; }

    /// <summary>The sync light popover's "Add a device" (QR) entry: while the QR code method is on.</summary>
    [ObservableProperty]
    public partial bool ShowsQrButton { get; set; }

    [RelayCommand]
    private void ShowSyncQrCodes()
    {
        ShowSyncQr?.Invoke();
    }

    private readonly DayBarViewModel? _dayBar;

    /// <summary>Today at a glance, above the Stats page. Clicking a session opens its profile; a plan part, the Plan page.</summary>
    public DayBarViewModel? DayBar
    {
        get => _dayBar;
        init
        {
            _dayBar = value;
            if (value is not null)
            {
                value.ProfileRequested += (_, profileId) =>
                {
                    OpenSettings(SettingsSection.Guards);
                    Profiles.Select(profileId);
                };
                value.DayPlanRequested += (_, _) => SelectedTab = PlanTab;
                // Empty Focus-row time clicked: add a past session there, in History on the Stats page (ADR 20).
                value.AddSessionRequested += (_, at) =>
                {
                    SelectedTab = StatsTab;
                    _ = History.OpenAddFormAtAsync(at);
                };
            }
        }
    }

    /// <summary>Shows the Activity (dev) page. Off unless ACCOUNTABILIBUDDY_DEV_TOOLS=1.</summary>
    public bool ShowDevTools { get; }

    public string? LoadError { get; }

    public bool HasLoadError => LoadError is not null;

    /// <summary>Shown while the Windows clock is off from real time; null when the clocks agree.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClockNotice))]
    public partial string? ClockNotice { get; set; }

    public bool HasClockNotice => ClockNotice is not null;

    /// <param name="offset">How far the Windows clock is from real time; zero when they agree.</param>
    public void ShowSystemClockOffset(TimeSpan offset)
    {
        if (offset == TimeSpan.Zero)
        {
            ClockNotice = null;
            return;
        }

        TimeSpan size = offset.Duration();
        string amount = size.TotalHours < 1 ? $"{(int)size.TotalMinutes} min"
            : size.TotalDays < 1 ? $"{(int)size.TotalHours} h {size.Minutes} min"
            : $"{(int)size.TotalDays} d {size.Hours} h";
        string direction = offset > TimeSpan.Zero ? "ahead" : "behind";
        ClockNotice = $"The Windows clock was changed and is {amount} {direction}. AccountabiliBuddy keeps real time, "
                      + "so sessions, waits and countdowns aren't affected. Setting the clock back clears this.";
    }
}
