using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The toast text of each task action (ADR 76, spec 05 section 14). They hold the user's task titles: shown on screen, never logged.
/// </summary>
public static class TaskToasts
{
    private const int ShortLength = 28;

    public const string Undone = "Undone";

    /// <summary>A title cut to 28 characters with an ellipsis.</summary>
    public static string Short(string title)
    {
        string trimmed = title.Trim();
        return trimmed.Length > ShortLength ? trimmed[..ShortLength] + "…" : trimmed;
    }

    /// <summary>"“Draft Q4…”" for one task, "3 tasks" for several.</summary>
    public static string Subject(IReadOnlyList<TaskItem> tasks)
    {
        return tasks.Count == 1 ? "“" + Short(tasks[0].Title) + "”" : tasks.Count + " tasks";
    }

    /// <summary>A step as "142b" (or "b" for a task with no number yet).</summary>
    public static string StepRef(TaskItem task, int stepIndex)
    {
        return (task.Number?.Value.ToString() ?? string.Empty) + TaskStepLetters.Of(stepIndex);
    }

    public static string Moved(IReadOnlyList<TaskItem> tasks, TaskBin bin)
    {
        return Subject(tasks) + " → " + TaskBins.Name(bin);
    }

    public static string Finished(IReadOnlyList<TaskItem> tasks)
    {
        return tasks.Count == 1 ? "Finished “" + Short(tasks[0].Title) + "”" : tasks.Count + " tasks finished";
    }

    public static string BackIn(TaskBin bin)
    {
        return "Back in " + TaskBins.Name(bin);
    }

    public static string Deleted(IReadOnlyList<TaskItem> tasks)
    {
        return "Deleted " + Subject(tasks);
    }

    public static string Archived(IReadOnlyList<TaskItem> tasks)
    {
        return "Archived " + Subject(tasks);
    }

    public static string AllStepsDone(TaskItem task)
    {
        return "All steps done · “" + Short(task.Title) + "” finished";
    }

    public static string TimeLimit(int? minutes)
    {
        return minutes is int value ? "Time limit " + value + " min" : "Time limit removed";
    }

    public static string Labelled(string? labelName)
    {
        return labelName is null ? "Label removed" : "Label " + labelName;
    }

    public static string Renamed(int count)
    {
        return "Renamed " + count + (count > 1 ? " tasks" : " task");
    }

    public static string Highlighted(bool on)
    {
        return on ? "Highlighted" : "Highlight removed";
    }

    public static string Tagged(IReadOnlyList<string> tags, bool added)
    {
        return (added ? "Tagged " : "Untagged ") + string.Join(' ', tags.Select(tag => "#" + tag));
    }

    public static string BroughtBack(TaskItem task)
    {
        return "Brought back “" + Short(task.Title) + "”";
    }

    public static string DoneAgain(TaskItem copy)
    {
        return "Added “" + Short(copy.Title) + "” to " + TaskBins.Name(copy.Bin);
    }

    /// <summary>An old list's delete that was ignored because another device moved the task since (nothing was deleted).</summary>
    public static string MovedElsewhere(TaskItem task)
    {
        return "“" + Short(task.Title) + "” was moved on another device, so it wasn't deleted";
    }

    public static string Tidied(int decided, int total)
    {
        return decided == total ? "Tidied " + total + (total == 1 ? " task" : " tasks") : "Tidied " + decided + " of " + total + " tasks";
    }
}
