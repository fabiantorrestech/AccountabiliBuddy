using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The other PCs' task history as their sealed sync files carried it (ADR 72): per PC, its last file's 90 days of checklists.
/// It feeds "Unfinished from &lt;PC&gt;" and the Archive; it never counts as focus time, sessions or stats. Kept DPAPI-encrypted
/// (document "tasks.receivedHistory"), never logged, pruned at 90 days. Use from the UI thread only.
/// </summary>
public sealed class ReceivedTaskHistoryStore
{
    private const string DocumentKey = "tasks.receivedHistory";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private List<DeviceHistory> _devices = [];
    private Task _saving = Task.CompletedTask;

    public ReceivedTaskHistoryStore(SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _time = time;
    }

    public event EventHandler? Changed;

    /// <summary>Every other PC's history kept here.</summary>
    public IReadOnlyList<DeviceHistory> Devices => _devices;

    public void LoadBlocking()
    {
        try
        {
            Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult();
            string? json = stored is null ? null : _protector.Unprotect(Convert.FromBase64String(stored.Data));
            _devices = json is null ? [] : JsonSerializer.Deserialize<List<DeviceHistory>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException)
        {
            _devices = [];
        }
    }

    /// <summary>Takes in <paramref name="history"/>, the whole history the PC <paramref name="deviceId"/> wrote; it replaces the older copy.</summary>
    public Task ReceiveAsync(Guid deviceId, string name, IReadOnlyList<SyncTaskHistoryEntry> history)
    {
        DateTimeOffset from = _time.GetUtcNow() - SyncTaskHistoryEntry.Kept;
        List<SyncTaskHistoryEntry> kept = history
            .Where(entry => entry?.Tasks is not null && entry.At is DateTimeOffset at && at >= from)
            .Select(entry => entry with { Tasks = entry.Tasks.OfType<SessionTask>().ToList() })
            .ToList();
        DeviceHistory? held = _devices.FirstOrDefault(device => device.DeviceId == deviceId);
        var updated = new DeviceHistory(deviceId, name, kept);
        if (held is not null && held.Name == name && JsonSerializer.Serialize(held.Entries, JsonOptions) == JsonSerializer.Serialize(kept, JsonOptions))
        {
            return Task.CompletedTask;
        }

        _devices = _devices.Where(device => device.DeviceId != deviceId).Append(updated).ToList();
        return SaveAsync();
    }

    /// <summary>Forgets a PC's history (it was removed from the group).</summary>
    public Task ForgetAsync(Guid deviceId)
    {
        return _devices.RemoveAll(device => device.DeviceId == deviceId) == 0 ? Task.CompletedTask : SaveAsync();
    }

    /// <summary>
    /// The open tasks of the other PCs' sessions since <paramref name="since"/>, newest session first, with the PC they came from;
    /// <paramref name="hides"/> leaves out the settled ones. Stubs (tasks kept on one device) never show.
    /// </summary>
    public IReadOnlyList<ReceivedPastTask> Unfinished(DateTimeOffset since, Func<Guid, bool> hides)
    {
        return All()
            .Where(item => !item.Outside && item.SessionStart is DateTimeOffset start && start >= since && !item.Task.IsDone && !hides(item.Task.Id))
            .ToList();
    }

    /// <summary>Every task of the other PCs' history (for the Archive and search), newest first; stubs left out.</summary>
    public IReadOnlyList<ReceivedPastTask> All()
    {
        return _devices
            .SelectMany(device => device.Entries.SelectMany(entry => entry.Tasks
                .Where(task => task.LocalTo is null && task.Title.Length > 0)
                .Select(task => new ReceivedPastTask(device.DeviceId, device.Name, entry.SessionStart, entry.Outside, task))))
            .OrderByDescending(item => item.SessionStart ?? item.Task.DoneAt)
            .ToList();
    }

    /// <summary>The task <paramref name="taskId"/> of another PC's history; null when none holds it.</summary>
    public ReceivedPastTask? Find(Guid taskId)
    {
        return All().FirstOrDefault(item => item.Task.Id == taskId);
    }

    private Task SaveAsync()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        string sealedList = Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(_devices, JsonOptions)));
        _saving = _saving.ContinueWith(_ => _store.SaveAsync(DocumentKey, new Stored(sealedList)), TaskScheduler.Default).Unwrap();
        return _saving;
    }

    private sealed record Stored(string Data);

    /// <summary>One other PC's history, as its last file carried it.</summary>
    public sealed record DeviceHistory(Guid DeviceId, string Name, IReadOnlyList<SyncTaskHistoryEntry> Entries);
}
