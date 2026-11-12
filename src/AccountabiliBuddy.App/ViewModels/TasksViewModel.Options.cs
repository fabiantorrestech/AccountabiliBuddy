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

    /// <summary>Opens Settings at a section scrolled to one of its groups (by search area name); set by the app. Null opens the section.</summary>
    public Action<SettingsSection, string>? OpenSettingsArea { get; set; }

    /// <summary>
    /// The Today page's Options menu (<see cref="PageOptionsMenus.Today"/>): Cards and Today sidebar choices; its last row opens
    /// Settings › Today.
    /// </summary>
    public IReadOnlyList<TaskMenuEntry> TodayOptionsMenu()
    {
        TodaySidebarViewModel? sidebar = _session.Sidebar;
        return PageOptionsMenus.Today(
            ShowsFinishedOnToday,
            new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => _todayLayout?.SetShowFinishedTodayAsync(!ShowsFinishedOnToday) ?? Task.CompletedTask),
            sidebar?.IsOpen ?? false,
            sidebar?.ToggleCommand,
            OpenSettingsAt,
            area =>
            {
                if (OpenSettingsArea is { } openArea)
                {
                    openArea(PageOptionsMenus.TodaySettingsSection, area);
                }
                else
                {
                    OpenSettingsAt(PageOptionsMenus.TodaySettingsSection);
                }
            });
    }

    private void OpenSettingsAt(SettingsSection section)
    {
        OpenSettings?.Invoke(section);
    }
}
