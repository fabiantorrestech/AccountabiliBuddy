using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// What was done with the open tasks of past sessions, on any of the user's devices (ADR 72): moved to another list, ticked,
/// opened again, deleted or kept on one device. A shared list (<see cref="SyncPastSettled"/>), merged item by item; it holds
/// only task ids and states, no text (document "tasks.pastSettled"). Every device hides a settled task from "Unfinished"; the
/// PC whose history holds it applies the record to that history (<see cref="Arrived"/>). Use from the UI thread only.
/// </summary>
public sealed class PastSettledStore
{
    private const string DocumentKey = "tasks.pastSettled";

    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;
    private List<SyncPastSettled> _all = [];
    private Task _saving = Task.CompletedTask;

    public PastSettledStore(SqliteJsonDocumentStore store, TimeProvider time)
    {
        _store = store;
        _time = time;
    }

    public event EventHandler? Changed;

    /// <summary>Records that came from another device and won the merge: the owning PC applies them to its history.</summary>
    public event EventHandler<IReadOnlyList<SyncPastSettled>>? Arrived;

    /// <summary>The record for past task <paramref name="id"/>; null when nothing was done with it.</summary>
    public SyncPastSettled? Of(Guid id)
    {
        return _all.FirstOrDefault(record => record.Id == id && record.DeletedAt is null);
    }

    /// <summary>Whether past task <paramref name="id"/> is no longer listed as unfinished.</summary>
    public bool Hides(Guid id)
    {
        return Of(id)?.HidesTask == true;
    }

    /// <summary>The list as the sync file carries it.</summary>
    public IReadOnlyList<SyncPastSettled> ForSync()
    {
        return Kept(_all);
    }

    public void LoadBlocking()
    {
        try
        {
            _all = Task.Run(() => _store.LoadAsync<List<SyncPastSettled>>(DocumentKey)).GetAwaiter().GetResult() ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or JsonException)
        {
            _all = [];
        }
    }

    /// <summary>
    /// Takes in the past tasks older versions kept as "moved" on this PC only (document "session.movedToLater"), once, so they are
    /// settled on every device too.
    /// </summary>
    public Task ImportMovedAsync(IEnumerable<(Guid Id, DateTimeOffset At)> moved)
    {
        bool changed = false;
        foreach ((Guid id, DateTimeOffset at) in moved)
        {
            if (_all.Any(record => record.Id == id))
            {
                continue;
            }

            _all.Add(new SyncPastSettled(id, SyncPastSettled.Moved, at));
            changed = true;
        }

        return changed ? SaveAsync() : Task.CompletedTask;
    }

    /// <summary>Records <paramref name="state"/> for past task <paramref name="id"/> (and its keeper with <see cref="SyncPastSettled.Local"/>).</summary>
    public Task SetAsync(Guid id, string state, Guid? localTo = null)
    {
        SyncPastSettled? held = _all.FirstOrDefault(record => record.Id == id);
        if (held is not null && held.DeletedAt is null && held.State == state && held.LocalTo == localTo)
        {
            return Task.CompletedTask;
        }

        DateTimeOffset stamp = SharedListMerge.NextStamp(_time.GetUtcNow(), held);
        _all = _all.Where(record => record.Id != id).Append(new SyncPastSettled(id, state, stamp, LocalTo: state == SyncPastSettled.Local ? localTo : null)).ToList();
        return SaveAsync();
    }

    /// <summary>Merges another device's records; the ones that changed here are raised through <see cref="Arrived"/>.</summary>
    public Task MergeAsync(IReadOnlyList<SyncPastSettled> incoming)
    {
        DateTimeOffset now = _time.GetUtcNow();
        Dictionary<Guid, SyncPastSettled> before = SharedListMerge.Merge(_all, [], now).ToDictionary(record => record.Id);
        List<SyncPastSettled> merged = SharedListMerge.Merge(_all, incoming, now);
        List<SyncPastSettled> arrived = merged
            .Where(record => !before.TryGetValue(record.Id, out SyncPastSettled? held) || held != record)
            .ToList();
        if (arrived.Count == 0)
        {
            return Task.CompletedTask;
        }

        _all = merged;
        Task saving = SaveAsync();
        Arrived?.Invoke(this, arrived);
        return saving;
    }

    // Records live as long as the history they speak of.
    private List<SyncPastSettled> Kept(IEnumerable<SyncPastSettled> records)
    {
        DateTimeOffset from = _time.GetUtcNow() - SyncPastSettled.Kept;
        return records.Where(record => record.EditedAt >= from).ToList();
    }

    private Task SaveAsync()
    {
        _all = Kept(_all);
        Changed?.Invoke(this, EventArgs.Empty);
        List<SyncPastSettled> copy = _all.ToList();
        _saving = _saving.ContinueWith(_ => _store.SaveAsync(DocumentKey, copy), TaskScheduler.Default).Unwrap();
        return _saving;
    }
}
