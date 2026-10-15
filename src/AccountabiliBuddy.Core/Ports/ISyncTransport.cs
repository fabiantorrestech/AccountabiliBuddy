namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Device-to-device transport for syncing profiles and sessions (ADR 2: no cloud).
/// Planned implementations: LAN first, then Bluetooth; NAS and USB later. Not implemented in the MVP.
/// </summary>
public interface ISyncTransport
{
    string Name { get; }

    event EventHandler<SyncPayload>? PayloadReceived;

    Task<IReadOnlyList<PeerDevice>> DiscoverPeersAsync(CancellationToken cancellationToken = default);

    Task SendAsync(PeerDevice peer, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}
