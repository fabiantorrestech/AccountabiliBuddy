using System.Text.Json;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The Tasks page's "Auto-saved" list: tomorrow's first task typed on a shutdown card that closed by itself before the
/// user pressed Done. Device-local state, kept as one DPAPI-encrypted list (documents "shutdown.autoSaved"); never synced
/// and never logged, since titles are private text. An item goes for good 30 days after it was saved. Use from the UI thread only.
/// </summary>
public sealed class AutoSavedTaskStore
{
    private const string DocumentKey = "shutdown.autoSaved";
    private static readonly TimeSpan Kept = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private List<AutoSavedTask> _all = [];

    // Saves run one after another, so an older list never overwrites a newer one.
    private Task _saving = Task.CompletedTask;

    public AutoSavedTaskStore(SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _time = time;
    }

    /// <summary>The items younger than 30 days, newest first.</summary>
    public IReadOnlyList<AutoSavedTask> Items
    {
        get
        {
            DateTimeOffset keptFrom = _time.GetUtcNow() - Kept;
            return _all.Where(item => item.SavedAt >= keptFrom).OrderByDescending(item => item.SavedAt).ToList();
        }
    }

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult();
            string? json = stored is null ? null : _protector.Unprotect(Convert.FromBase64String(stored.Data));
            _all = json is null ? [] : JsonSerializer.Deserialize<List<AutoSavedTask>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException)
        {
            _all = [];
        }

        if (_all.Count != Items.Count)
        {
            // Items older than 30 days go for good.
            _ = SaveAsync();
        }
    }

    /// <summary>Keeps <paramref name="title"/> from the shutdown card of <paramref name="wrapUpOf"/>; a blank title adds nothing.</summary>
    public Task AddAsync(string title, DateOnly wrapUpOf)
    {
        string trimmed = title.Trim();
        if (trimmed.Length == 0)
        {
            return Task.CompletedTask;
        }

        _all.Add(new AutoSavedTask(Guid.NewGuid(), trimmed, wrapUpOf, _time.GetUtcNow()));
        return SaveAsync();
    }

    /// <summary>Deletes the item <paramref name="id"/>; an unknown id changes nothing.</summary>
    public Task RemoveAsync(Guid id)
    {
        return _all.RemoveAll(item => item.Id == id) == 0 ? Task.CompletedTask : SaveAsync();
    }

    private Task SaveAsync()
    {
        _all = Items.ToList();
        Changed?.Invoke(this, EventArgs.Empty);
        string sealedList = Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(_all, JsonOptions)));
        _saving = _saving.ContinueWith(_ => _store.SaveAsync(DocumentKey, new Stored(sealedList)), TaskScheduler.Default).Unwrap();
        return _saving;
    }

    private sealed record Stored(string Data);
}
