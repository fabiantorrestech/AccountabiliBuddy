namespace AccountabiliBuddy.App;

/// <summary>
/// Lets a second launch reach the copy that is already running, using one named event per signal
/// ("Local\" = this Windows sign-in only). Events are auto-reset, so a signal sent while the running copy is
/// still starting up is delivered once it starts listening.
/// </summary>
public sealed class InstanceSignals : IDisposable
{
    private readonly Dictionary<InstanceSignal, EventWaitHandle> _events;
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _thread;
    private readonly Action<InstanceSignal> _onSignal;

    private InstanceSignals(string instanceName, Action<InstanceSignal> onSignal)
    {
        _onSignal = onSignal;
        _events = Enum.GetValues<InstanceSignal>().ToDictionary(signal => signal, signal => Open(instanceName, signal));
        _thread = new Thread(Listen) { IsBackground = true, Name = "AccountabiliBuddy instance signals" };
        _thread.Start();
    }

    /// <summary>Starts listening. <paramref name="onSignal"/> runs on a background thread.</summary>
    public static InstanceSignals Listen(string instanceName, Action<InstanceSignal> onSignal)
    {
        return new InstanceSignals(instanceName, onSignal);
    }

    public static void Send(string instanceName, InstanceSignal signal)
    {
        using EventWaitHandle handle = Open(instanceName, signal);
        handle.Set();
    }

    public void Dispose()
    {
        _stop.Set();
        _thread.Join();
        foreach (EventWaitHandle handle in _events.Values)
        {
            handle.Dispose();
        }

        _stop.Dispose();
    }

    private void Listen()
    {
        InstanceSignal[] signals = _events.Keys.ToArray();
        WaitHandle[] handles = signals.Select(signal => (WaitHandle)_events[signal]).Append(_stop).ToArray();

        while (true)
        {
            int index = WaitHandle.WaitAny(handles);
            if (index >= signals.Length)
            {
                return; // Stop requested.
            }

            _onSignal(signals[index]);
        }
    }

    private static EventWaitHandle Open(string instanceName, InstanceSignal signal)
    {
        return new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{instanceName}.{signal}");
    }
}
