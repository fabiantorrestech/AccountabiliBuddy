namespace AccountabiliBuddy.Relay;

/// <summary>Counts how many things (connections, requests, streams) one client address has open at once and refuses more than a maximum.</summary>
internal sealed class ConcurrencyGate
{
    private readonly int _maxPerKey;
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _open = new(StringComparer.Ordinal);

    public ConcurrencyGate(int maxPerKey)
    {
        _maxPerKey = maxPerKey;
    }

    /// <summary>A lease to dispose when the thing closes, or null when the address already has the maximum open.</summary>
    public IDisposable? TryEnter(string address)
    {
        lock (_gate)
        {
            _open.TryGetValue(address, out int count);
            if (count >= _maxPerKey)
            {
                return null;
            }

            _open[address] = count + 1;
            return new Lease(this, address);
        }
    }

    private void Leave(string address)
    {
        lock (_gate)
        {
            if (_open.TryGetValue(address, out int count))
            {
                if (count <= 1)
                {
                    _open.Remove(address);
                }
                else
                {
                    _open[address] = count - 1;
                }
            }
        }
    }

    private sealed class Lease : IDisposable
    {
        private readonly ConcurrencyGate _owner;
        private readonly string _address;
        private int _released;

        public Lease(ConcurrencyGate owner, string address)
        {
            _owner = owner;
            _address = address;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _owner.Leave(_address);
            }
        }
    }
}
