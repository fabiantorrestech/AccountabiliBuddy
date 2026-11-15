namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// One task that carries a goal (GS5, ADR 76 amended): what the goal sources "a task carries this goal" and "milestone tasks" and the goal
/// page's task list read, taken from the task list (<see cref="GoalTaskLinks.From"/>) and never stored or synced on its own.
/// </summary>
/// <param name="TaskId">The task's id (the id of its checklist task in a session).</param>
/// <param name="GoalId">The goal it carries (a goal that may no longer exist: nothing counts for it then).</param>
/// <param name="Title">The task's title, for the goal page's list only. Private text: never logged, never stored here.</param>
/// <param name="DoneAt">When it was finished; null while open.</param>
public sealed record GoalTaskLink(Guid TaskId, Guid GoalId, string Title, DateTimeOffset? DoneAt)
{
    public bool IsDone => DoneAt is not null;
}
