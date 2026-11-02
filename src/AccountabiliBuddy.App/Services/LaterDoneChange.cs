using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// What another device's Later list changed about ticked-off items (ADR 50), as <see cref="LaterStore.MergeAsync"/> found it:
/// items that became done with this merge, and items that were done and are open again. Only a change makes one (a restart or
/// a merge that brings nothing new makes none), so a record is made once per tick.
/// </summary>
/// <param name="NewlyDone">Items ticked off on another device (with their text, which stays on the list for 90 days).</param>
/// <param name="Reopened">Ids of items that were done here and are open again (an Undo, or a newer edit on another device).</param>
public sealed record LaterDoneChange(IReadOnlyList<SyncLaterItem> NewlyDone, IReadOnlyList<Guid> Reopened);
