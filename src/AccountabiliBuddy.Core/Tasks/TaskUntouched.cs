using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// "Untouched" (ADR 76, spec 05 section 9): a task nobody edited, moved, ticked a step of or opened for N weeks, by its
/// <see cref="TaskItem.TouchedAt"/>. N is a setting from <see cref="MinWeeks"/> to <see cref="MaxWeeks"/>, <see cref="DefaultWeeks"/> by default.
/// </summary>
public static class TaskUntouched
{
    public const int MinWeeks = 1;
    public const int MaxWeeks = 4;
    public const int DefaultWeeks = 3;

    /// <summary>Opening a task touches it only when it was not touched within this long (spares sync).</summary>
    public static readonly TimeSpan TouchThrottle = TimeSpan.FromHours(1);

    /// <summary>The task was touched within <see cref="TouchThrottle"/>: opening it need not touch it again.</summary>
    public static bool TouchedRecently(TaskItem task, DateTimeOffset now)
    {
        return now - task.TouchedAt < TouchThrottle;
    }

    public static int ClampWeeks(int weeks)
    {
        return Math.Clamp(weeks, MinWeeks, MaxWeeks);
    }

    /// <summary>Whole weeks since the task was last touched (0 for a task touched this week).</summary>
    public static int Weeks(TaskItem task, DateTimeOffset now)
    {
        return Math.Max(0, (int)Math.Floor((now - task.TouchedAt).TotalDays / 7));
    }

    /// <summary>The task was last touched at least <paramref name="weeks"/> weeks ago (any bin: the "Untouched for" filter).</summary>
    public static bool IsOlderThan(TaskItem task, int weeks, DateTimeOffset now)
    {
        return now - task.TouchedAt >= TimeSpan.FromDays(7 * weeks);
    }

    /// <summary>An open Shelf task that is <see cref="IsOlderThan"/> (the weekly tidy-up's tasks).</summary>
    public static bool IsUntouched(TaskItem task, int weeks, DateTimeOffset now)
    {
        return task.IsLive && !task.IsDone && TaskBins.IsShelf(task.Bin) && IsOlderThan(task, weeks, now);
    }

    /// <summary>The untouched Shelf tasks, the longest untouched first (the tidy-up's order), then by number.</summary>
    public static IReadOnlyList<TaskItem> Of(TaskLists lists, int weeks, DateTimeOffset now)
    {
        return lists.Tasks.Select(item => item.Task)
            .Where(task => IsUntouched(task, weeks, now))
            .OrderBy(task => task.TouchedAt)
            .ThenBy(task => task.Number?.Value ?? int.MaxValue)
            .ThenBy(task => task.Id)
            .ToList();
    }
}
