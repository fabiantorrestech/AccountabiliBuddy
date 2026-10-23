namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The main window's tabs, in sidebar order (the same order as its tab list and pages). Three tiers: the daily ones
/// (Today to Calendar), the sometimes ones (History, Devices), and under <see cref="More"/> the ones set once
/// (Setup, Integrations, Settings, About). <see cref="More"/> itself has no page: selecting it unfolds the rest.
/// </summary>
public enum MainTab
{
    Today,
    Profiles,
    DayPlan,
    Calendar,
    History,

    /// <summary>Everything written down: come-backs, notes, unfinished and done tasks.</summary>
    Notebook,
    Devices,
    More,
    Setup,
    Integrations,
    Settings,
    About,

    /// <summary>Only with ACCOUNTABILIBUDDY_DEV_TOOLS=1.</summary>
    Activity,
}
