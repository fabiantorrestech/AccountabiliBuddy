namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// What an undo did: whether it put anything back, the tasks that were in This session but found no room there and stayed in Queued,
/// and the touched tasks it left alone because they changed since (held by the session, a newer History entry, gone another way).
/// </summary>
public sealed record TaskUndoOutcome(bool Undone, IReadOnlyList<Guid> QueuedNoRoom, IReadOnlyList<Guid> ChangedSince);
