namespace AccountabiliBuddy.Core.Sync;

/// <summary>What a rekey file says (ADR 54), once opened with the old group key.</summary>
/// <param name="Epoch">When the rekey was made, in seconds since 1970-01-01 UTC. Tells rekeys apart; never compared across devices' clocks to refuse one.</param>
/// <param name="Removed">The devices this rekey removes from the group.</param>
/// <param name="Wraps">The new link code's secret wrapped for each remaining device that had a public key.</param>
public sealed record RekeyPayload(long Epoch, IReadOnlyList<RekeyRemovedDevice> Removed, IReadOnlyList<RekeyWrapRecord> Wraps);
