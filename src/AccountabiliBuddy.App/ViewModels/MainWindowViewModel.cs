using AccountabiliBuddy.App.DevTools;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Root of the main window: one view model per tab, plus a banner for load errors.</summary>
public sealed class MainWindowViewModel
{
    public MainWindowViewModel(
        SessionViewModel session,
        ProfilesViewModel profiles,
        ScheduleViewModel schedule,
        HistoryViewModel history,
        SetupViewModel setup,
        IntegrationsViewModel integrations,
        SettingsViewModel settings,
        ActivityDebugViewModel activity,
        string? loadError)
    {
        Session = session;
        Profiles = profiles;
        Schedule = schedule;
        History = history;
        Setup = setup;
        Integrations = integrations;
        Settings = settings;
        Activity = activity;
        LoadError = loadError;
    }

    public SessionViewModel Session { get; }

    public ProfilesViewModel Profiles { get; }

    public ScheduleViewModel Schedule { get; }

    public HistoryViewModel History { get; }

    public SetupViewModel Setup { get; }

    public IntegrationsViewModel Integrations { get; }

    public SettingsViewModel Settings { get; }

    public ActivityDebugViewModel Activity { get; }

    public string? LoadError { get; }

    public bool HasLoadError => LoadError is not null;
}
