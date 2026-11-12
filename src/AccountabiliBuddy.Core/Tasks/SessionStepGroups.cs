using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// How a running session's task with steps reads in the Grouped view (Tasks redesign `05` §2, Implementation Notes "Session slots: picking
/// and order"): its done steps folded into "done (a–b, d)", the next open steps after the focused ones greyed, "+N more steps", and the
/// open steps passed over ("skipped": open, not in focus, and before a step that is done or in focus). Letters follow the step's place.
/// </summary>
public static class SessionStepGroups
{
    /// <summary>How many open steps after the focused ones the Grouped view shows greyed.</summary>
    public const int NextShown = 2;

    /// <summary>"a–b, d" for the done steps (a run of two or more joined with an en dash); empty when none is done.</summary>
    public static string DoneLetters(SessionTask task)
    {
        var done = new List<int>();
        for (int index = 0; index < task.Subtasks.Count; index++)
        {
            if (task.Subtasks[index].IsDone)
            {
                done.Add(index);
            }
        }

        var parts = new List<string>();
        int at = 0;
        while (at < done.Count)
        {
            int end = at;
            while (end + 1 < done.Count && done[end + 1] == done[end] + 1)
            {
                end++;
            }

            if (end > at)
            {
                parts.Add(TaskStepLetters.Of(done[at]) + "–" + TaskStepLetters.Of(done[end]));
            }
            else
            {
                for (int index = at; index <= end; index++)
                {
                    parts.Add(TaskStepLetters.Of(done[index]));
                }
            }

            at = end + 1;
        }

        return string.Join(", ", parts);
    }

    /// <summary>The open steps passed over: not in focus, and placed before the last step that is done or in focus.</summary>
    public static IReadOnlySet<Guid> Skipped(SessionTask task, IReadOnlyCollection<Guid>? focused)
    {
        int last = -1;
        for (int index = 0; index < task.Subtasks.Count; index++)
        {
            SessionSubtask step = task.Subtasks[index];
            if (step.IsDone || focused?.Contains(step.Id) == true)
            {
                last = index;
            }
        }

        var skipped = new HashSet<Guid>();
        for (int index = 0; index < last; index++)
        {
            SessionSubtask step = task.Subtasks[index];
            if (!step.IsDone && focused?.Contains(step.Id) != true)
            {
                skipped.Add(step.Id);
            }
        }

        return skipped;
    }

    /// <summary>The open steps after the last focused or done one, not in focus, in order: the first <see cref="NextShown"/> show greyed, the rest as "+N more steps".</summary>
    public static IReadOnlyList<SessionSubtask> Upcoming(SessionTask task, IReadOnlyCollection<Guid>? focused)
    {
        IReadOnlySet<Guid> skipped = Skipped(task, focused);
        return task.Subtasks.Where(step => !step.IsDone && focused?.Contains(step.Id) != true && !skipped.Contains(step.Id)).ToList();
    }
}
