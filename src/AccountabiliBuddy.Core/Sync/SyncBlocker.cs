namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// One of the device's blocks (ADR 37): a Curbox group or blocker on a phone, a Cold Turkey block on a PC, by
/// <see cref="Kind"/> (a <see cref="Domain.BlockRef"/> kind), the blocker's own <see cref="Id"/>, its name, whether it's
/// on, and whether its blocker keeps it as it is (a locked Cold Turkey block). Names only, never what a block holds.
/// </summary>
public sealed record SyncBlocker(string Kind, string Id, string Name, bool On, bool Locked = false);
