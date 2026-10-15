namespace AccountabiliBuddy.Core.Ports;

public sealed record SyncPayload(PeerDevice From, ReadOnlyMemory<byte> Data);
