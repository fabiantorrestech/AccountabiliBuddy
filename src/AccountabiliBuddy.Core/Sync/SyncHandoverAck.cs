namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The keeper's receipt for a handover it took in (ADR 72): the item's id and the edit time it arrived with. Its sender stops
/// carrying the handover once a receipt at least as new shows in the keeper's file. Kept and written for 30 days.
/// </summary>
public sealed record SyncHandoverAck(Guid Id, DateTimeOffset EditedAt);
