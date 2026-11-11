namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// What to write so the calendar matches the tasks (ADR 78): compares the events the tasks want with the ones this PC recorded.
/// Only recorded events are ever updated or deleted, so nothing the app didn't make is touched.
/// </summary>
public static class TaskCalendarDiff
{
    /// <param name="keptElsewhere">Tasks that still want their event but are kept on other devices only (<see cref="TaskCalendarMapping.KeptElsewhereIds"/>).</param>
    /// <returns>
    /// The writes, forgets and deletes first, then updates, then creates (each by task id): a recorded event of a task kept elsewhere is
    /// forgotten (record dropped, event left for the devices that keep the task: they share its id); any other recorded event no task
    /// wants any more is deleted (finished, archived, deleted, switched off, no due date and no reminder left); one whose calendar
    /// changed is deleted there and created in the new one; one whose content changed is updated; a wanted event with no record is created.
    /// </returns>
    public static IReadOnlyList<TaskCalendarAction> Plan(
        IReadOnlyDictionary<Guid, TaskCalendarWanted> wanted,
        IReadOnlyDictionary<Guid, TaskCalendarRecord> recorded,
        IReadOnlySet<Guid> keptElsewhere)
    {
        var deletes = new List<TaskCalendarAction>();
        var updates = new List<TaskCalendarAction>();
        var creates = new List<TaskCalendarAction>();

        foreach ((Guid taskId, TaskCalendarRecord record) in recorded.OrderBy(pair => pair.Key))
        {
            if (!wanted.TryGetValue(taskId, out TaskCalendarWanted? want))
            {
                TaskCalendarActionKind kind = keptElsewhere.Contains(taskId) ? TaskCalendarActionKind.Forget : TaskCalendarActionKind.Delete;
                deletes.Add(new TaskCalendarAction(kind, taskId, record, null));
            }
            else if (want.CalendarKey != record.CalendarKey)
            {
                deletes.Add(new TaskCalendarAction(TaskCalendarActionKind.Delete, taskId, record, null));
                creates.Add(new TaskCalendarAction(TaskCalendarActionKind.Create, taskId, null, want));
            }
            else if (want.Signature != record.Signature)
            {
                updates.Add(new TaskCalendarAction(TaskCalendarActionKind.Update, taskId, record, want));
            }
        }

        foreach ((Guid taskId, TaskCalendarWanted want) in wanted.OrderBy(pair => pair.Key))
        {
            if (!recorded.ContainsKey(taskId))
            {
                creates.Add(new TaskCalendarAction(TaskCalendarActionKind.Create, taskId, null, want));
            }
        }

        return [.. deletes, .. updates, .. creates.OrderBy(action => action.TaskId)];
    }
}
