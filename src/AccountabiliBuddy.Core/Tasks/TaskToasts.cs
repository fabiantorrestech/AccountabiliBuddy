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

    /// <summary>"Linked to Spanish" / "Link removed" (GS5); a goal with no name says "Linked to a goal".</summary>
    public static string Linked(string? goalName)
    {
        return goalName is null ? "Link removed" : goalName.Length == 0 ? "Linked to a goal" : "Linked to " + goalName;
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

    // ---- Tasks redesign 8a: the right-click menu's and the select bar's toasts, which count the tasks ("1 task", "3 tasks") ----

    /// <summary>"1 task" or "3 tasks".</summary>
    public static string Count(int count)
    {
        return count == 1 ? "1 task" : count + " tasks";
    }

    /// <summary>"Moved 3 tasks to Later"; <paramref name="binName"/> is the name the menu showed ("Today" for Queued).</summary>
    public static string MovedCount(int count, string binName)
    {
        return "Moved " + Count(count) + " to " + binName;
    }

    /// <summary>"Archived 3 tasks" (Tasks redesign 8b: a stack dropped on Group by None's Archive zone; Archive › Unfinished keeps them).</summary>
    public static string ArchivedCount(int count)
    {
        return "Archived " + Count(count);
    }

    /// <summary>"Reordered 3 tasks" (Tasks redesign 8b: a stack dropped on its own column).</summary>
    public static string ReorderedCount(int count)
    {
        return "Reordered " + Count(count);
    }

    /// <summary>"Marked 3 tasks done".</summary>
    public static string MarkedDone(int count)
    {
        return "Marked " + Count(count) + " done";
    }

    /// <summary>"Deleted 3 tasks · in the Archive for 30 days" (Archive › Recently deleted keeps them).</summary>
    public static string DeletedToArchive(int count)
    {
        return "Deleted " + Count(count) + " · in the Archive for 30 days";
    }

    /// <summary>"Labelled 3 tasks Work - AI" (the label's full name); null = "Labelled 3 tasks No label".</summary>
    public static string LabelledCount(int count, string? labelName)
    {
        return "Labelled " + Count(count) + " " + (labelName ?? "No label");
    }

    /// <summary>"Highlighted 3 tasks" / "Took the highlight off 3 tasks".</summary>
    public static string HighlightedCount(int count, bool on)
    {
        return (on ? "Highlighted " : "Took the highlight off ") + Count(count);
    }

    /// <summary>"Added #docs to 3 tasks" / "Took #docs off 3 tasks".</summary>
    public static string TagCount(int count, string tag, bool added)
    {
        return added ? "Added #" + tag + " to " + Count(count) : "Took #" + tag + " off " + Count(count);
    }

    /// <summary>"Time limit 25 min for 3 tasks" / "No time limit for 3 tasks".</summary>
    public static string TimeLimitCount(int count, int? minutes)
    {
        return minutes is int value ? "Time limit " + value + " min for " + Count(count) : "No time limit for " + Count(count);
    }

    /// <summary>"Linked 3 tasks to Spanish" / "Unlinked 3 tasks"; a goal with no name says "a goal".</summary>
    public static string LinkedCount(int count, string? goalName)
    {
        if (goalName is null)
        {
            return "Unlinked " + Count(count);
        }

        return "Linked " + Count(count) + " to " + (goalName.Length == 0 ? "a goal" : goalName);
    }

    /// <summary>"3 tasks sync to all your devices" (one: "1 task syncs to all your devices").</summary>
    public static string SyncedEverywhere(int count)
    {
        return Count(count) + (count == 1 ? " syncs" : " sync") + " to all your devices";
    }

    /// <summary>"Kept 3 tasks on this PC only" / "Kept 3 tasks on Pixel only".</summary>
    public static string KeptOn(int count, string where)
    {
        return "Kept " + Count(count) + " on " + where + " only";
    }

    public static string Tidied(int decided, int total)
    {
        return decided == total ? "Tidied " + total + (total == 1 ? " task" : " tasks") : "Tidied " + decided + " of " + total + " tasks";
    }
}
