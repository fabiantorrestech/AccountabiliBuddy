namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The ticks waiting out their strike-and-fold on the Tasks page, keyed by the task's row key (not by the row object, which a rebuild
/// may replace): a second tick on the same task takes it back, a rebuilt row shows it still pending, and quitting stores every
/// waiting tick at once (<see cref="FlushAsync"/>) so none is lost. Use from the UI thread only.
/// </summary>
public sealed class TaskFinishQueue
{
    private readonly Dictionary<TaskRowKey, Pending> _pending = [];

    public bool IsPending(TaskRowKey key) => _pending.ContainsKey(key);

    /// <summary>Takes a waiting tick back (nothing is stored). False when none waited.</summary>
    public bool Cancel(TaskRowKey key)
    {
        if (!_pending.Remove(key, out Pending? pending))
        {
            return false;
        }

        pending.Cancel.Cancel();
        return true;
    }

    /// <summary>
    /// Holds the tick for <paramref name="hold"/>, calls <paramref name="folding"/>, waits <paramref name="fold"/>, then stores it
    /// (<paramref name="commit"/>), unless it was taken back or flushed meanwhile.
    /// </summary>
    public async Task RunAsync(TaskRowKey key, Func<Task> commit, Action folding, TimeSpan hold, TimeSpan fold)
    {
        Cancel(key);
        var cancel = new CancellationTokenSource();
        _pending[key] = new Pending(cancel, commit);
        try
        {
            await Task.Delay(hold, cancel.Token);
            folding();
            await Task.Delay(fold, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_pending.TryGetValue(key, out Pending? pending) && ReferenceEquals(pending.Cancel, cancel))
        {
            _pending.Remove(key);
            await commit();
        }
    }

    /// <summary>Stores every waiting tick now (the app is quitting).</summary>
    public async Task FlushAsync()
    {
        List<Pending> waiting = _pending.Values.ToList();
        _pending.Clear();
        foreach (Pending pending in waiting)
        {
            pending.Cancel.Cancel();
            await pending.Commit();
        }
    }

    private sealed record Pending(CancellationTokenSource Cancel, Func<Task> Commit);
}
