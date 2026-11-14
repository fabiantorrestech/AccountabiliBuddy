using System.Text.Json;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using AccountabiliBuddy.Core.Tasks;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Come-backs (redesign phase 2, capture at the source): ideas parked with Ctrl+Alt+N or the work-hours gate, listed on
/// Today until ticked. Kept as one DPAPI-encrypted list (documents "capture.comebacks"), like a session's checklist. The
/// list is shared with the user's other devices through the sealed sync files (ADR 30, 48): each item has an edit time and
/// a deleted one stays as a tombstone for 30 days, so <see cref="MergeAsync"/> can take another device's list item by item.
/// Only the sealed files carry it between devices; at rest here it stays encrypted. Use from the UI thread only.
/// Since ADR 76 (<see cref="UseTaskStore"/>) the list is a view of the one task list (To sort, except Auto-saved's, with History's
/// finished ones ticked): every change goes there as an edit; "capture.comebacks" stays as it was, the way back.
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

    // ADR 76: the task list this list is a view of (null before the migration), and the items as last read from it.
    private TaskStore? _tasks;
    private Dictionary<Guid, string> _shown = [];

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

    /// <summary>An add was refused because the task list this is a view of can't be opened (ADR 76's unreadable state).</summary>
    public event EventHandler? AddRefused;

    /// <summary>This device's id in its sync group (ADR 72's keeper rule); null while it isn't in one. Set by the app.</summary>
    public Func<Guid?> SelfId { get; set; } = () => null;

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

    /// <summary>Every come-back kept (tombstones, ticked and local ones included) in its shared form, for the task migration (ADR 76).</summary>
    public IReadOnlyList<SyncParked> StoredItems => _all.Select(ToShared).ToList();

    /// <summary>
    /// From now on the list is a view of <paramref name="tasks"/> (ADR 76): read from it, its changes sent to it, its other changes
    /// (another device, another list) shown as they come.
    /// </summary>
    public void UseTaskStore(TaskStore tasks)
    {
        _tasks = tasks;
        ReadFromTasks();
        tasks.Changed += OnTasksChanged;
    }

    private void OnTasksChanged(object? sender, TaskStoreChangedEventArgs e)
    {
        if (e.Source == this)
        {
            return;
        }

        string before = string.Join("\n", _shown.OrderBy(pair => pair.Key).Select(pair => pair.Value));
        ReadFromTasks();
        if (string.Join("\n", _shown.OrderBy(pair => pair.Key).Select(pair => pair.Value)) != before)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ReadFromTasks()
    {
        _all = TaskFacadeLists.Parked(_tasks!.Lists, _time.GetUtcNow(), _tasks.LabelShown, _tasks.TagShown).Select(FromShared).ToList();
        _shown = _all.ToDictionary(comeback => comeback.Id, comeback => ToShared(comeback).TieBreak);
    }

    // The come-backs this list changed since it last read the task list (tombstones for the ones it deleted), sent there as edits.
    private Task SendToTasks()
    {
        List<SyncParked> changed = _all.Select(ToShared).Where(item => !_shown.TryGetValue(item.Id, out string? shown) || shown != item.TieBreak).ToList();
        Task saved = _tasks!.ApplyLocal(this, OldTaskFields.Empty with { Parked = changed });
        ReadFromTasks();
        Changed?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    /// <summary>
    /// Adds one, in a category (its name and colour) or none; a blank line adds nothing. With <paramref name="id"/> (a task moved here
    /// from another list of the task list, ADR 76) it keeps that id, so the move is a bin change of the same task.
    /// </summary>
    public Task AddAsync(string text, string? category = null, string? categoryColor = null, IReadOnlyList<string>? labels = null, Guid? id = null)
    {
        // ADR 72: "##word" in the typed text is a label.
        (string typedText, IReadOnlyList<string>? typedLabels) = TaskLabels.Parse(text.Trim());
        string trimmed = typedText.Trim();
        labels = TaskLabels.With(labels, typedLabels ?? []);
        if (trimmed.Length == 0)
        {
            return Task.CompletedTask;
        }

        if (_tasks is { IsActive: false })
        {
            // The task list can't be opened (nothing is saved): say so rather than drop the text unseen.
            AddRefused?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        DateTimeOffset now = _time.GetUtcNow();
        double last = _all.Count == 0 ? 0 : _all.Max(comeback => comeback.Position);
        string? name = CleanCategory(category);
        if (id is Guid kept)
        {
            _all.RemoveAll(other => other.Id == kept);
        }

        _all.Add(new Comeback(
            id is Guid given && given != Guid.Empty ? given : Guid.NewGuid(), trimmed, now, EditedAt: now, Position: SharedListMerge.PositionBetween(last, null),
            Category: name, CategoryColor: name is null ? null : categoryColor, Labels: labels));
        return SaveAsync();
    }

    /// <summary>The list for a task-list backup (ADR 71): as <see cref="ForSync"/>, but the local come-backs kept here keep their text (ADR 72).</summary>
    public IReadOnlyList<SyncParked> ForBackup()
    {
        DateTimeOffset from = _time.GetUtcNow() - SharedListMerge.TombstonesKept;
        return _all
            .Where(comeback => comeback.DeletedAt is DateTimeOffset deletedAt ? deletedAt > from || comeback.LocalTo is not null : comeback.DoneAt is not DateTimeOffset doneAt || doneAt > from)
            .Select(ToShared)
            .OrderBy(item => item.Position)
            .ToList();
    }

    /// <summary>The come-back <paramref name="id"/> while it is shown here; null otherwise.</summary>
    public Comeback? Find(Guid id)
    {
        return _all.FirstOrDefault(comeback => comeback.Id == id && comeback.DeletedAt is null);
    }

    /// <summary>Sets the labels of come-back <paramref name="id"/> (ADR 72; null or none clears them).</summary>
    public Task SetLabelsAsync(Guid id, IReadOnlyList<string>? labels)
    {
        int index = IndexOfLive(id);
        if (index < 0)
        {
            return Task.CompletedTask;
        }

        _all[index] = _all[index] with { Labels = TaskLabels.Clean(labels), EditedAt = EditStamp(_time.GetUtcNow(), _all[index]) };
        return SaveAsync();
    }

    /// <summary>Renames (blank <paramref name="to"/>: deletes) label <paramref name="from"/> on every come-back that has it.</summary>
    public Task RenameLabelAsync(string from, string to)
    {
        DateTimeOffset now = _time.GetUtcNow();
        bool changed = false;
        for (int index = 0; index < _all.Count; index++)
        {
            Comeback held = _all[index];
            if (held.DeletedAt is null && TaskLabels.Has(held.Labels, from))
            {
                _all[index] = held with { Labels = TaskLabels.Renamed(held.Labels, from, to), EditedAt = EditStamp(now, held) };
                changed = true;
            }
        }

        return changed ? SaveAsync() : Task.CompletedTask;
    }

    /// <summary>
    /// Makes come-back <paramref name="id"/> local to <paramref name="keeper"/> (ADR 72), or synced again with null; kept on
    /// another device, only its stub stays here (the caller hands the item over first).
    /// </summary>
    /// <returns>The come-back as it now is, for the handover; null when it isn't shown here.</returns>
    public async Task<SyncParked?> SetLocalAsync(Guid id, Guid? keeper, Guid? self)
    {
        int index = IndexOfLive(id);
        if (index < 0)
        {
            return null;
        }

        Comeback changed = _all[index] with { LocalTo = keeper, EditedAt = EditStamp(_time.GetUtcNow(), _all[index]) };
        SyncParked shared = ToShared(changed);
        _all[index] = keeper is Guid other && other != self ? FromShared((SyncParked)shared.AsLocalStub()) : changed;
        await SaveAsync();
        return shared;
    }

    /// <summary>Takes in a come-back handed over to this device to keep (ADR 72): it replaces any stub or older copy.</summary>
    public Task AdoptAsync(SyncParked item)
    {
        Comeback? held = _all.FirstOrDefault(comeback => comeback.Id == item.Id);
        if (held is not null && held.DeletedAt is null && held.EditedAt > item.EditedAt)
        {
            return Task.CompletedTask;
        }

        _all = _all.Where(comeback => comeback.Id != item.Id).Append(FromShared(item with { DeletedAt = null })).ToList();
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
        _all[index] = _all[index] with { Text = string.Empty, DoneAt = null, Category = null, CategoryColor = null, Labels = null, LocalTo = null, EditedAt = stamp, DeletedAt = stamp };
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
                ? deletedAt > from || comeback.LocalTo is not null
                : comeback.DoneAt is not DateTimeOffset doneAt || doneAt > from)
            .Select(comeback => ToShared(comeback))
            .Select(item => item.LocalTo is not null && item.DeletedAt is null ? (SyncParked)item.AsLocalStub() : item)
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
        List<SyncParked> merged = SharedListMerge.Merge(local, incoming, _time.GetUtcNow(), SelfId());
        List<SyncParked> before = SharedListMerge.Merge(local, [], _time.GetUtcNow(), SelfId());
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
            comeback.Category, comeback.CategoryColor, TaskLabels.Clean(comeback.Labels), comeback.LocalTo);
    }

    private static Comeback FromShared(SyncParked item)
    {
        // A malformed file's null text becomes empty rather than throwing.
        return new Comeback(
            item.Id, item.Text ?? string.Empty, item.CreatedAt, item.DoneAt, item.EditedAt, item.DeletedAt, item.Position,
            item.Category, item.CategoryColor, TaskLabels.Clean(item.Labels), item.LocalTo);
    }

    private Task SaveAsync()
    {
        if (_tasks is not null)
        {
            return SendToTasks();
        }

        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset cutoff = now.AddDays(-KeepDoneDays);
        DateTimeOffset tombstonesFrom = now - SharedListMerge.TombstonesKept;
        _all = _all
            .Where(comeback => comeback.DeletedAt is DateTimeOffset deletedAt
                ? deletedAt > tombstonesFrom || comeback.LocalTo is not null
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
