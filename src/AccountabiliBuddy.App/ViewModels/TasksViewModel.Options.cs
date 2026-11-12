namespace AccountabiliBuddy.App.ViewModels;

// Round 2 (plan R1.3): the page headers' Options (tune) menus of the two pages this view model drives, Tasks and Today, and the views
// list behind the Tasks header's Views (bookmark) button.
public sealed partial class TasksViewModel
{
    /// <summary>Opens Settings at a section (the Options menus' last row); set by the app. Null does nothing.</summary>
    public Action<SettingsSection>? OpenSettings { get; set; }

    /// <summary>The Tasks page's Options menu (<see cref="PageOptionsMenus.Tasks"/>): its last row opens Settings › Tasks.</summary>
    public IReadOnlyList<TaskMenuEntry> TasksOptionsMenu()
    {
        return PageOptionsMenus.Tasks(
            IsCompact, UseNormalRowsCommand, UseCompactRowsCommand, Shelf.HasViews ? Shelf.GroupMenu() : [], Shelf.ViewMenu(), OpenSettingsAt);
    }

    /// <summary>The Today page's Options menu (<see cref="PageOptionsMenus.Today"/>): its last row opens Settings › Your day.</summary>
    public IReadOnlyList<TaskMenuEntry> TodayOptionsMenu()
    {
        return PageOptionsMenus.Today(TodayTabPanel.ExpandTip, TodayTabPanel.ExpandIcon, TodayTabPanel.ToggleExpandCommand, OpenSettingsAt);
    }

    private void OpenSettingsAt(SettingsSection section)
    {
        OpenSettings?.Invoke(section);
    }
}
