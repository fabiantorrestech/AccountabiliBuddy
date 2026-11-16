namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// A change of the device roles that this device has not applied yet (ADR 90), kept device-local with the roles document. Everything it holds
/// is this device's own bookkeeping: it is never sent.
/// </summary>
/// <param name="Target">
/// What the change comes to (the effective roles of the devices this device applied, and which of them are "the phones"). A new target is a new
/// change, so its wait starts again; the same target seen again (a fast clock's file re-read, a peer's repeat) never restarts it.
/// </param>
/// <param name="FirstSeen">When this device first saw this target (trusted time). A received or derived change counts its wait from here.</param>
/// <param name="RequestedAt">The time of the entry this device wrote for this change; null for a change it only received or derived. The Drive gate reads it.</param>
/// <param name="GateOpenAt">When the gate opened (or the request was seen, with nothing to confirm); null while it waits.</param>
/// <param name="Touched">The devices this PC's request wrote an entry for (a Cancel puts only these back); null for a received or derived change.</param>
/// <param name="ApplyAt">The earliest time the change may apply (the gate's opening plus the looser-change delay when the phones change); null while the gate waits.</param>
public sealed record DeviceRolesPending(
    string Target, DateTimeOffset FirstSeen, DateTimeOffset? RequestedAt = null, DateTimeOffset? GateOpenAt = null, DateTimeOffset? ApplyAt = null,
    IReadOnlyList<Guid>? Touched = null);
