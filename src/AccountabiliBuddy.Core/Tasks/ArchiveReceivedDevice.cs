using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>One other PC's task history as its last sync file carried it, an input of <see cref="ArchiveBuilder"/>.</summary>
public sealed record ArchiveReceivedDevice(Guid DeviceId, string Name, IReadOnlyList<SyncTaskHistoryEntry> Entries);
