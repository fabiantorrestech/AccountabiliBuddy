using AccountabiliBuddy.App.ViewModels;

namespace AccountabiliBuddy.App.Services.Navigation;

/// <summary>
/// One place in the PC's back / forward history (owner 2026-11-14): the main tab, and on Settings the section, on Tasks the open task,
/// on Goals the goal shown. Only what is cheap to put back; scroll positions, folds, filters and dialogs are not kept.
/// </summary>
/// <param name="Tab">The page.</param>
/// <param name="Section">Settings only: the section shown; null = Settings as it opens.</param>
/// <param name="Task">Tasks only: the task open in the open-task card; null = no task open.</param>
/// <param name="Goal">Goals only: the goal shown; null = the page picks its first goal.</param>
public sealed record NavigationPlace(MainTab Tab, SettingsSection? Section = null, TaskRowKey? Task = null, Guid? Goal = null)
{
    /// <summary>True when the place names nothing inside its page (the page only).</summary>
    public bool IsPageOnly => Section is null && Task is null && Goal is null;
}
