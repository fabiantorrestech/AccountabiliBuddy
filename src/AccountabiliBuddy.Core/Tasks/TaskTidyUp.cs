using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The weekly tidy-up (ADR 76, spec 05 section 9), pure: when its card shows, which tasks it goes through, the suggestion for each
/// and applying a choice. It shows on the chosen day and stays until it is done or dismissed ("not this week"), counted per
/// week from the most recent chosen day; only on the Shelf, never during a session, and only while untouched tasks remain.
/// </summary>
public static class TaskTidyUp
{
    /// <summary>Untouched this many weeks or more: Archive is suggested.</summary>
    public const int ArchiveSuggestedWeeks = 8;

    /// <summary>Due within this many days (or overdue): Move to Next is suggested.</summary>
    public const int NextSuggestedDays = 7;

    /// <summary>The most recent <paramref name="day"/> that is not after <paramref name="today"/>: the start of this tidy-up's week.</summary>
    public static DateOnly PeriodStart(DateOnly today, DayOfWeek day)
    {
        int back = ((int)today.DayOfWeek - (int)day + 7) % 7;
        return today.AddDays(-back);
    }

    /// <summary>
    /// Whether the card shows: it is on, it is the tidy-up day or later in its week and not yet handled this week
    /// (<paramref name="handledFor"/> is the <see cref="PeriodStart"/> of the week it was finished or dismissed), no session
    /// runs, and some tasks are untouched.
    /// </summary>
    public static bool IsDue(bool enabled, DateOnly today, DayOfWeek day, DateOnly? handledFor, bool sessionActive, int untouchedCount)
    {
        return enabled && !sessionActive && untouchedCount > 0 && handledFor != PeriodStart(today, day);
    }

    public static TidyUpCard Card(int untouchedCount, int weeks, DayOfWeek day)
    {
        return new TidyUpCard(untouchedCount, weeks, day);
    }

    /// <summary>
    /// The suggestion for a task: Archive when untouched <see cref="ArchiveSuggestedWeeks"/> weeks or more, Move to Next when it is due
    /// within <see cref="NextSuggestedDays"/> days (or overdue), else Keep.
    /// </summary>
    public static TidyUpChoice Suggest(TaskItem task, DateTimeOffset now, DateOnly today)
    {
        if (TaskUntouched.Weeks(task, now) >= ArchiveSuggestedWeeks)
        {
            return TidyUpChoice.Archive;
        }

        return task.DueOn is DateOnly due && due <= today.AddDays(NextSuggestedDays) ? TidyUpChoice.MoveToNext : TidyUpChoice.Keep;
    }

    /// <summary>The tidy-up for the untouched tasks now; null when there are none.</summary>
    public static TidyUpPlan? Start(TaskLists lists, int weeks, DateTimeOffset now, DateOnly today)
    {
        IReadOnlyList<TaskItem> tasks = TaskUntouched.Of(lists, weeks, now);
        if (tasks.Count == 0)
        {
            return null;
        }

        List<TidyUpItem> items = tasks
            .Select(task => new TidyUpItem(task.Id, TaskUndoRules.Capture(lists, [task.Id], TaskActionKind.Edit, string.Empty), Suggest(task, now, today)))
            .ToList();
        return new TidyUpPlan(items, weeks);
    }

    /// <summary>
    /// Applies a choice to a task, from the task as it was when the tidy-up began: a decision changed later first puts the task back
    /// (and takes it out of History if it went there), so the new choice is the only one in effect. Keep resets its age, Move to Next
    /// puts it at the end of Next, Archive and Delete file it in History.
    /// </summary>
    public static TaskActionResult Apply(TaskLists lists, TidyUpItem item, TidyUpChoice choice, DateTimeOffset now, Guid self)
    {
        TaskLists reset = TaskUndoRules.Restore(lists, item.Original, self, now);
        TaskActionResult applied = choice switch
        {
            TidyUpChoice.MoveToNext when Bin(reset, item.TaskId) != TaskBin.Next => TaskActions.Move(reset, [item.TaskId], TaskBin.Next, now, self),
            TidyUpChoice.Archive => TaskActions.Archive(reset, [item.TaskId], now, self),
            TidyUpChoice.Delete => TaskActions.Delete(reset, [item.TaskId], now, self),
            _ => TaskActions.Touch(reset, item.TaskId, now),
        };
        return new TaskActionResult(applied.Lists, string.Empty, [item.TaskId]);
    }

    private static TaskBin? Bin(TaskLists lists, Guid id)
    {
        return lists.Tasks.FirstOrDefault(task => task.Id == id && task.Task.IsLive)?.Task.Bin;
    }
}
