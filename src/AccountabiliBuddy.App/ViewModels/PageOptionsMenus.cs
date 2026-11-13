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
        Action<string> openTodaySettingsAt,
        bool showMoreToday = false,
        ICommand? toggleMoreToday = null)
    {
        // TD1: "Show More today" (the folded card under the session card) joins the Cards group once the page gives its switch.
        TaskMenuEntry[] moreToday = toggleMoreToday is null
            ? []
            : [new TaskMenuEntry("Show More today", toggleMoreToday, IsChecked: showMoreToday, Icon: MaterialIconKind.UnfoldMoreHorizontal)];
        return
        [
            new TaskMenuEntry("Cards", IsHeading: true),
            new TaskMenuEntry("Arrange cards…", new RelayCommand(() => openTodaySettingsAt(TodayCardsArea)), Icon: MaterialIconKind.ViewDashboardEditOutline, OpensMore: true),
            new TaskMenuEntry("Show finished today", toggleFinished, IsChecked: showFinished, Icon: MaterialIconKind.EyeOutline),
            .. moreToday,
            TaskMenuEntry.Separator,
            new TaskMenuEntry("Today sidebar", IsHeading: true),
            new TaskMenuEntry("Show the sidebar", toggleSidebar, IsChecked: sidebarOpen, Icon: MaterialIconKind.PageLayoutSidebarRight, Note: "Ctrl+\\ on Today"),
            new TaskMenuEntry("Sidebar items and order…", new RelayCommand(() => openTodaySettingsAt(TodaySidebarArea)), Icon: MaterialIconKind.FormatListBulleted, OpensMore: true),
            TaskMenuEntry.Separator,
            SettingsRow(TodaySettingsText, TodaySettingsSection, "How the day starts and ends, the start-of-day card", openSettings),
        ];
    }

    /// <summary>
    /// Goals (round 2 R5, brief §4.3, §4.4, CN §4a): List › Show goals (By area / Current / All), Sort (Your order), Areas…; Charts › Week
    /// starts on (Mon / Sun / Sat), Heatmap day labels (All / Some / None), Extra tiles (Streak, Total: the default for goals without
    /// their own); then Goals settings ›, last. Areas… and Goals settings › open R5b's Edit areas and Settings › Goals through hooks.
    /// </summary>
    public static IReadOnlyList<TaskMenuEntry> Goals(
        AccountabiliBuddy.App.Services.Goals.GoalListView view,
        Func<AccountabiliBuddy.App.Services.Goals.GoalListView, Task> setView,
        AccountabiliBuddy.Core.Goals.GoalWeekStart weekStart,
        Func<AccountabiliBuddy.Core.Goals.GoalWeekStart, Task> setWeekStart,
        AccountabiliBuddy.App.Services.Goals.GoalHeatmapLabels labels,
        Func<AccountabiliBuddy.App.Services.Goals.GoalHeatmapLabels, Task> setLabels,
        AccountabiliBuddy.Core.Goals.GoalExtraTiles extraTiles,
        Func<AccountabiliBuddy.Core.Goals.GoalExtraTiles, Task> setExtraTiles,
        Action openAreas,
        Action openGoalsSettings,
        AccountabiliBuddy.App.Services.Goals.GoalListSort sort = AccountabiliBuddy.App.Services.Goals.GoalListSort.YourOrder,
        Func<AccountabiliBuddy.App.Services.Goals.GoalListSort, Task>? setSortOrNull = null,
        AccountabiliBuddy.App.Services.Goals.GoalMonthLines monthLines = AccountabiliBuddy.App.Services.Goals.GoalMonthLines.Normal,
        Func<AccountabiliBuddy.App.Services.Goals.GoalMonthLines, Task>? setMonthLines = null)
    {
        Func<AccountabiliBuddy.App.Services.Goals.GoalListSort, Task> setSort = setSortOrNull ?? (_ => Task.CompletedTask);
        static string ViewName(AccountabiliBuddy.App.Services.Goals.GoalListView value) => value switch
        {
            AccountabiliBuddy.App.Services.Goals.GoalListView.Current => "Current",
            AccountabiliBuddy.App.Services.Goals.GoalListView.All => "All",
            _ => "By area",
        };

        static string WeekName(AccountabiliBuddy.Core.Goals.GoalWeekStart value) => value switch
        {
            AccountabiliBuddy.Core.Goals.GoalWeekStart.Monday => "Mon",
            AccountabiliBuddy.Core.Goals.GoalWeekStart.Saturday => "Sat",
            _ => "Sun",
        };

        // Board Goals-PC › Options: Charts first, then List, each choice inline (a segmented control), then Goals settings.
        List<TaskMenuEntry> weeks = new[] { AccountabiliBuddy.Core.Goals.GoalWeekStart.Monday, AccountabiliBuddy.Core.Goals.GoalWeekStart.Sunday, AccountabiliBuddy.Core.Goals.GoalWeekStart.Saturday }
            .Select(value => new TaskMenuEntry(WeekName(value), new AsyncRelayCommand(() => setWeekStart(value)), IsChecked: value == weekStart))
            .ToList();
        List<TaskMenuEntry> dayLabels = Enum.GetValues<AccountabiliBuddy.App.Services.Goals.GoalHeatmapLabels>()
            .Select(value => new TaskMenuEntry(value.ToString(), new AsyncRelayCommand(() => setLabels(value)), IsChecked: value == labels))
            .ToList();
        List<TaskMenuEntry> tiles =
        [
            new TaskMenuEntry("Streak", new AsyncRelayCommand(() => setExtraTiles(extraTiles with { Streak = !extraTiles.Streak })), IsChecked: extraTiles.Streak),
            new TaskMenuEntry("Total", new AsyncRelayCommand(() => setExtraTiles(extraTiles with { Total = !extraTiles.Total })), IsChecked: extraTiles.Total),
        ];
        string tilesValue = extraTiles is { Streak: true, Total: true } ? "Streak, Total" : extraTiles.Streak ? "Streak" : extraTiles.Total ? "Total" : "Off";
        bool byArea = view == AccountabiliBuddy.App.Services.Goals.GoalListView.ByArea;
        List<TaskMenuEntry> groups =
        [
            new TaskMenuEntry("Area", new AsyncRelayCommand(() => setView(AccountabiliBuddy.App.Services.Goals.GoalListView.ByArea)), IsChecked: byArea),
            // One list: Current (the goals that ask for something); a list already on All stays there.
            new TaskMenuEntry("None", new AsyncRelayCommand(() => byArea ? setView(AccountabiliBuddy.App.Services.Goals.GoalListView.Current) : Task.CompletedTask), IsChecked: !byArea),
        ];
        List<TaskMenuEntry> sorts =
        [
            new TaskMenuEntry("Your order", new AsyncRelayCommand(() => setSort(AccountabiliBuddy.App.Services.Goals.GoalListSort.YourOrder)),
                IsChecked: sort == AccountabiliBuddy.App.Services.Goals.GoalListSort.YourOrder),
            new TaskMenuEntry("Behind first", new AsyncRelayCommand(() => setSort(AccountabiliBuddy.App.Services.Goals.GoalListSort.BehindFirst)),
                IsChecked: sort == AccountabiliBuddy.App.Services.Goals.GoalListSort.BehindFirst),
        ];
        return
        [
            new TaskMenuEntry("Charts", IsHeading: true),
            new TaskMenuEntry("Week starts on", Segments: weeks, Note: "Heatmaps, week bars and weekly counts"),
            new TaskMenuEntry("Heatmap day labels", Segments: dayLabels, Note: "Every row, or every other row"),
            MonthLinesEntry(monthLines, setMonthLines ?? (_ => Task.CompletedTask)),
            new TaskMenuEntry("Extra tiles", Children: tiles, Value: tilesValue, Note: "For goals without their own choice"),
            TaskMenuEntry.Separator,
            new TaskMenuEntry("List", IsHeading: true),
            new TaskMenuEntry("Group by", Segments: groups, Note: "Areas you made, or one list"),
            new TaskMenuEntry("Sort", Segments: sorts, Note: "Within each group"),
            new TaskMenuEntry("Areas", new RelayCommand(openAreas), Note: "Rename, pick icons, reorder, add", OpensMore: true),
            TaskMenuEntry.Separator,
            new TaskMenuEntry(GoalsSettingsText, new RelayCommand(openGoalsSettings), Icon: MaterialIconKind.Cog, Note: "Weekly review, reminders, charts, areas", OpensMore: true),
        ];
    }

    public const string GoalsSettingsText = "Goals settings";

    /// <summary>Month lines (HM1): Darker / Normal / Lighter / Off, one inline row; the same device-local choice in Goals Options, Stats Options and Settings › Goals.</summary>
    public static TaskMenuEntry MonthLinesEntry(
        AccountabiliBuddy.App.Services.Goals.GoalMonthLines current, Func<AccountabiliBuddy.App.Services.Goals.GoalMonthLines, Task> set)
    {
        AccountabiliBuddy.App.Services.Goals.GoalMonthLines[] order =
        [
            AccountabiliBuddy.App.Services.Goals.GoalMonthLines.Darker,
            AccountabiliBuddy.App.Services.Goals.GoalMonthLines.Normal,
            AccountabiliBuddy.App.Services.Goals.GoalMonthLines.Lighter,
            AccountabiliBuddy.App.Services.Goals.GoalMonthLines.Off,
        ];
        List<TaskMenuEntry> segments = order
            .Select(value => new TaskMenuEntry(value.ToString(), new AsyncRelayCommand(() => set(value)), IsChecked: value == current))
            .ToList();
        return new TaskMenuEntry("Month lines", Segments: segments, Note: "The line between months on every heatmap");
    }

    /// <summary>The Stats page's Options menu (HM1): Charts › Month lines; Stats has no settings section of its own, so no settings row.</summary>
    public static IReadOnlyList<TaskMenuEntry> Stats(
        AccountabiliBuddy.App.Services.Goals.GoalMonthLines monthLines, Func<AccountabiliBuddy.App.Services.Goals.GoalMonthLines, Task> setMonthLines)
    {
        return
        [
            new TaskMenuEntry("Charts", IsHeading: true),
            MonthLinesEntry(monthLines, setMonthLines),
        ];
    }

    /// <summary>A page's settings row: always last in its Options menu; opens Settings at <paramref name="section"/>.</summary>
    public static TaskMenuEntry SettingsRow(string text, SettingsSection section, string? note, Action<SettingsSection> openSettings)
    {
        return new TaskMenuEntry(text, new RelayCommand(() => openSettings(section)), Icon: MaterialIconKind.Cog, Note: note, OpensMore: true);
    }
}
