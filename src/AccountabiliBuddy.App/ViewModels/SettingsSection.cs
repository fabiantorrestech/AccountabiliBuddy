namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The rows of the Settings list (the same order as <see cref="SettingsViewModel.Rows"/>). Pages, search and links pick a row by its
/// section (<see cref="SettingsViewModel.Select"/>, <see cref="SettingsSectionIs"/>), never by its place in the list, so the list can
/// be reordered.
/// </summary>
public enum SettingsSection
{
    /// <summary>Round 2: the Today page's layout (Settings › Today); first, as the Tabs group's first row on the board.</summary>
    Today,
    YourDay,
    Sleep,
    FocusSessions,
    Tasks,
    Guards,
    AlertsAndOnScreen,
    DevicesAndSync,
    Connections,
    Appearance,
    AppAndData,
    Setup,
    About,

    /// <summary>Round 2 R5.6: Settings › Goals (its row sits after Today; the value is last so no other section's number changes).</summary>
    Goals,
}
