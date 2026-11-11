using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>A search hit: a task, or one of its steps (<paramref name="Step"/> and its 0-based place, so "142b" can be shown as such).</summary>
public sealed record TaskSearchHit(TaskItem Task, TaskStep? Step, int? StepIndex, TaskSearchMatch Match);
