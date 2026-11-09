using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Past-session tasks of other PCs that this device keeps as local items (ADR 72), shown under "Unfinished" and in the Archive
/// as "from &lt;PC&gt;". Only this device holds them; they never travel, except back to their PC when made synced again. Kept
/// DPAPI-encrypted (document "tasks.keptPast"), never logged, 90 days. Use from the UI thread only.
/// </summary>
public sealed class KeptPastStore
{
    private const string DocumentKey = "tasks.keptPast";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private List<SyncKeptPast> _all = [];
    private Task _saving = Task.CompletedTask;

    public KeptPastStore(SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _time = time;
    }

    public event EventHandler? Changed;

    /// <summary>The kept tasks, newest session first.</summary>
    public IReadOnlyList<SyncKeptPast> Items => _all.OrderByDescending(item => item.SessionStart ?? item.Task.DoneAt).ToList();

    public void LoadBlocking()
    {
        try
        {
            Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult();
            string? json = stored is null ? null : _protector.Unprotect(Convert.FromBase64String(stored.Data));
            _all = json is null ? [] : JsonSerializer.Deserialize<List<SyncKeptPast>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException)
        {
            _all = [];
        }
    }

    /// <summary>The kept task <paramref name="taskId"/>; null when it isn't kept here.</summary>
    public SyncKeptPast? Find(Guid taskId)
    {
        return _all.FirstOrDefault(item => item.Task.Id == taskId);
    }

    /// <summary>Keeps <paramref name="item"/> (a newer copy of one already kept replaces it).</summary>
    public Task KeepAsync(SyncKeptPast item)
    {
        SyncKeptPast? held = Find(item.Task.Id);
        if (held is not null && held.EditedAt > item.EditedAt)
        {
            return Task.CompletedTask;
        }

        _all = _all.Where(other => other.Task.Id != item.Task.Id).Append(item).ToList();
        return SaveAsync();
    }

    /// <summary>Changes the kept task <paramref name="taskId"/> (a tick, its labels); nothing when it isn't kept here.</summary>
    public Task ChangeAsync(Guid taskId, Func<SessionTask, SessionTask> change)
    {
        SyncKeptPast? held = Find(taskId);
        if (held is null)
        {
            return Task.CompletedTask;
        }

        _all = _all.Select(item => item.Task.Id == taskId ? item with { Task = change(item.Task), EditedAt = _time.GetUtcNow() } : item).ToList();
        return SaveAsync();
    }

    /// <summary>Drops the kept task <paramref name="taskId"/> (moved to a list, deleted, or handed back); returns it.</summary>
    public async Task<SyncKeptPast?> RemoveAsync(Guid taskId)
    {
        SyncKeptPast? held = Find(taskId);
        if (held is null)
        {
            return null;
        }

        _all = _all.Where(item => item.Task.Id != taskId).ToList();
        await SaveAsync();
        return held;
    }

    private Task SaveAsync()
    {
        DateTimeOffset from = _time.GetUtcNow() - SyncTaskHistoryEntry.Kept;
        _all = _all.Where(item => (item.SessionStart ?? item.EditedAt) >= from).ToList();
        Changed?.Invoke(this, EventArgs.Empty);
        string sealedList = Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(_all, JsonOptions)));
        _saving = _saving.ContinueWith(_ => _store.SaveAsync(DocumentKey, new Stored(sealedList)), TaskScheduler.Default).Unwrap();
        return _saving;
    }

    private sealed record Stored(string Data);
}
