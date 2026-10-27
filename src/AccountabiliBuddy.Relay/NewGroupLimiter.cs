namespace AccountabiliBuddy.Relay;

/// <summary>
/// At most <see cref="RelayLimits.MaxNewGroupsPerIpPerDay"/> new groups per client address in any 24 hours, so one
/// client can't use up the relay's group slots (the first writer of a group id owns it). Counts only, in memory. A
/// slot is reserved when the check passes (check and record are one step, so a burst of parallel first writes can't all
/// pass) and given back when the group wasn't made after all.
/// </summary>
internal sealed class NewGroupLimiter
{
    private const int PruneAbove = 10_000;
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private readonly object _gate = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _created = new(StringComparer.Ordinal);

    /// <returns>False when the address already made its share in the last 24 hours (nothing is reserved).</returns>
    public bool TryReserve(string address, DateTimeOffset now)
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
            if (times.Count >= RelayLimits.MaxNewGroupsPerIpPerDay)
            {
                return false;
            }

            times.Add(now);
            return true;
        }
    }

    /// <summary>Gives back the newest reservation of an address (the write that held it did not make a group).</summary>
    public void Release(string address)
    {
        lock (_gate)
        {
            if (_created.TryGetValue(address, out List<DateTimeOffset>? times) && times.Count > 0)
            {
                times.RemoveAt(times.Count - 1);
            }
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
