using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The page Options (tune) menus (round 2, brief §2, §3.3, §4.2, §4.6; CN §4b): one bordered Options button, last in each page's
/// header, instead of a gear. Each menu holds that page's view choices and always ends with the page's settings row, which opens
/// Settings at that page's section. Built from plain values and actions, so the checks can call them without a page.
/// </summary>
public static class PageOptionsMenus
{
    public const string TasksSettingsText = "Tasks settings";

    public const string TodaySettingsText = "Today settings";

    /// <summary>The Settings section each page's last Options row opens.</summary>
    public const SettingsSection TasksSettingsSection = SettingsSection.Tasks;

    /// <summary>Today's settings row opens Settings › Today (round 2 R2; its first row links to Your day, how the day starts and ends).</summary>
    public const SettingsSection TodaySettingsSection = SettingsSection.Today;

    /// <summary>The Settings › Today group "Arrange cards…" scrolls to (its search area name).</summary>
    public const string TodayCardsArea = "Today layout";

    /// <summary>The Settings › Today group "Sidebar items and order…" scrolls to (its search area name).</summary>
    public const string TodaySidebarArea = "Today sidebar";

    /// <summary>The note under Options › History (board Tasks-PC-Options).</summary>
    public const string HistoryNote = "Finished and deleted tasks, 30 days";

    /// <summary>
    /// Tasks (round 2 R3, brief §4.6, board Tasks-PC-Options): Group by Bins / Label / Tag / None · one list, Rows Normal / Compact │
    /// Views › (the active view, "★ Default"; the whole View menu: saved views, This view, arrange, Show only, Reset), Sort › (its value,
    /// "Your order"), Expand all bins (or Collapse all), History (with its note; opens the History popover through
    /// <see cref="TaskMenuEntry.ViewAction"/> "history") │ Tasks settings ›.
    /// </summary>
    public static IReadOnlyList<TaskMenuEntry> Tasks(
        bool compact,
        ICommand normalRows,
        ICommand compactRows,
        IReadOnlyList<TaskMenuEntry> groupBy,
        IReadOnlyList<TaskMenuEntry> viewMenu,
        string? viewText,
        IReadOnlyList<TaskMenuEntry> sortMenu,
        string? sortText,
        bool expanded,
        ICommand? toggleExpand,
        bool hasHistory,
        Action<SettingsSection> openSettings)
    {
        var entries = new List<TaskMenuEntry>();
        if (groupBy.Count > 0)
        {
            entries.Add(new TaskMenuEntry("Group by", IsHeading: true));
            entries.AddRange(groupBy);
            entries.Add(TaskMenuEntry.Separator);
        }

        entries.Add(new TaskMenuEntry("Rows", IsHeading: true));
        entries.Add(new TaskMenuEntry("Normal", normalRows, IsChecked: !compact, Icon: MaterialIconKind.ViewHeadline));
        entries.Add(new TaskMenuEntry("Compact", compactRows, IsChecked: compact, Icon: MaterialIconKind.FormatLineSpacing));
        entries.Add(TaskMenuEntry.Separator);
        if (viewMenu.Count > 0)
        {
            entries.Add(new TaskMenuEntry("Views", Children: viewMenu, Icon: MaterialIconKind.BookmarkOutline, Value: viewText));
        }

        if (sortMenu.Count > 0)
        {
            entries.Add(new TaskMenuEntry("Sort", Children: sortMenu, Icon: MaterialIconKind.Sort, Value: sortText));
        }

        entries.Add(new TaskMenuEntry(
            expanded ? "Collapse all bins" : "Expand all bins", toggleExpand,
            Icon: expanded ? MaterialIconKind.UnfoldLessHorizontal : MaterialIconKind.UnfoldMoreHorizontal));
        if (hasHistory)
        {
            entries.Add(new TaskMenuEntry("History", ViewAction: "history", Icon: MaterialIconKind.History, Note: HistoryNote));
        }

        entries.Add(TaskMenuEntry.Separator);
        entries.Add(SettingsRow(TasksSettingsText, TasksSettingsSection, null, openSettings));
        return entries;
    }

    /// <summary>
    /// Today (round 2, brief §4.2, board Today-PC-Options): Cards › Arrange cards…, Show finished today ✓; Today sidebar › Show the
    /// sidebar ✓ (Ctrl+\ in its note: menus show no keys), Sidebar items and order…; then Today settings ›. Arrange cards… and Sidebar
    /// items and order… open Settings › Today scrolled to that part (no new dialog).
    /// </summary>
    public static IReadOnlyList<TaskMenuEntry> Today(
        bool showFinished,
        ICommand toggleFinished,
        bool sidebarOpen,
        ICommand? toggleSidebar,
        Action<SettingsSection> openSettings,
        Action<string> openTodaySettingsAt)
    {
        return
        [
            new TaskMenuEntry("Cards", IsHeading: true),
            new TaskMenuEntry("Arrange cards…", new RelayCommand(() => openTodaySettingsAt(TodayCardsArea)), Icon: MaterialIconKind.ViewDashboardEditOutline, OpensMore: true),
            new TaskMenuEntry("Show finished today", toggleFinished, IsChecked: showFinished, Icon: MaterialIconKind.EyeOutline),
            TaskMenuEntry.Separator,
            new TaskMenuEntry("Today sidebar", IsHeading: true),
            new TaskMenuEntry("Show the sidebar", toggleSidebar, IsChecked: sidebarOpen, Icon: MaterialIconKind.PageLayoutSidebarRight, Note: "Ctrl+\\ on Today"),
            new TaskMenuEntry("Sidebar items and order…", new RelayCommand(() => openTodaySettingsAt(TodaySidebarArea)), Icon: MaterialIconKind.FormatListBulleted, OpensMore: true),
            TaskMenuEntry.Separator,
            SettingsRow(TodaySettingsText, TodaySettingsSection, "How the day starts and ends, the start-of-day card", openSettings),
        ];
    }

    /// <summary>A page's settings row: always last in its Options menu; opens Settings at <paramref name="section"/>.</summary>
    public static TaskMenuEntry SettingsRow(string text, SettingsSection section, string? note, Action<SettingsSection> openSettings)
    {
        return new TaskMenuEntry(text, new RelayCommand(() => openSettings(section)), Icon: MaterialIconKind.Cog, Note: note, OpensMore: true);
    }
}
