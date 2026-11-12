using Avalonia.Threading;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The ticks waiting out their strike-and-fold on the Tasks page, keyed by the task's row key (not by the row object, which a rebuild
/// may replace): a second tick on the same task takes it back, every row of that task (a rebuilt one, the Today page's copy) strikes,
/// folds and comes back from this state (<see cref="Changed"/>), and quitting stores every waiting tick at once
/// (<see cref="FlushAsync"/>) so none is lost. Use from the UI thread only.
/// </summary>
public sealed class TaskFinishQueue
{
    private readonly Dictionary<TaskRowKey, Pending> _pending = [];

    /// <summary>A task's tick started, began to fold, was taken back or stored: its rows should show <see cref="IsPending"/> and <see cref="IsFolding"/>.</summary>
    public event EventHandler<TaskRowKey>? Changed;

    /// <summary>The task's tick is waiting (struck through).</summary>
    public bool IsPending(TaskRowKey key) => _pending.ContainsKey(key);

    /// <summary>The task's row is folding away.</summary>
    public bool IsFolding(TaskRowKey key) => _pending.TryGetValue(key, out Pending? pending) && pending.Folding;

    /// <summary>Takes a waiting tick back (nothing is stored). False when none waited.</summary>
    public bool Cancel(TaskRowKey key)
    {
        if (!_pending.Remove(key, out Pending? pending))
        {
            return false;
        }

        pending.Cancel.Cancel();
        Changed?.Invoke(this, key);
        return true;
    }

    /// <summary>
    /// Holds the tick for <paramref name="hold"/>, folds the row for <paramref name="fold"/>, then stores it (<paramref name="commit"/>),
    /// unless it was taken back or flushed meanwhile.
    /// </summary>
    public async Task RunAsync(TaskRowKey key, Func<Task> commit, TimeSpan hold, TimeSpan fold)
    {
        Cancel(key);
        var pending = new Pending(new CancellationTokenSource(), commit);
        _pending[key] = pending;
        Changed?.Invoke(this, key);
        try
        {
            await Task.Delay(hold, pending.Cancel.Token);
            pending.Folding = true;
            Changed?.Invoke(this, key);
            await Task.Delay(fold, pending.Cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_pending.TryGetValue(key, out Pending? still) && ReferenceEquals(still, pending))
        {
            _pending.Remove(key);
            await StoreAsync(key, commit);
        }
    }

    /// <summary>Stores every waiting tick now (the app is quitting).</summary>
    public async Task FlushAsync()
    {
        List<(TaskRowKey Key, Pending Pending)> waiting = _pending.Select(pair => (pair.Key, pair.Value)).ToList();
        _pending.Clear();
        foreach ((TaskRowKey key, Pending pending) in waiting)
        {
            pending.Cancel.Cancel();
            await StoreAsync(key, pending.Commit);
        }
    }

    // The page's rebuild (queued by the store change) takes the rows away first; a row the rules keep shows again after it.
    private async Task StoreAsync(TaskRowKey key, Func<Task> commit)
    {
        await commit();
        Dispatcher.UIThread.Post(() => Changed?.Invoke(this, key), DispatcherPriority.ContextIdle);
    }

    private sealed class Pending(CancellationTokenSource cancel, Func<Task> commit)
    {
        public CancellationTokenSource Cancel { get; } = cancel;

        public Func<Task> Commit { get; } = commit;

        public bool Folding { get; set; }
    }
}
