namespace AccountabiliBuddy.Core.Domain;

/// <summary>A step of a <see cref="SessionTask"/> in a guided session's checklist.</summary>
/// <param name="DoneAt">When it was ticked; null while it's open.</param>
public sealed record SessionSubtask(Guid Id, string Title, DateTimeOffset? DoneAt)
{
    public bool IsDone => DoneAt is not null;
}
