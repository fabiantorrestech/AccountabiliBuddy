using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The Tasks page's "Auto-saved" list: tomorrow's first task typed on a shutdown card that closed by itself before the user
/// pressed Done. Since ADR 72 a shared list (<see cref="SyncAutoSavedItem"/>), merged item by item with the user's other devices
/// like ADR 48's lists, tombstones included. Kept as one DPAPI-encrypted list (document "tasks.autoSaved"; the device-local
/// "shutdown.autoSaved" of older versions is taken in once); never logged, since titles are private text. An item goes for good
/// 30 days after it was saved, on every device. Use from the UI thread only.
/// </summary>
public sealed class AutoSavedTaskStore
{
    private const string DocumentKey = "tasks.autoSaved";
    private const string OldDocumentKey = "shutdown.autoSaved";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private List<SyncAutoSavedItem> _all = [];

    // Saves run one after another, so an older list never overwrites a newer one.
    private Task _saving = Task.CompletedTask;

    public AutoSavedTaskStore(SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _time = time;
    }

    /// <summary>This device's id in its sync group (ADR 72's keeper rule); null while it isn't in one. Set by the app.</summary>
    public Func<Guid?> SelfId { get; set; } = () => null;

    public event EventHandler? Changed;

    /// <summary>The items younger than 30 days that are shown here (not tombstones, not kept on another device), newest first.</summary>
    public IReadOnlyList<AutoSavedTask> Items
    {
        get
        {
            DateTimeOffset now = _time.GetUtcNow();
            return _all.Where(item => SharedListMerge.IsLive(item) && !item.IsExpired(now))
                .OrderByDescending(item => item.SavedAt)
                .Select(ToTask)
                .ToList();
        }
    }

    /// <summary>The full items behind <see cref="Items"/> (for labels and the local mark).</summary>
    public IReadOnlyList<SyncAutoSavedItem> Live
    {
        get
        {
            DateTimeOffset now = _time.GetUtcNow();
            return _all.Where(item => SharedListMerge.IsLive(item) && !item.IsExpired(now)).OrderByDescending(item => item.SavedAt).ToList();
        }
    }

    /// <summary>The list as the sync file carries it: tombstones included, local items as stubs, expired ones gone.</summary>
    public IReadOnlyList<SyncAutoSavedItem> ForSync()
    {
        return SharedListMerge.ForFile(Kept(_all));
    }

    public void LoadBlocking()
    {
        try
        {
            Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult();
            if (stored is not null)
            {
                string? json = _protector.Unprotect(Convert.FromBase64String(stored.Data));
                _all = json is null ? [] : JsonSerializer.Deserialize<List<SyncAutoSavedItem>>(json, JsonOptions) ?? [];
            }
            else
            {
                _all = LoadOldList();
                if (_all.Count > 0)
                {
                    _ = SaveAsync();
                }
            }
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException)
        {
            _all = [];
        }

        if (_all.Count != Kept(_all).Count)
        {
            // Items older than 30 days, and tombstones past theirs, go for good.
            _ = SaveAsync();
        }
    }

    /// <summary>Keeps <paramref name="title"/> from the shutdown card of <paramref name="wrapUpOf"/>; a blank title adds nothing. "##word" adds a label.</summary>
    /// <returns>The item kept; null for a blank title.</returns>
    public async Task<AutoSavedTask?> AddAsync(string title, DateOnly wrapUpOf)
    {
        (string text, IReadOnlyList<string>? labels) = TaskLabels.Parse(title.Trim());
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        DateTimeOffset now = _time.GetUtcNow();
        double? last = _all.Count == 0 ? null : _all.Max(item => item.Position);
        var item = new SyncAutoSavedItem(Guid.NewGuid(), trimmed, wrapUpOf, now, now, null, SharedListMerge.PositionBetween(last, null), Labels: labels);
        _all.Add(item);
        await SaveAsync();
        return ToTask(item);
    }

    /// <summary>Puts a deleted item back with the time it was saved (the 30 days run from then); nothing changes when it is already shown.</summary>
    public Task RestoreAsync(AutoSavedTask item)
    {
        SyncAutoSavedItem? held = _all.FirstOrDefault(candidate => candidate.Id == item.Id);
        if (held is not null && SharedListMerge.IsLive(held))
        {
            return Task.CompletedTask;
        }

        DateTimeOffset stamp = SharedListMerge.NextStamp(_time.GetUtcNow(), held);
        double position = held?.Position ?? SharedListMerge.PositionBetween(_all.Count == 0 ? null : _all.Max(other => other.Position), null);
        var restored = new SyncAutoSavedItem(item.Id, item.Title, item.WrapUpOf, item.SavedAt, stamp, null, position, Labels: item.Labels);
        Replace(restored);
        return SaveAsync();
    }

    /// <summary>Deletes the item <paramref name="id"/> here and, as a tombstone, on the other devices; an unknown id changes nothing.</summary>
    public Task RemoveAsync(Guid id)
    {
        SyncAutoSavedItem? held = _all.FirstOrDefault(item => item.Id == id);
        if (held is null || held.DeletedAt is not null)
        {
            return Task.CompletedTask;
        }

        Replace(held.Deleted(SharedListMerge.NextStamp(_time.GetUtcNow(), held)));
        return SaveAsync();
    }

    /// <summary>Sets the labels of item <paramref name="id"/> (null or none clears them).</summary>
    public Task SetLabelsAsync(Guid id, IReadOnlyList<string>? labels)
    {
        return ChangeAsync(id, (item, stamp) => item with { Labels = TaskLabels.Clean(labels), EditedAt = stamp });
    }

