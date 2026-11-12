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

    /// <summary>
    /// Today has no Settings section of its own yet (Settings › Today is round 2's R2): its row opens Your day, where how the day
    /// starts and ends lives today.
    /// </summary>
    public const SettingsSection TodaySettingsSection = SettingsSection.YourDay;

    /// <summary>
    /// Tasks: Group by (the Shelf's), Rows (Normal / Compact, moved here from the header), View, filter and sort › (the Shelf's View
    /// menu, unchanged), then Tasks settings ›. Round 2's R3 gives it its final rows (brief §4.6).
    /// </summary>
    public static IReadOnlyList<TaskMenuEntry> Tasks(
        bool compact,
        ICommand normalRows,
        ICommand compactRows,
        IReadOnlyList<TaskMenuEntry> groupBy,
        IReadOnlyList<TaskMenuEntry> viewMenu,
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
        if (viewMenu.Count > 0)
        {
            entries.Add(TaskMenuEntry.Separator);
            entries.Add(TaskMenuEntry.Group("View, filter and sort", viewMenu, MaterialIconKind.FilterVariant));
        }

        entries.Add(TaskMenuEntry.Separator);
        entries.Add(SettingsRow(TasksSettingsText, TasksSettingsSection, null, openSettings));
        return entries;
    }

    /// <summary>
    /// Today: its sections' Expand all / Collapse all, then Today settings ›. Round 2's R2 gives it its final rows (Cards, Today
    /// sidebar; brief §4.2).
    /// </summary>
    public static IReadOnlyList<TaskMenuEntry> Today(string expandText, MaterialIconKind expandIcon, ICommand toggleExpand, Action<SettingsSection> openSettings)
    {
        return
        [
            new TaskMenuEntry("Sections", IsHeading: true),
            new TaskMenuEntry(expandText, toggleExpand, Icon: expandIcon),
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
