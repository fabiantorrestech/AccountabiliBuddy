namespace AccountabiliBuddy.Core.Sync;

/// <summary>A sync file received from another device with the receiver's own trusted time of its arrival (ADR 85, PS1a review B2).</summary>
/// <param name="File">The newest file (merged copy) of that device.</param>
/// <param name="ReceivedAt">When it arrived, on the receiver's trusted clock; never the sender's stamp.</param>
public sealed record ReceivedSyncFile(SyncFile File, DateTimeOffset ReceivedAt);
