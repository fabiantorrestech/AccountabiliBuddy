namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The rows of the Settings list (the same order as <see cref="SettingsViewModel.Rows"/>). Pages, search and links pick a row by its
/// section (<see cref="SettingsViewModel.Select"/>, <see cref="SettingsSectionIs"/>), never by its place in the list, so the list can
/// be reordered.
/// </summary>
public enum SettingsSection
{
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
}
