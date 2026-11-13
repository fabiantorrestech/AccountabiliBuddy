using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>Everything <see cref="ArchiveBuilder"/> builds the Archive's tasks from; the same on every device.</summary>
/// <param name="Own">This device's own task history (ended sessions and "done outside a session" records; drafts left out by the caller or ignored here).</param>
/// <param name="Received">The other PCs' task histories.</param>
/// <param name="Kept">Past tasks of other PCs kept on this device as local items.</param>
/// <param name="Settled">What was done with past tasks on any device (ADR 72's shared settle records).</param>
/// <param name="Lists">The one task list (ADR 76): its finished tasks and its History log.</param>
/// <param name="Now">Now, from the trusted clock.</param>
public sealed record ArchiveInput(
    IReadOnlyList<SyncTaskHistoryEntry> Own,
    IReadOnlyList<ArchiveReceivedDevice> Received,
    IReadOnlyList<SyncKeptPast> Kept,
    IReadOnlyList<SyncPastSettled> Settled,
    TaskLists Lists,
    DateTimeOffset Now);
