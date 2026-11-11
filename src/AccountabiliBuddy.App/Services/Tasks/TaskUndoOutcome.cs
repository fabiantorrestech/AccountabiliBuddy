namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>What an undo did: whether there was anything to undo, and the tasks that were in This session but found no room there and stayed in Queued.</summary>
public sealed record TaskUndoOutcome(bool Undone, IReadOnlyList<Guid> QueuedNoRoom);
