namespace AccountabiliBuddy.App;

/// <summary>
/// Prevents two copies of the app running at once (e.g. auto-start plus a manual launch),
/// which would double every nudge and fight over the database.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
    }

    /// <returns>A guard to hold for the app's lifetime, or null if another instance is already running.</returns>
    public static SingleInstanceGuard? TryAcquire(string name)
    {
        // "Local\" scopes the mutex to the current Windows session (per user sign-in).
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{name}", out bool createdNew);
        if (createdNew)
        {
            return new SingleInstanceGuard(mutex);
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>Waits up to <paramref name="wait"/> for the running instance to exit (used after a restart).</summary>
    /// <returns>A guard to hold for the app's lifetime, or null if the other instance is still running.</returns>
    public static SingleInstanceGuard? TryAcquire(string name, TimeSpan wait)
    {
        var mutex = new Mutex(initiallyOwned: false, $@"Local\{name}");
        try
        {
            if (mutex.WaitOne(wait))
            {
                return new SingleInstanceGuard(mutex);
            }
        }
        catch (AbandonedMutexException)
        {
            // The old instance exited without releasing it; this one owns it now.
            return new SingleInstanceGuard(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
