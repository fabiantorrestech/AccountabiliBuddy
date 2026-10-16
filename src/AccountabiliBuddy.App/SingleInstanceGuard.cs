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

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
