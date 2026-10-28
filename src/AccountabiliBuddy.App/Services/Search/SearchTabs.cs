using AccountabiliBuddy.App.ViewModels;

namespace AccountabiliBuddy.App.Services.Search;

/// <summary>
/// The main window's pages by index (<see cref="MainTab"/>, the same order as its pages), and the Settings rows by
/// index (<see cref="SettingsSection"/>) that a result inside Settings opens.
/// </summary>
public static class SearchTabs
{
    public const int Today = (int)MainTab.Today;
    public const int Tasks = (int)MainTab.Tasks;
    public const int Plan = (int)MainTab.Plan;
    public const int Stats = (int)MainTab.Stats;
    public const int Settings = (int)MainTab.Settings;

    /// <summary>The Settings row holding Profiles (and the Unlocks card).</summary>
    public const int GuardsRow = (int)SettingsSection.Guards;
}
