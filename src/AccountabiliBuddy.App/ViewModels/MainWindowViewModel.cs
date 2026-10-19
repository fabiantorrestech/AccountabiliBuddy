using AccountabiliBuddy.App.DevTools;
using CommunityToolkit.Mvvm.ComponentModel;

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
    }

    public SessionViewModel Session { get; }

    public ProfilesViewModel Profiles { get; }

    public ScheduleViewModel Schedule { get; }

    public ExceptionsViewModel Exceptions { get; }

    public HistoryViewModel History { get; }

    public SetupViewModel Setup { get; }

    public IntegrationsViewModel Integrations { get; }

    public SettingsViewModel Settings { get; }

    public ActivityDebugViewModel Activity { get; }

    /// <summary>Today at a glance, above the tabs.</summary>
    public DayBarViewModel? DayBar { get; init; }

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
