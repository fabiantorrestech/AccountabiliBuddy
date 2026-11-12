namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The toasts' lives (spec 05 section 14): 4 s plain, 6 s with Undo or an action, 7 s for an explanation (swap, undo), 8 s for "N more
/// reminders waiting". Pure.
/// </summary>
public static class TaskToastTiming
{
    public static TimeSpan For(TaskToastLength length)
    {
        return length switch
        {
            TaskToastLength.WithUndo => TimeSpan.FromSeconds(6),
            TaskToastLength.Explanation => TimeSpan.FromSeconds(7),
            TaskToastLength.Waiting => TimeSpan.FromSeconds(8),
            _ => TimeSpan.FromSeconds(4),
        };
    }

    /// <summary>The length of a toast that <paramref name="explains"/> something, else one with or without Undo.</summary>
    public static TaskToastLength LengthOf(bool canUndo, bool explains)
    {
        return explains ? TaskToastLength.Explanation : canUndo ? TaskToastLength.WithUndo : TaskToastLength.Plain;
    }
}
