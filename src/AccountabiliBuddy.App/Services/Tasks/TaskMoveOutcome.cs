namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>What a move into This session did: how many tasks went, and the ones that found no free place (the caller offers "This session is full": swap, or add anyway with a reason).</summary>
public sealed record TaskMoveOutcome(int Moved, IReadOnlyList<Guid> NeedsRoom);
