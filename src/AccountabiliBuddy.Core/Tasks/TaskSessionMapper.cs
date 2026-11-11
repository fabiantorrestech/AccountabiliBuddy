using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Between a task of the shared list (ADR 76) and the running checklist's shape (<see cref="SessionTask"/>, ADR 24), which stays
/// as it is: a session's checklist is made from the tasks in This session, and its ticks and edits go back to them. Nothing the
/// checklist can't carry (reminders, highlight, due date, number, devices, focus, step estimates) is lost on the way back.
/// </summary>
public static class TaskSessionMapper
{
    /// <summary>
    /// The task as a checklist task: its steps as subtasks, its label as the category (id, name and opaque colour), its tags as
    /// ADR 72 labels and a single keeping device as <see cref="SessionTask.LocalTo"/>.
    /// </summary>
    public static SessionTask ToSessionTask(TaskItem task, IReadOnlyList<TaskLabel> labels)
    {
        TaskLabel? label = LabelOf(task.LabelId, labels);
        return new SessionTask(
            task.Id,
            task.Title,
            task.DoneAt,
            (task.Steps ?? []).Select(step => step.ToSubtask()).ToList(),
            CategoryId: label?.Id,
            Category: label?.Name,
            CategoryColor: label is null ? null : TaskLabelColors.Opaque(label.Color),
            EstimateMinutes: task.EstimateMinutes,
            Note: task.Note,
            PlannedFor: task.PlannedFor,
            Labels: TaskLabels.Clean(task.Tags),
            LocalTo: task.LocalTo);
    }

    /// <summary>
    /// <paramref name="target"/> with what <paramref name="checklistTask"/> carries: title, done time, steps (each step keeps its
    /// focus and estimate by id), label (by id, else by name), estimate, note, plan and tags. Everything else stays.
    /// </summary>
    public static TaskItem Apply(TaskItem target, SessionTask checklistTask, IReadOnlyList<TaskLabel> labels)
    {
        return target with
        {
            Title = checklistTask.Title,
            DoneAt = checklistTask.DoneAt,
            Steps = StepsFrom(checklistTask.Subtasks, target.Steps),
            LabelId = LabelIdFrom(checklistTask, labels, target.LabelId),
            EstimateMinutes = checklistTask.EstimateMinutes,
            Note = checklistTask.Note,
            PlannedFor = checklistTask.PlannedFor,
            Tags = TaskLabels.Clean(checklistTask.Labels),
        };
    }

    /// <summary>
    /// The old subtasks as steps: a step with the same id keeps its focus and estimate, a new one starts without them, and steps
    /// the subtasks no longer list are gone. Null for none.
    /// </summary>
    public static IReadOnlyList<TaskStep>? StepsFrom(IReadOnlyList<SessionSubtask>? subtasks, IReadOnlyList<TaskStep>? existing)
    {
        if (subtasks is null || subtasks.Count == 0)
        {
            return null;
        }

        Dictionary<Guid, TaskStep> byId = (existing ?? []).GroupBy(step => step.Id).ToDictionary(group => group.Key, group => group.First());
        return subtasks
            .Select(subtask => byId.TryGetValue(subtask.Id, out TaskStep? held)
                ? held with { Title = subtask.Title, DoneAt = subtask.DoneAt }
                : new TaskStep(subtask.Id, subtask.Title, subtask.DoneAt))
            .ToList();
    }

    // A category id the labels don't know (an old category's own id) with no name says nothing new: the task keeps its label.
    private static Guid? LabelIdFrom(SessionTask checklistTask, IReadOnlyList<TaskLabel> labels, Guid? held)
    {
        if (checklistTask.CategoryId is Guid id && labels.Any(label => label.Id == id && label.IsLive))
        {
            return id;
        }

        if (checklistTask.Category is string name && name.Trim().Length > 0)
        {
            return labels.FirstOrDefault(label => label.IsLive && TaskLabel.SameName(label.Name, name))?.Id ?? TaskLabel.IdFor(name);
        }

        return checklistTask.CategoryId is null ? null : held;
    }

    private static TaskLabel? LabelOf(Guid? id, IReadOnlyList<TaskLabel> labels)
    {
        return id is Guid labelId ? labels.FirstOrDefault(label => label.Id == labelId && label.IsLive) : null;
    }
}
