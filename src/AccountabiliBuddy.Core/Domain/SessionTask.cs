namespace AccountabiliBuddy.Core.Domain;

/// <summary>A task in a guided session's checklist (ADR 24), with its subtasks.</summary>
/// <param name="DoneAt">When it was ticked, or when its last subtask was; null while it's open.</param>
public sealed record SessionTask(Guid Id, string Title, DateTimeOffset? DoneAt, IReadOnlyList<SessionSubtask> Subtasks)
{
    public bool IsDone => DoneAt is not null;
}
