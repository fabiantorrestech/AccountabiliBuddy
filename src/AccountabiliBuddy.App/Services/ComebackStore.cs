using System.Text.Json;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Come-backs (redesign phase 2, capture at the source): ideas parked with Ctrl+Alt+N or the work-hours gate, listed on
/// Today until ticked. Kept as one DPAPI-encrypted list (documents "capture.comebacks"), like a session's checklist. The
/// list is shared with the user's other devices through the sealed sync files (ADR 30, 48): each item has an edit time and
/// a deleted one stays as a tombstone for 30 days, so <see cref="MergeAsync"/> can take another device's list item by item.
/// Only the sealed files carry it between devices; at rest here it stays encrypted. Use from the UI thread only.
/// </summary>
public sealed class ComebackStore
{
    private const string DocumentKey = "capture.comebacks";
    private const int KeepDoneDays = 90;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;

    // Every item kept, tombstones included (they only exist for the merge).
    private List<Comeback> _all = [];

    // Saves run one after another, so an older list never overwrites a newer one.
    private Task _saving = Task.CompletedTask;

    public ComebackStore(SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _time = time;
    }

    /// <summary>Every come-back kept, open and ticked (never a tombstone), in list order, oldest first.</summary>
    public IReadOnlyList<Comeback> All => _all.Where(comeback => comeback.DeletedAt is null).ToList();

