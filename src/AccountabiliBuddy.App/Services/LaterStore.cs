using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The Later list (ADR 50): tasks the user keeps for another day, each with the steps, category, estimate and note it had
/// and an optional due date. Kept as one DPAPI-encrypted list (documents "session.later"), tombstones included, like the
/// next session's tasks and the come-backs. The list is shared with the user's other devices through the sealed sync files
/// (ADR 22, 48): each item has an edit time and a deleted one stays as a tombstone for 30 days, so <see cref="MergeAsync"/>
/// can take another device's list item by item. Titles, steps and notes are private text: only the sealed files carry them
/// between devices, at rest they stay encrypted, and they are never logged. Use from the UI thread only.
/// </summary>
public sealed class LaterStore
{
    private const string DocumentKey = "session.later";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;

    // Every item kept, tombstones included (they only exist for the merge).
    private List<SyncLaterItem> _all = [];

    // Saves run one after another, so an older list never overwrites a newer one.
    private Task _saving = Task.CompletedTask;

    public LaterStore(SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _time = time;
    }

    /// <summary>The items kept (never a tombstone), in list order.</summary>
    public IReadOnlyList<SyncLaterItem> Items => _all.Where(item => item.DeletedAt is null).OrderBy(item => item.Position).ToList();

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult();
            string? json = stored is null ? null : _protector.Unprotect(Convert.FromBase64String(stored.Data));
            _all = json is null ? [] : JsonSerializer.Deserialize<List<SyncLaterItem>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException)
        {
            _all = [];
        }
    }

    /// <summary>Adds an item at the end of the list; a blank title adds nothing. Returns it (null when nothing was added).</summary>
    public async Task<SyncLaterItem?> AddAsync(
        string title, IReadOnlyList<SessionSubtask>? steps, string? category, string? categoryColor, int? estimateMinutes, string? note,
        DateOnly? dueOn, PlannedSlot? plannedFor = null)
    {
        string trimmed = title.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        DateTimeOffset now = _time.GetUtcNow();
        double last = _all.Where(item => item.DeletedAt is null).Select(item => item.Position).DefaultIfEmpty(0).Max();
        var item = new SyncLaterItem(
            Guid.NewGuid(), trimmed, steps is { Count: > 0 } ? steps : null, category, categoryColor,
            SessionChecklist.CleanEstimate(estimateMinutes), SessionChecklist.CleanNote(note), dueOn,
            now, null, SharedListMerge.PositionBetween(last, null), plannedFor);
        _all.Add(item);
        await SaveAsync();
        return item;
    }

    /// <summary>Changes the item <paramref name="id"/> and stamps the change; an unknown or deleted id changes nothing.</summary>
    public Task EditAsync(Guid id, Func<SyncLaterItem, SyncLaterItem> change)
    {
        int index = _all.FindIndex(item => item.Id == id && item.DeletedAt is null);
        if (index < 0)
        {
            return Task.CompletedTask;
        }

        SyncLaterItem held = _all[index];
        SyncLaterItem changed = change(held);
        if (changed.Equals(held) || (changed.Title ?? string.Empty).Trim().Length == 0)
        {
            return Task.CompletedTask;
        }

        _all[index] = changed with { EditedAt = SharedListMerge.NextStamp(_time.GetUtcNow(), held) };
        return SaveAsync();
    }

    /// <summary>
    /// Deletes an item: its text goes at once and a tombstone stays for 30 days, so the user's other devices delete it
    /// too. An unknown id changes nothing.
    /// </summary>
    public Task DeleteAsync(Guid id)
    {
        int index = _all.FindIndex(item => item.Id == id && item.DeletedAt is null);
        if (index < 0)
        {
            return Task.CompletedTask;
        }

        _all[index] = _all[index].Deleted(SharedListMerge.NextStamp(_time.GetUtcNow(), _all[index]));
        return SaveAsync();
    }

    /// <summary>The list for this device's sync file (ADR 50): its items and the tombstones of the last 30 days, in list order.</summary>
    public IReadOnlyList<SyncLaterItem> ForSync()
    {
        DateTimeOffset from = _time.GetUtcNow() - SharedListMerge.TombstonesKept;
        return _all
            .Where(item => item.DeletedAt is not DateTimeOffset deletedAt || deletedAt > from)
            .OrderBy(item => item.Position)
            .ToList();
    }

    /// <summary>
    /// Takes another device's list in (ADR 50): item by item, the newest edit wins and a newer tombstone removes an item
    /// (<see cref="SharedListMerge"/>). Saves and raises <see cref="Changed"/> only when the list changed.
    /// </summary>
    /// <returns>True when this device's list changed.</returns>
    public async Task<bool> MergeAsync(IReadOnlyList<SyncLaterItem> incoming)
    {
        DateTimeOffset now = _time.GetUtcNow();
        List<SyncLaterItem> merged = SharedListMerge.Merge(_all, incoming, now);
        List<SyncLaterItem> before = SharedListMerge.Merge(_all, [], now);
        if (merged.SequenceEqual(before, new ItemComparer()))
        {
            return false;
        }

        _all = merged;
        await SaveAsync();
        return true;
    }

    private Task SaveAsync()
    {
        DateTimeOffset tombstonesFrom = _time.GetUtcNow() - SharedListMerge.TombstonesKept;
        _all = _all.Where(item => item.DeletedAt is not DateTimeOffset deletedAt || deletedAt > tombstonesFrom).ToList();
        Changed?.Invoke(this, EventArgs.Empty);
        string sealedList = Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(_all, JsonOptions)));
        // After the saves asked for before it, whatever became of them, so an older list never overwrites a newer
        // one; a failed save still fails the task it returns.
        _saving = _saving.ContinueWith(_ => _store.SaveAsync(DocumentKey, new Stored(sealedList)), TaskScheduler.Default).Unwrap();
        return _saving;
    }

    // SyncLaterItem holds a list, so it isn't equal by value: compare it by its fields.
    private sealed class ItemComparer : IEqualityComparer<SyncLaterItem>
    {
        public bool Equals(SyncLaterItem? left, SyncLaterItem? right)
        {
            return left is not null && right is not null
                && left.Id == right.Id
                && left.EditedAt == right.EditedAt
                && left.DeletedAt == right.DeletedAt
                && left.Position == right.Position
                && left.TieBreak == right.TieBreak;
        }

        public int GetHashCode(SyncLaterItem item)
        {
            return item.Id.GetHashCode();
        }
    }

    private sealed record Stored(string Data);
}
