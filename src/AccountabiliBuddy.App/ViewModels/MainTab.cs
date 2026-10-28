namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The main window's pages, in rail order (the same order as the pages in the window). Four daily pages, then Settings
/// at the bottom of the rail. Profiles, Devices, Integrations, Setup and About live inside Settings
/// (<see cref="SettingsSection"/>); Day plan and Calendar inside Plan; History inside Stats; Notebook inside Tasks.
/// </summary>
public enum MainTab
{
    Today,
    Tasks,
    Plan,
    Stats,
    Settings,

    /// <summary>Only with ACCOUNTABILIBUDDY_DEV_TOOLS=1.</summary>
    Activity,
}
