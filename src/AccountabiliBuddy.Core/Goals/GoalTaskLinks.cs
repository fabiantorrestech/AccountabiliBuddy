using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Reads the goal links off the task list (GS5): every live task with a <see cref="TaskItem.GoalId"/>, plus the tasks History still holds as
/// finished (<see cref="TaskHistoryEntry.Kept"/>, 90 days), so a milestone keeps counting a task once it left the list. A task in both counts once
/// (the list's copy). A goal id no live goal has is kept here; whoever shows the link ignores it.
/// </summary>
public static class GoalTaskLinks
{
    public static IReadOnlyList<GoalTaskLink> From(TaskLists lists)
    {
        var links = new List<GoalTaskLink>();
        var seen = new HashSet<Guid>();
        foreach (TaskItem task in lists.Tasks.Select(item => item.Task).Where(task => task.IsLive))
        {
            seen.Add(task.Id);
            if (task.GoalId is Guid goalId)
            {
                links.Add(new GoalTaskLink(task.Id, goalId, task.Title, task.DoneAt));
            }
        }

        foreach (TaskHistoryEntry entry in lists.Log.Select(item => item.Entry).Where(entry => entry.IsLive && entry.Kind == TaskHistoryKind.Finished))
        {
            if (entry.Task.GoalId is Guid goalId && seen.Add(entry.Task.Id))
            {
                links.Add(new GoalTaskLink(entry.Task.Id, goalId, entry.Task.Title, entry.Task.DoneAt ?? entry.When));
            }
        }

        return links;
    }
}
