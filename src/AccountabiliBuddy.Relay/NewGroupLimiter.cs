namespace AccountabiliBuddy.Relay;

/// <summary>
/// At most <see cref="RelayLimits.MaxNewGroupsPerIpPerDay"/> new groups per client address in any 24 hours, so one
/// client can't use up the relay's group slots (the first writer of a group id owns it). Counts only, in memory.
/// </summary>
internal sealed class NewGroupLimiter
{
    private const int PruneAbove = 10_000;
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private readonly object _gate = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _created = new(StringComparer.Ordinal);

    public bool HasRoom(string address, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _created.TryGetValue(address, out List<DateTimeOffset>? times)
                ? times.Count(t => now - t < Window) < RelayLimits.MaxNewGroupsPerIpPerDay
                : true;
        }
    }

    public void Record(string address, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_created.Count > PruneAbove)
            {
                Prune(now);
            }

            if (!_created.TryGetValue(address, out List<DateTimeOffset>? times))
            {
                times = new List<DateTimeOffset>();
                _created[address] = times;
            }

            times.RemoveAll(t => now - t >= Window);
            times.Add(now);
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (string address in _created.Where(c => c.Value.All(t => now - t >= Window)).Select(c => c.Key).ToList())
        {
            _created.Remove(address);
        }
    }
}
