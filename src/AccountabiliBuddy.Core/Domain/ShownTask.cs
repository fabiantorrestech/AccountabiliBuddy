namespace AccountabiliBuddy.Core.Domain;

/// <summary>The checklist's current task as the on-screen window's bar shows it, with its next open subtask (null: none).</summary>
public sealed record ShownTask(SessionTask Task, SessionSubtask? Subtask)
{
    /// <summary>E.g. "Fix tests › Integration", or just "Write intro".</summary>
    public string Text => Subtask is null ? Task.Title : $"{Task.Title} › {Subtask.Title}";
}
