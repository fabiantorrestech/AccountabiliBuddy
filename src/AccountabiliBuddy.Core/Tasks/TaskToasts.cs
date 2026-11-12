using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The toast text of each task action (ADR 76, spec 05 section 14). They hold the user's task titles: shown on screen, never logged.
/// </summary>
public static class TaskToasts
{
    private const int ShortLength = 28;

    public const string Undone = "Undone";

    /// <summary>Undo left everything alone: each task it touched changed since (the session holds it, History has it again, it went another way).</summary>
    public const string ChangedSince = "Can't undo · it changed since";

    /// <summary>Undo put back some of what the action touched; the rest changed since.</summary>
    public const string PartlyChangedSince = "Undone · some had changed since";

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
        return tasks.Count == 1 ? FinishedTitle(tasks[0].Title) : tasks.Count + " tasks finished";
    }

    /// <summary>"Finished “Draft Q4…”": the toast a row's tick shows at once (the same text the stored finish raises).</summary>
    public static string FinishedTitle(string title)
    {
        return "Finished “" + Short(title) + "”";
    }

    /// <summary>Copy ID: "Copied 142 · Draft Q4…" (<paramref name="copied"/> is what went on the clipboard, "142 · title").</summary>
    public static string Copied(string copied)
    {
        return "Copied " + Short(copied);
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

    /// <summary>A task typed in the add box or jotted down: "Added “Fix tray icon” to To sort".</summary>
    public static string Added(TaskItem task)
    {
        return "Added “" + Short(task.Title) + "” to " + TaskBins.Name(task.Bin);
    }

    /// <summary>An old list's delete that was ignored because another device moved the task since (nothing was deleted).</summary>
    public static string MovedElsewhere(TaskItem task)
    {
        return "“" + Short(task.Title) + "” was moved on another device, so it wasn't deleted";
    }

    /// <summary>"Reminder added", with "· also on Google Calendar" when the task goes there.</summary>
    public static string ReminderAdded(TaskItem task)
    {
        return task.Calendar ? "Reminder added · also on Google Calendar" : "Reminder added";
    }

    public static string ReminderRemoved => "Reminder removed";

    public static string CantMiss(bool on)
    {
        return on ? "Can’t miss on · it rings full screen" : "Can’t miss off";
    }

    /// <summary>"Done by Fri 11/13", "Done by Fri 11/13 5:00 PM" (always 12-hour), or "Due date removed".</summary>
    public static string Due(DateOnly? on, TimeOnly? at)
    {
        if (on is not DateOnly day)
        {
            return "Due date removed";
        }

        string text = "Done by " + day.ToString("ddd M/d", System.Globalization.CultureInfo.InvariantCulture);
        return at is TimeOnly time ? text + " " + TaskSnooze.Clock(time) : text;
    }

    public static string Calendar(bool on)
    {
        return on ? "Added to Google Calendar" : "Taken off Google Calendar";
    }

    public static string Tidied(int decided, int total)
    {
        return decided == total ? "Tidied " + total + (total == 1 ? " task" : " tasks") : "Tidied " + decided + " of " + total + " tasks";
    }
}
