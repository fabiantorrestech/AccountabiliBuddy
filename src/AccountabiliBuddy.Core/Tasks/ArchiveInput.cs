using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>Everything <see cref="ArchiveBuilder"/> builds the Archive's tasks from; the same on every device.</summary>
/// <param name="Own">This device's own task history (ended sessions and "done outside a session" records; drafts left out by the caller or ignored here).</param>
/// <param name="Received">The other PCs' task histories.</param>
/// <param name="Kept">Past tasks of other PCs kept on this device as local items.</param>
/// <param name="Settled">What was done with past tasks on any device (ADR 72's shared settle records).</param>
/// <param name="Lists">The one task list (ADR 76): its finished tasks and its History log.</param>
/// <param name="Now">Now, from the trusted clock.</param>
/// <param name="Notes">Every device's session notes (ADR 87) as the store holds them, tombstones included; null for none (a device with no notes store).</param>
/// <param name="Self">This device's id: a note by it (or by none yet) isn't marked "from".</param>
/// <param name="DeviceNames">The last known name of each other device by id (removed devices whose data was kept included); an id missing here reads "another device".</param>
/// <param name="Snapshots">Past tasks deleted on any device that this device still holds a copy of (its own Recently deleted); lets a delete whose task is in no history here still be listed, with its text.</param>
/// <param name="Running">Tasks of the running session's checklist: one ticked there is in the task list's finished tasks but isn't "From your task list" (its session's own row takes it when the session ends); null for none.</param>
public sealed record ArchiveInput(
    IReadOnlyList<SyncTaskHistoryEntry> Own,
    IReadOnlyList<ArchiveReceivedDevice> Received,
    IReadOnlyList<SyncKeptPast> Kept,
    IReadOnlyList<SyncPastSettled> Settled,
    TaskLists Lists,
    DateTimeOffset Now,
    IReadOnlySet<Guid>? Running = null,
    IReadOnlyList<SyncSessionNote>? Notes = null,
    Guid Self = default,
    IReadOnlyDictionary<Guid, string>? DeviceNames = null,
    IReadOnlyList<SessionTask>? Snapshots = null);
