namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The days a read covered for one goal and source (<see cref="GoalDerivedWriter.Changes"/>): inside it, this device's own derived entry
/// for a day the read no longer has is removed (a tombstone); outside it nothing of this device's is touched.
/// </summary>
/// <param name="From">The first day covered.</param>
/// <param name="To">The last day covered (included).</param>
public sealed record GoalDerivedCover(Guid GoalId, GoalEntrySource Source, DateOnly From, DateOnly To);
