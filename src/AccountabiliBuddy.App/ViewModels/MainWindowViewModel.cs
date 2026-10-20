using AccountabiliBuddy.App.DevTools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Root of the main window: one view model per tab, the day bar, and banners for load errors and a changed clock.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(
        SessionViewModel session,
        ProfilesViewModel profiles,
        ScheduleViewModel schedule,
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
        Schedule = schedule;
        Exceptions = exceptions;
        History = history;
        Setup = setup;
        Integrations = integrations;
        Settings = settings;
        Activity = activity;
        ShowDevTools = showDevTools;
        LoadError = loadError;
        session.OpenProfilesRequested += (_, _) => SelectedTab = ProfilesTab;
    }

    private const int ProfilesTab = 1;
    public const int SettingsTab = 7;

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

    /// <summary>Index of the tab shown, in sidebar order (Session, Profiles, Schedule, …).</summary>
    [ObservableProperty]
    public partial int SelectedTab { get; set; }

    public SessionViewModel Session { get; }

    public ProfilesViewModel Profiles { get; }

    public ScheduleViewModel Schedule { get; }

    public ExceptionsViewModel Exceptions { get; }

    public HistoryViewModel History { get; }

    public SetupViewModel Setup { get; }

    public IntegrationsViewModel Integrations { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>The noise player at the bottom of the sidebar.</summary>
    public NoisePlayerViewModel? NoisePlayer { get; set; }

    public ActivityDebugViewModel Activity { get; }

    private readonly DayBarViewModel? _dayBar;

    /// <summary>Today at a glance, above the tabs. Clicking a block there opens its profile.</summary>
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
                    SelectedTab = ProfilesTab;
                    Profiles.Select(profileId);
                };
            }
        }
    }

    /// <summary>Shows the Activity (dev) tab. Off unless ACCOUNTABILIBUDDY_DEV_TOOLS=1.</summary>
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