    /// <summary>The ones not ticked yet, oldest first.</summary>
    public IReadOnlyList<Comeback> Open => _all.Where(comeback => comeback.DeletedAt is null && comeback.DoneAt is null).ToList();

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult();
            string? json = stored is null ? null : _protector.Unprotect(Convert.FromBase64String(stored.Data));
            _all = json is null ? [] : WithSharedFields(JsonSerializer.Deserialize<List<Comeback>>(json, JsonOptions) ?? []);
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException)
        {
            _all = [];
        }
    }

    /// <summary>Adds one, in a category (its name and colour) or none; a blank line adds nothing.</summary>
    public Task AddAsync(string text, string? category = null, string? categoryColor = null)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return Task.CompletedTask;
        }

        DateTimeOffset now = _time.GetUtcNow();
        double last = _all.Count == 0 ? 0 : _all.Max(comeback => comeback.Position);
        string? name = CleanCategory(category);
        _all.Add(new Comeback(
            Guid.NewGuid(), trimmed, now, EditedAt: now, Position: SharedListMerge.PositionBetween(last, null),
            Category: name, CategoryColor: name is null ? null : categoryColor));
        return SaveAsync();
    }

    /// <summary>Puts one in a category (its name and colour; null name: none), as a normal edit that syncs; an unknown id or no change does nothing.</summary>
    public Task SetCategoryAsync(Guid id, string? category, string? categoryColor)
    {
        int index = IndexOfLive(id);
        string? name = CleanCategory(category);
        string? color = name is null ? null : categoryColor;
        if (index < 0 || (_all[index].Category == name && _all[index].CategoryColor == color))
        {
            return Task.CompletedTask;
        }

        _all[index] = _all[index] with { Category = name, CategoryColor = color, EditedAt = EditStamp(_time.GetUtcNow(), _all[index]) };
        return SaveAsync();
    }

    /// <summary>How many live come-backs are in the category <paramref name="name"/>.</summary>
    public int CountInCategory(string name)
    {
        return _all.Count(comeback => comeback.DeletedAt is null && string.Equals(comeback.Category, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every come-back in the category <paramref name="oldName"/> takes the new name and colour (a renamed category), or none
    /// when <paramref name="newName"/> is null (a deleted one): each is a normal edit that syncs. One already as asked is left alone.
    /// </summary>
    public async Task RelabelAsync(string oldName, string? newName, string? newColor)
    {
        DateTimeOffset now = _time.GetUtcNow();
        bool changed = false;
        for (int index = 0; index < _all.Count; index++)
        {
            Comeback held = _all[index];
            if (held.DeletedAt is not null || !string.Equals(held.Category, oldName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? color = newName is null ? null : newColor;
            if (held.Category == newName && held.CategoryColor == color)
            {
                continue;
            }

            _all[index] = held with { Category = newName, CategoryColor = color, EditedAt = EditStamp(now, held) };
            changed = true;
        }

        if (changed)
        {
            await SaveAsync();
        }
    }

    private static string? CleanCategory(string? category)
    {
        string? trimmed = category?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>Ticks one: it leaves the list, keeping when (ticked ones are dropped after 90 days).</summary>
    public Task TickAsync(Guid id)
    {
        int index = IndexOfLive(id);
        if (index < 0 || _all[index].DoneAt is not null)
        {
            return Task.CompletedTask;
        }

        DateTimeOffset now = _time.GetUtcNow();
        _all[index] = _all[index] with { DoneAt = now, EditedAt = EditStamp(now, _all[index]) };
        return SaveAsync();
    }

    /// <summary>Opens a ticked one again (unticking it in the Archive); an open or unknown one changes nothing.</summary>
    public Task UntickAsync(Guid id)
    {
        int index = IndexOfLive(id);
        if (index < 0 || _all[index].DoneAt is null)
        {
            return Task.CompletedTask;
        }

        _all[index] = _all[index] with { DoneAt = null, EditedAt = EditStamp(_time.GetUtcNow(), _all[index]) };
        return SaveAsync();
    }

    /// <summary>
    /// Puts a deleted come-back back (Undo, Recently deleted): the same id, text, times and place, as a normal newer edit that
    /// beats its tombstone on every device. Nothing changes when it is already on the list.
    /// </summary>
    public Task RestoreAsync(Comeback comeback)
    {
        int index = _all.FindIndex(held => held.Id == comeback.Id);
        if (index >= 0 && _all[index].DeletedAt is null)
        {
            return Task.CompletedTask;
        }

        DateTimeOffset stamp = index >= 0 ? EditStamp(_time.GetUtcNow(), _all[index]) : _time.GetUtcNow();
        Comeback back = comeback with { EditedAt = stamp, DeletedAt = null };
        if (index >= 0)
        {
            _all[index] = back;
        }
        else
        {
            _all.Add(back);
        }

        return SaveAsync();
    }

    /// <summary>Changes one's text (the Notebook's ✎); a blank text or an unknown id changes nothing.</summary>
    public Task EditAsync(Guid id, string text)
    {
        string trimmed = text.Trim();
        int index = IndexOfLive(id);
        if (index < 0 || trimmed.Length == 0 || _all[index].Text == trimmed)
        {
            return Task.CompletedTask;
        }

        _all[index] = _all[index] with { Text = trimmed, EditedAt = EditStamp(_time.GetUtcNow(), _all[index]) };
        return SaveAsync();
    }

    /// <summary>
    /// Deletes one, open or ticked (the Notebook's ✕); an unknown id changes nothing. Its text goes at once and a
    /// tombstone stays for 30 days, so the user's other devices delete it too.
    /// </summary>
    public Task DeleteAsync(Guid id)
    {
        int index = IndexOfLive(id);
        if (index < 0)
        {
            return Task.CompletedTask;
        }

        DateTimeOffset stamp = EditStamp(_time.GetUtcNow(), _all[index]);
        _all[index] = _all[index] with { Text = string.Empty, DoneAt = null, Category = null, CategoryColor = null, EditedAt = stamp, DeletedAt = stamp };
        return SaveAsync();
    }

    // A change is stamped with now, or one tick past the item's own newest time when that is later (a faster clock's edit
    // must not outlast this one), so the newest change always carries the newest stamp.
    private static DateTimeOffset EditStamp(DateTimeOffset now, Comeback held)
    {
        return SharedListMerge.NextStamp(now, ToShared(held));
    }

    /// <summary>
    /// The list for this device's sync file (ADR 48): the open and recently ticked come-backs and the tombstones of the last
    /// 30 days, in list order.
    /// </summary>
    public IReadOnlyList<SyncParked> ForSync()
    {
        DateTimeOffset from = _time.GetUtcNow() - SharedListMerge.TombstonesKept;
        return _all
            .Where(comeback => comeback.DeletedAt is DateTimeOffset deletedAt
                ? deletedAt > from
                : comeback.DoneAt is not DateTimeOffset doneAt || doneAt > from)
            .Select(ToShared)
            .OrderBy(item => item.Position)
            .ToList();
    }

    /// <summary>
    /// Takes another device's list in (ADR 48): item by item, the newest edit wins and a newer tombstone removes an item
    /// (<see cref="SharedListMerge"/>). Saves and raises <see cref="Changed"/> only when the list changed.
    /// </summary>
    /// <returns>True when this device's list changed.</returns>
    public async Task<bool> MergeAsync(IReadOnlyList<SyncParked> incoming)
    {
        List<SyncParked> local = _all.Select(ToShared).ToList();
        List<SyncParked> merged = SharedListMerge.Merge(local, incoming, _time.GetUtcNow());
        List<SyncParked> before = SharedListMerge.Merge(local, [], _time.GetUtcNow());
        if (merged.SequenceEqual(before))
        {
            return false;
        }

        _all = merged.Select(FromShared).ToList();
        await SaveAsync();
        return true;
    }

    private int IndexOfLive(Guid id)
    {
        return _all.FindIndex(comeback => comeback.Id == id && comeback.DeletedAt is null);
    }

    // A store written before ADR 48 has no edit times or positions: they come from when each was parked or ticked.
    private static List<Comeback> WithSharedFields(List<Comeback> stored)
    {
        if (stored.All(comeback => comeback.EditedAt != default))
        {
            return stored;
        }

        return stored
            .OrderBy(comeback => comeback.CreatedAt)
            .Select((comeback, index) => comeback.EditedAt != default
                ? comeback
                : comeback with { EditedAt = comeback.DoneAt ?? comeback.CreatedAt, Position = index + 1 })
            .ToList();
    }

    private static SyncParked ToShared(Comeback comeback)
    {
        return new SyncParked(
            comeback.Id, comeback.Text, comeback.CreatedAt, comeback.DoneAt, comeback.EditedAt, comeback.DeletedAt, comeback.Position,
            comeback.Category, comeback.CategoryColor);
    }

    private static Comeback FromShared(SyncParked item)
    {
        // A malformed file's null text becomes empty rather than throwing.
        return new Comeback(
            item.Id, item.Text ?? string.Empty, item.CreatedAt, item.DoneAt, item.EditedAt, item.DeletedAt, item.Position,
            item.Category, item.CategoryColor);
    }

    private Task SaveAsync()
    {
        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset cutoff = now.AddDays(-KeepDoneDays);
        DateTimeOffset tombstonesFrom = now - SharedListMerge.TombstonesKept;
        _all = _all
            .Where(comeback => comeback.DeletedAt is DateTimeOffset deletedAt
                ? deletedAt > tombstonesFrom
                : comeback.DoneAt is null || comeback.DoneAt > cutoff)
            .ToList();
        Changed?.Invoke(this, EventArgs.Empty);
        string sealedList = Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(_all, JsonOptions)));
        // After the saves asked for before it, whatever became of them, so an older list never overwrites a newer
        // one; a failed save still fails the task it returns.
        _saving = _saving.ContinueWith(_ => _store.SaveAsync(DocumentKey, new Stored(sealedList)), TaskScheduler.Default).Unwrap();
        return _saving;
    }

    private sealed record Stored(string Data);
}
