using System.Text.Json;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Recently deleted (ADR 50): the tasks, steps, Later items and come-backs deleted in the last 30 days, with their content, so
/// "Undo" and "Restore" can bring them back as they were. Kept in one DPAPI-encrypted document ("session.recentlyDeleted") on this
/// PC only: never synced (the tombstones that sync carry no text) and never logged. Entries older than 30 days go. Use from the UI
/// thread only.
/// </summary>
public sealed class RecentlyDeletedStore
{
    private const string DocumentKey = "session.recentlyDeleted";

    /// <summary>How long a deleted item can be restored.</summary>
    public static readonly TimeSpan Kept = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private List<DeletedItem> _items = [];

    // Saves run one after another, so an older list never overwrites a newer one.
    private Task _saving = Task.CompletedTask;

    public RecentlyDeletedStore(SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _time = time;
    }

    /// <summary>The entries kept, newest first.</summary>
    public IReadOnlyList<DeletedItem> Items
    {
        get
        {
            // An app left running for weeks prunes only on load and save, so entries past 30 days are left out here too.
            DateTimeOffset keptFrom = _time.GetUtcNow() - Kept;
            return _items.Where(item => item.DeletedAt >= keptFrom).OrderByDescending(item => item.DeletedAt).ToList();
        }
    }

    /// <summary>Raised when the list changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised with the entry just recorded: the Undo bar shows it for a few seconds.</summary>
    public event EventHandler<DeletedItem>? Recorded;

    public void LoadBlocking()
    {
        try
        {
            Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult();
            string? json = stored is null ? null : _protector.Unprotect(Convert.FromBase64String(stored.Data));
            _items = json is null ? [] : JsonSerializer.Deserialize<List<DeletedItem>>(json, JsonOptions) ?? [];
            DateTimeOffset from = _time.GetUtcNow() - Kept;
            _items = _items.Where(item => item.DeletedAt >= from).ToList();
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException)
        {
            _items = [];
        }
    }

    public DeletedItem? Find(Guid entryId)
    {
        return _items.FirstOrDefault(item => item.EntryId == entryId);
    }

    /// <summary>Keeps a deleted item for 30 days and raises <see cref="Recorded"/>.</summary>
    public async Task RecordAsync(DeletedItem item)
    {
        _items.Add(item);
        Task saved = SaveAsync();
        Recorded?.Invoke(this, item);
        await saved;
    }

    /// <summary>Takes an entry out: it was restored, or it is deleted for good. Its content goes with it.</summary>
    public Task RemoveAsync(Guid entryId)
    {
        if (_items.RemoveAll(item => item.EntryId == entryId) == 0)
        {
            return Task.CompletedTask;
        }

        return SaveAsync();
    }

    private Task SaveAsync()
    {
        DateTimeOffset from = _time.GetUtcNow() - Kept;
        _items = _items.Where(item => item.DeletedAt >= from).ToList();
        Changed?.Invoke(this, EventArgs.Empty);
        string sealedList = Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(_items, JsonOptions)));
        _saving = _saving.ContinueWith(_ => _store.SaveAsync(DocumentKey, new Stored(sealedList)), TaskScheduler.Default).Unwrap();
        return _saving;
    }

    private sealed record Stored(string Data);
}
