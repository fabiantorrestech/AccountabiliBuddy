namespace AccountabiliBuddy.Relay;

/// <summary>
/// The per-client-address limits that keep one client from using up what everyone shares (the global caps in
/// <see cref="RelayLimits"/> stay as a backstop): open connections, requests in flight, event streams, new groups a day.
/// </summary>
internal sealed class ClientLimits
{
    public ClientLimits(bool behindProxy)
    {
        BehindProxy = behindProxy;
    }

    public bool BehindProxy { get; }

    /// <summary>Open TCP connections. Only meaningful when clients connect directly; behind a proxy every connection is the proxy's.</summary>
    public ConcurrencyGate Connections { get; } = new(RelayLimits.MaxConnectionsPerIp);

    /// <summary>Requests in flight, which behind a proxy stands in for open connections.</summary>
    public ConcurrencyGate Requests { get; } = new(RelayLimits.MaxConnectionsPerIp);

    public ConcurrencyGate Streams { get; } = new(RelayLimits.MaxStreamsPerIp);

    public NewGroupLimiter NewGroups { get; } = new();
}
