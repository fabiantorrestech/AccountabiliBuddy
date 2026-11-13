using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// A small per-method log of this PC's own puts (times only, the newest <see cref="MaxPerMethod"/> per method), kept in the device-local
/// document "sync.sendlog" (never in a backup, never sent). The confirmation board needs it: whether the receiver's last file could
/// already have confirmed my newest one is decided against the receiver's OWN write time (<see cref="SyncSentFacts.PreviousSentAt"/>,
/// SYNC1a Q850). Use from the UI thread.
/// </summary>
public sealed class SyncSendLog
{
    /// <summary>The document key.</summary>
    public const string DocumentKey = "sync.sendlog";

    /// <summary>Entries kept per method (the oldest go first).</summary>
    public const int MaxPerMethod = 24;

    private readonly SqliteJsonDocumentStore _store;
    private Dictionary<string, List<SyncSendEntry>> _entries = [];
    private Task _saving = Task.CompletedTask;

    public SyncSendLog(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public void LoadBlocking()
    {
        try
        {
            Dictionary<string, List<SyncSendEntry>>? loaded = Task.Run(() => _store.LoadAsync<Dictionary<string, List<SyncSendEntry>>>(DocumentKey)).GetAwaiter().GetResult();
            _entries = loaded?
                .Where(pair => SyncReceiptParts.Methods.Contains(pair.Key) && pair.Value is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value.Where(entry => entry is not null).TakeLast(MaxPerMethod).ToList()) ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _entries = [];
        }
    }

    /// <summary>My file written at <paramref name="writtenAt"/> reached <paramref name="method"/> at <paramref name="reachedAt"/>.</summary>
    public void Record(string method, DateTimeOffset writtenAt, DateTimeOffset reachedAt)
    {
        if (!SyncReceiptParts.Methods.Contains(method))
        {
            return;
        }

        if (!_entries.TryGetValue(method, out List<SyncSendEntry>? list))
        {
            list = _entries[method] = [];
        }

        list.Add(new SyncSendEntry(writtenAt, reachedAt));
        if (list.Count > MaxPerMethod)
        {
            list.RemoveRange(0, list.Count - MaxPerMethod);
        }

        Save();
    }

    /// <summary>Forgets everything (left the group: the files belonged to it).</summary>
    public void Clear()
    {
        _entries = [];
        Save();
    }

    /// <summary>The newest <c>WrittenAt</c> of any put; null when nothing was put yet.</summary>
    public DateTimeOffset? Newest => _entries.Values.SelectMany(list => list).Select(entry => (DateTimeOffset?)entry.WrittenAt).Max();

    /// <summary>When this PC's newest put on <paramref name="method"/> reached it; null when nothing was put there.</summary>
    public DateTimeOffset? LastReached(string method) => _entries.TryGetValue(method, out List<SyncSendEntry>? list) && list.Count > 0 ? list.Max(entry => entry.ReachedAt) : null;

    /// <summary>
    /// What the sender knows for one receiver (<see cref="SyncSentFacts"/>): the newest file, when that file reached each method, and per
    /// method the newest of my files that had reached it by <paramref name="receiverWrote"/>, the receiver's own latest write time.
    /// </summary>
    public SyncSentFacts? Facts(DateTimeOffset? receiverWrote)
    {
        if (Newest is not { } newest)
        {
            return null;
        }

        var reached = new Dictionary<string, DateTimeOffset>();
        var previous = new Dictionary<string, DateTimeOffset>();
        foreach ((string method, List<SyncSendEntry> list) in _entries)
        {
            SyncSendEntry? atNewest = list.Where(entry => entry.WrittenAt == newest).OrderBy(entry => entry.ReachedAt).FirstOrDefault();
            if (atNewest is not null)
            {
                reached[method] = atNewest.ReachedAt;
            }

            if (receiverWrote is { } wrote)
            {
                IEnumerable<SyncSendEntry> before = list.Where(entry => entry.ReachedAt <= wrote);
                if (before.Any())
                {
                    previous[method] = before.Max(entry => entry.WrittenAt);
                }
            }
        }

        return new SyncSentFacts(newest, reached, previous.Count == 0 ? null : previous);
    }

    private void Save()
    {
        Dictionary<string, List<SyncSendEntry>> snapshot = _entries.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
        _saving = _saving.ContinueWith(
            async _ =>
            {
                try
                {
                    await _store.SaveAsync(DocumentKey, snapshot).ConfigureAwait(false);
                }
                catch (SqliteException)
                {
                    // Kept in memory.
                }
            },
            TaskScheduler.Default).Unwrap();
    }
}