    /// <summary>Renames (or, with a blank <paramref name="to"/>, deletes) label <paramref name="from"/> on every item that has it.</summary>
    public Task RenameLabelAsync(string from, string to)
    {
        DateTimeOffset now = _time.GetUtcNow();
        bool changed = false;
        _all = _all.Select(item =>
        {
            if (!SharedListMerge.IsLive(item) || !TaskLabels.Has(item.Labels, from))
            {
                return item;
            }

            changed = true;
            return item with { Labels = TaskLabels.Renamed(item.Labels, from, to), EditedAt = SharedListMerge.NextStamp(now, item) };
        }).ToList();
        return changed ? SaveAsync() : Task.CompletedTask;
    }

    /// <summary>The full item <paramref name="id"/> while it is shown here; null otherwise.</summary>
    public SyncAutoSavedItem? Find(Guid id)
    {
        return _all.FirstOrDefault(item => item.Id == id && SharedListMerge.IsLive(item));
    }

    /// <summary>
    /// Makes item <paramref name="id"/> local to <paramref name="keeper"/> (ADR 72), or synced again with null. Keeping it on
    /// another device leaves only its stub here: the caller hands the full item over first.
    /// </summary>
    /// <returns>The item as it was just before (for the handover); null when it isn't shown here.</returns>
    public async Task<SyncAutoSavedItem?> SetLocalAsync(Guid id, Guid? keeper)
    {
        SyncAutoSavedItem? held = Find(id);
        if (held is null)
        {
            return null;
        }

        SyncAutoSavedItem changed = held with { LocalTo = keeper, EditedAt = SharedListMerge.NextStamp(_time.GetUtcNow(), held) };
        Replace(keeper is Guid other && other != SelfId() ? (SyncAutoSavedItem)changed.AsLocalStub() : changed);
        await SaveAsync();
        return changed;
    }

    /// <summary>Takes in an item handed over to this device to keep (ADR 72): it replaces any stub or older copy.</summary>
    public Task AdoptAsync(SyncAutoSavedItem item)
    {
        SyncAutoSavedItem? held = _all.FirstOrDefault(candidate => candidate.Id == item.Id);
        if (held is not null && SharedListMerge.IsLive(held) && held.EditedAt > item.EditedAt)
        {
            return Task.CompletedTask;
        }

        Replace(item with { DeletedAt = null });
        return SaveAsync();
    }

    /// <summary>Merges another device's list into this one (ADR 72); saves only when something changed.</summary>
    public Task MergeAsync(IReadOnlyList<SyncAutoSavedItem> incoming)
    {
        DateTimeOffset now = _time.GetUtcNow();
        List<SyncAutoSavedItem> merged = SharedListMerge.Merge(_all, incoming, now, SelfId());
        List<SyncAutoSavedItem> before = SharedListMerge.Merge(_all, [], now, SelfId());
        if (merged.SequenceEqual(before))
        {
            return Task.CompletedTask;
        }

        _all = merged;
        return SaveAsync();
    }

    private static AutoSavedTask ToTask(SyncAutoSavedItem item)
    {
        return new AutoSavedTask(item.Id, item.Title, item.WrapUpOf, item.SavedAt, item.Labels, item.LocalTo);
    }

    private Task ChangeAsync(Guid id, Func<SyncAutoSavedItem, DateTimeOffset, SyncAutoSavedItem> change)
    {
        SyncAutoSavedItem? held = Find(id);
        if (held is null)
        {
            return Task.CompletedTask;
        }

        Replace(change(held, SharedListMerge.NextStamp(_time.GetUtcNow(), held)));
        return SaveAsync();
    }

    private void Replace(SyncAutoSavedItem item)
    {
        _all = _all.Where(held => held.Id != item.Id).Append(item).ToList();
    }

    // Expired items go for good (on every device alike); tombstones age out as in every shared list.
    private List<SyncAutoSavedItem> Kept(IEnumerable<SyncAutoSavedItem> items)
    {
        DateTimeOffset now = _time.GetUtcNow();
        return SharedListMerge.Merge(items.Where(item => item.DeletedAt is not null || !item.IsExpired(now)), [], now, SelfId());
    }

    // The device-local list of older versions: plain items, stamped once now so they join the shared list.
    private List<SyncAutoSavedItem> LoadOldList()
    {
        Stored? old = Task.Run(() => _store.LoadAsync<Stored>(OldDocumentKey)).GetAwaiter().GetResult();
        if (old is null)
        {
            return [];
        }

        string? json = _protector.Unprotect(Convert.FromBase64String(old.Data));
        List<OldItem> items = json is null ? [] : JsonSerializer.Deserialize<List<OldItem>>(json, JsonOptions) ?? [];
        DateTimeOffset now = _time.GetUtcNow();
        return items.Select((item, index) => new SyncAutoSavedItem(item.Id, item.Title, item.WrapUpOf, item.SavedAt, now, null, index + 1)).ToList();
    }

    private Task SaveAsync()
    {
        _all = Kept(_all);
        Changed?.Invoke(this, EventArgs.Empty);
        string sealedList = Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(_all, JsonOptions)));
        _saving = _saving.ContinueWith(_ => _store.SaveAsync(DocumentKey, new Stored(sealedList)), TaskScheduler.Default).Unwrap();
        return _saving;
    }

    private sealed record Stored(string Data);

    private sealed record OldItem(Guid Id, string Title, DateOnly WrapUpOf, DateTimeOffset SavedAt);
}
