namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// This PC found itself removed from its device group (ADR 54), or cut off by a removal: it has stopped syncing (the group's
/// key is dropped) and waits for the user's choice. Kept in the local document "sync.removed" until this PC joins a group
/// again or the user picks "Continue unsynced here".
/// </summary>
/// <param name="At">When this PC found out (its trusted clock).</param>
/// <param name="Removed">True when this PC was named in the rekey; false when it was left out of it (it had no public key to wrap the new code for) and is cut off all the same.</param>
/// <param name="RemovedByName">The name of the device that removed it, as that device wrote it in the rekey file.</param>
public sealed record UnlinkedState(DateTimeOffset At, bool Removed, string RemovedByName);
