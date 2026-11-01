using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>The group's key was replaced in place (ADR 54): the key before, and the devices removed with the switch.</summary>
/// <param name="OldKey">The key the group had; names the old files each sync method clears.</param>
/// <param name="RemovedDeviceIds">The devices the rekey removed.</param>
public sealed record SyncKeySwitchedEventArgs(SyncKey OldKey, IReadOnlyList<Guid> RemovedDeviceIds);
