using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.App.Services.Today;

namespace AccountabiliBuddy.App.ViewModels;

// Round 2 (plan R1.3): the page headers' Options (tune) menus of the two pages this view model drives, Tasks and Today, and the views
// list behind the Tasks header's Views (bookmark) button.
public sealed partial class TasksViewModel
{
    /// <summary>Opens Settings at a section (the Options menus' last row); set by the app. Null does nothing.</summary>
    public Action<SettingsSection>? OpenSettings { get; set; }

    /// <summary>
    /// The Tasks page's Options menu (<see cref="PageOptionsMenus.Tasks"/>, Tasks redesign board 09): Sort ›, Rows, Label dot, Fade controls
    /// you're not using, then Views ›, Layout › (0.1.154), Expand all bins, Show finished on Today, Show more today, History; its last row opens Settings › Tasks.
    /// </summary>
    public IReadOnlyList<TaskMenuEntry> TasksOptionsMenu()
    {
        TaskSettingsService? settings = TaskSettings;
        TodayLayoutService? today = _todayLayout;
        var state = new TasksOptionsState(
            IsCompact, UseNormalRowsCommand, UseCompactRowsCommand,
            Shelf.HasViews ? Shelf.SortEntries() : [], Shelf.SortText,
            TaskLabelStyles.Migrated(settings?.Current.LabelStyle ?? TaskLabelStyle.UnderTask),
            settings is null ? null : style => settings.SetLabelStyleAsync(style),
            _layout.Current.FadeIdleControls, () => _layout.SetFadeIdleControlsAsync(!_layout.Current.FadeIdleControls),
            Shelf.ViewMenu(), Shelf.ActiveViewText,
            Shelf.Panel.IsExpanded, Shelf.Panel.ToggleExpandCommand,
            ShowsFinishedOnToday,
            today is null ? null : new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => today.SetShowFinishedTodayAsync(!ShowsFinishedOnToday)),
            today?.Current.ShowMoreToday ?? false,
            today is null ? null : new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => today.SetShowMoreTodayAsync(!today.Current.ShowMoreToday)),
            LayoutMenu.IsAvailable ? LayoutMenu.GroupName : null);
        return PageOptionsMenus.Tasks(state, OpenSettingsAt);
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
            },
            _todayLayout?.Current.ShowMoreToday ?? false,
            _todayLayout is null ? null : new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => _todayLayout.SetShowMoreTodayAsync(!_todayLayout.Current.ShowMoreToday)));
    }

    private void OpenSettingsAt(SettingsSection section)
    {
        OpenSettings?.Invoke(section);
    }
}
