namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>The Label dot choices: the stored value as the rows read it, and the words Settings › Tasks shows for each.</summary>
public static class TaskLabelStyles
{
    /// <summary>Settings › Tasks › Label dot, in order (Options, later, offers the first two only).</summary>
    public static IReadOnlyList<TaskLabelStyle> SettingsChoices { get; } = [TaskLabelStyle.UnderTask, TaskLabelStyle.ByCheckBox, TaskLabelStyle.PillBefore];

    /// <summary>A stored value as it reads now: the old "Pill in details" and anything unknown become "Under the task"; the rest stay.</summary>
    public static TaskLabelStyle Migrated(TaskLabelStyle stored)
    {
        return stored is TaskLabelStyle.PillBefore or TaskLabelStyle.ByCheckBox ? stored : TaskLabelStyle.UnderTask;
    }

    /// <summary>The words for a choice ("Under the task", "By the check box", "Pill before the title").</summary>
    public static string NameOf(TaskLabelStyle style)
    {
        return Migrated(style) switch
        {
            TaskLabelStyle.ByCheckBox => "By the check box",
            TaskLabelStyle.PillBefore => "Pill before the title",
            _ => "Under the task",
        };
    }
}
