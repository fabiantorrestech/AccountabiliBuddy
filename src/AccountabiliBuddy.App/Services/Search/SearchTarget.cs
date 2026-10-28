namespace AccountabiliBuddy.App.Services.Search;

/// <summary>Where a search result opens: a page, and inside it what to show.</summary>
/// <param name="Tab">The main window's page index (<see cref="SearchTabs"/>).</param>
/// <param name="ProfileId">The profile to select in Settings › Guards.</param>
/// <param name="Date">The day to show in Plan's Calendar section.</param>
/// <param name="SettingsGroup">The Settings row to switch to (a <see cref="AccountabiliBuddy.App.ViewModels.SettingsSection"/> index).</param>
/// <param name="Control">The control the text was found in, to bring into view (an app word).</param>
/// <param name="PlanTemplate">The Day plan's inner tab: 0 = Workdays, 1 = Off-Days.</param>
public sealed record SearchTarget(
    int Tab,
    Guid? ProfileId = null,
    DateOnly? Date = null,
    int? SettingsGroup = null,
    object? Control = null,
    int? PlanTemplate = null);
