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

    /// <summary>Adds one; a blank line adds nothing.</summary>
    public Task AddAsync(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return Task.CompletedTask;
        }

        DateTimeOffset now = _time.GetUtcNow();
        double last = _all.Count == 0 ? 0 : _all.Max(comeback => comeback.Position);
        _all.Add(new Comeback(Guid.NewGuid(), trimmed, now, EditedAt: now, Position: SharedListMerge.PositionBetween(last, null)));
        return SaveAsync();
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
        _all[index] = _all[index] with { DoneAt = now, EditedAt = now };
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

        _all[index] = _all[index] with { Text = trimmed, EditedAt = _time.GetUtcNow() };
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

        DateTimeOffset now = _time.GetUtcNow();
        _all[index] = _all[index] with { Text = string.Empty, DoneAt = null, EditedAt = now, DeletedAt = now };
        return SaveAsync();
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
        return new SyncParked(comeback.Id, comeback.Text, comeback.CreatedAt, comeback.DoneAt, comeback.EditedAt, comeback.DeletedAt, comeback.Position);
    }

    private static Comeback FromShared(SyncParked item)
    {
        return new Comeback(item.Id, item.Text, item.CreatedAt, item.DoneAt, item.EditedAt, item.DeletedAt, item.Position);
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
