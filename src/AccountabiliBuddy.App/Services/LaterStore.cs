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
/// The Later list (ADR 50): tasks the user keeps for another day, each with the steps, category, estimate and note it had
/// and an optional due date. Kept as one DPAPI-encrypted list (documents "session.later"), tombstones included, like the
/// next session's tasks and the come-backs. The list is shared with the user's other devices through the sealed sync files
/// (ADR 22, 48): each item has an edit time and a deleted one stays as a tombstone for 30 days, so <see cref="MergeAsync"/>
/// can take another device's list item by item. Titles, steps and notes are private text: only the sealed files carry them
/// between devices, at rest they stay encrypted, and they are never logged. Use from the UI thread only.
/// Since ADR 76 (<see cref="UseTaskStore"/>) the list is a view of the one task list (Next and Later, with History's finished ones
/// ticked): every change goes there as an edit and the list is read back from it; "session.later" stays as it was, the way back.
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

    // ADR 76: the task list this list is a view of (null before the migration), and the items as last read from it.
    private TaskStore? _tasks;

    /// <summary>An add was refused: the task list can't be opened, so nothing could be kept (the app says so; nothing is dropped unseen).</summary>
    public event EventHandler? AddRefused;
    private Dictionary<Guid, string> _shown = [];

    public LaterStore(SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _time = time;
    }

    // A ticked item stays on the shared list (it keeps its text) for as long as the PC keeps its records of done tasks.
    private static readonly TimeSpan DoneKept = TimeSpan.FromDays(90);

    /// <summary>The open items (never a tombstone, never a ticked one), in list order.</summary>
    public IReadOnlyList<SyncLaterItem> Items => _all.Where(item => item.DeletedAt is null && item.DoneAt is null).OrderBy(item => item.Position).ToList();

    /// <summary>The ticked-off items still kept (90 days), newest first.</summary>
    public IReadOnlyList<SyncLaterItem> DoneItems => _all.Where(item => item.DeletedAt is null && item.DoneAt is not null).OrderByDescending(item => item.DoneAt).ToList();

    public event EventHandler? Changed;

    /// <summary>Raised after a merge that ticked items off, or opened ticked ones again, on another device (ADR 50).</summary>
    public event EventHandler<LaterDoneChange>? DoneChanged;

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

    /// <summary>Every item kept (tombstones and local items included), for the task migration (ADR 76).</summary>
    public IReadOnlyList<SyncLaterItem> StoredItems => _all.ToList();

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

        HashSet<Guid> doneBefore = DoneItems.Select(item => item.Id).ToHashSet();
        string before = string.Join("\n", _shown.OrderBy(pair => pair.Key).Select(pair => pair.Value));
        ReadFromTasks();
        if (string.Join("\n", _shown.OrderBy(pair => pair.Key).Select(pair => pair.Value)) == before)
        {
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        if (e.FromOtherDevice)
        {
            // Which items another device ticked off or opened again, found by comparing before and after (as a merge did).
            List<SyncLaterItem> newlyDone = DoneItems.Where(item => !doneBefore.Contains(item.Id)).ToList();
            List<Guid> reopened = Items.Where(item => doneBefore.Contains(item.Id)).Select(item => item.Id).ToList();
            if (newlyDone.Count > 0 || reopened.Count > 0)
            {
                DoneChanged?.Invoke(this, new LaterDoneChange(newlyDone, reopened));
            }
        }
    }

    private void ReadFromTasks()
    {
        _all = TaskFacadeLists.Later(_tasks!.Lists, _time.GetUtcNow(), _tasks.LabelShown, _tasks.TagShown);
        _shown = _all.ToDictionary(item => item.Id, item => item.TieBreak);
    }

    // The items this list changed since it last read the task list (tombstones for the ones it deleted), sent there as edits.
    private Task SendToTasks()
    {
        List<SyncLaterItem> changed = _all.Where(item => !_shown.TryGetValue(item.Id, out string? shown) || shown != item.TieBreak).ToList();
        Task saved = _tasks!.ApplyLocal(this, OldTaskFields.Empty with { Later = changed });
        ReadFromTasks();
        Changed?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    /// <summary>
    /// Adds an item at the end of the list; a blank title adds nothing. Returns it (null when nothing was added). With
    /// <paramref name="id"/> (a task moved here from another list of the task list, ADR 76) it keeps that id, so the move is a bin
    /// change of the same task (its number, reminders and History stay).
    /// </summary>
    public async Task<SyncLaterItem?> AddAsync(
        string title, IReadOnlyList<SessionSubtask>? steps, string? category, string? categoryColor, int? estimateMinutes, string? note,
        DateOnly? dueOn, PlannedSlot? plannedFor = null, IReadOnlyList<string>? labels = null, Guid? id = null)
    {
        // ADR 72: "##word" in a typed title is a label.
        (string typed, IReadOnlyList<string>? typedLabels) = TaskLabels.Parse(title.Trim());
        string trimmed = typed.Trim();
        labels = TaskLabels.With(labels, typedLabels ?? []);
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (_tasks is { IsActive: false })
        {
            // The task list can't be opened (nothing is saved): say so rather than drop it unseen, as Come back to does.
            AddRefused?.Invoke(this, EventArgs.Empty);
            return null;
        }

        DateTimeOffset now = _time.GetUtcNow();
        double last = _all.Where(item => item.DeletedAt is null).Select(item => item.Position).DefaultIfEmpty(0).Max();
        if (id is Guid kept)
        {
            _all.RemoveAll(other => other.Id == kept);
        }

        var item = new SyncLaterItem(
            id is Guid given && given != Guid.Empty ? given : Guid.NewGuid(), trimmed, steps is { Count: > 0 } ? steps : null, category, categoryColor,
            SessionChecklist.CleanEstimate(estimateMinutes), SessionChecklist.CleanNote(note), dueOn,
            now, null, SharedListMerge.PositionBetween(last, null), plannedFor, Labels: labels);
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
    /// Every item in the category <paramref name="oldName"/> takes the new name and colour, or none when <paramref name="newName"/> is null
    /// (a renamed or deleted category): each is a normal stamped edit, saved once for all. Items already as asked are left alone.
    /// </summary>
    public Task RelabelAsync(string oldName, string? newName, string? newColor)
    {
        DateTimeOffset now = _time.GetUtcNow();
        bool changed = false;
        for (int index = 0; index < _all.Count; index++)
        {
            SyncLaterItem held = _all[index];
            if (held.DeletedAt is not null || !string.Equals(held.Category, oldName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? color = newName is null ? null : newColor;
            if (held.Category == newName && held.CategoryColor == color)
            {
                continue;
            }

            _all[index] = held with { Category = newName, CategoryColor = color, EditedAt = SharedListMerge.NextStamp(now, held) };
            changed = true;
        }

        return changed ? SaveAsync() : Task.CompletedTask;
    }

    /// <summary>How many open items are in the category <paramref name="name"/>.</summary>
    public int CountInCategory(string name)
    {
        return _all.Count(item => item.DeletedAt is null && item.DoneAt is null && string.Equals(item.Category, name, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>
    /// Ticks an open item off: it keeps its text and gets <paramref name="doneAt"/> as a normal stamped edit, so every device shows
    /// it as done. Returns the ticked item; null when there is no open item with that id.
    /// </summary>
    public async Task<SyncLaterItem?> CompleteAsync(Guid id, DateTimeOffset doneAt)
    {
        int index = _all.FindIndex(item => item.Id == id && item.DeletedAt is null && item.DoneAt is null);
        if (index < 0)
        {
            return null;
        }

        SyncLaterItem held = _all[index];
        SyncLaterItem done = held with { DoneAt = doneAt, EditedAt = SharedListMerge.NextStamp(_time.GetUtcNow(), held) };
        _all[index] = done;
        await SaveAsync();
        return done;
    }

    /// <summary>Opens a ticked item again (Undo, or un-ticking it in Done): <c>DoneAt</c> goes with a newer stamp. False when there is no such ticked item.</summary>
    public async Task<bool> ReopenAsync(Guid id)
    {
        int index = _all.FindIndex(item => item.Id == id && item.DeletedAt is null && item.DoneAt is not null);
        if (index < 0)
        {
            return false;
        }

        SyncLaterItem held = _all[index];
        _all[index] = held with { DoneAt = null, EditedAt = SharedListMerge.NextStamp(_time.GetUtcNow(), held) };
        await SaveAsync();
        return true;
    }

    /// <summary>
    /// Puts a deleted item back (Undo, Recently deleted): the same id, position and fields, as a normal newer edit that beats
    /// its tombstone on every device. Nothing changes when the item is already on the list.
    /// </summary>
    public Task RestoreAsync(SyncLaterItem item)
    {
        int index = _all.FindIndex(held => held.Id == item.Id);
        if (index >= 0 && _all[index].DeletedAt is null)
        {
            return Task.CompletedTask;
        }

        DateTimeOffset stamp = SharedListMerge.NextStamp(_time.GetUtcNow(), index >= 0 ? _all[index] : null);
        SyncLaterItem back = item with { EditedAt = stamp, DeletedAt = null };
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

    /// <summary>The list for this device's sync file (ADR 50): its items and the tombstones of the last 30 days, in list order.</summary>
    public IReadOnlyList<SyncLaterItem> ForSync()
    {
        DateTimeOffset from = _time.GetUtcNow() - SharedListMerge.TombstonesKept;
        return _all
            .Where(item => item.DeletedAt is not DateTimeOffset deletedAt || deletedAt > from || item.LocalTo is not null)
            .OrderBy(item => item.Position)
            .Select(item => item.LocalTo is not null && item.DeletedAt is null ? (SyncLaterItem)item.AsLocalStub() : item)
            .ToList();
    }

    /// <summary>This device's id in its sync group (ADR 72's keeper rule); null while it isn't in one. Set by the app.</summary>
    public Func<Guid?> SelfId { get; set; } = () => null;

    /// <summary>The list for a task-list backup (ADR 71): as <see cref="ForSync"/>, but the local items kept here keep their text (ADR 72).</summary>
    public IReadOnlyList<SyncLaterItem> ForBackup()
    {
        DateTimeOffset from = _time.GetUtcNow() - SharedListMerge.TombstonesKept;
        return _all.Where(item => item.DeletedAt is not DateTimeOffset deletedAt || deletedAt > from || item.LocalTo is not null).OrderBy(item => item.Position).ToList();
    }

    /// <summary>The item <paramref name="id"/> while it is shown here; null otherwise.</summary>
    public SyncLaterItem? Find(Guid id)
    {
        return _all.FirstOrDefault(item => item.Id == id && item.DeletedAt is null);
    }

    /// <summary>Renames (blank <paramref name="to"/>: deletes) label <paramref name="from"/> on every item that has it (ADR 72).</summary>
    public Task RenameLabelAsync(string from, string to)
    {
        DateTimeOffset now = _time.GetUtcNow();
        bool changed = false;
        for (int index = 0; index < _all.Count; index++)
        {
            SyncLaterItem held = _all[index];
            if (held.DeletedAt is null && TaskLabels.Has(held.Labels, from))
            {
                _all[index] = held with { Labels = TaskLabels.Renamed(held.Labels, from, to), EditedAt = SharedListMerge.NextStamp(now, held) };
                changed = true;
            }
        }

        return changed ? SaveAsync() : Task.CompletedTask;
    }

    /// <summary>
    /// Makes item <paramref name="id"/> local to <paramref name="keeper"/> (ADR 72), or synced again with null; kept on another
    /// device, only its stub stays here (the caller hands the item over first).
    /// </summary>
    /// <returns>The item as it now is, for the handover; null when it isn't shown here.</returns>
    public async Task<SyncLaterItem?> SetLocalAsync(Guid id, Guid? keeper)
    {
        int index = _all.FindIndex(item => item.Id == id && item.DeletedAt is null);
        if (index < 0)
        {
            return null;
        }

        SyncLaterItem changed = _all[index] with { LocalTo = keeper, EditedAt = SharedListMerge.NextStamp(_time.GetUtcNow(), _all[index]) };
        _all[index] = keeper is Guid other && other != SelfId() ? (SyncLaterItem)changed.AsLocalStub() : changed;
        await SaveAsync();
        return changed;
    }

    /// <summary>Takes in an item handed over to this device to keep (ADR 72): it replaces any stub or older copy.</summary>
    public Task AdoptAsync(SyncLaterItem item)
    {
        SyncLaterItem? held = _all.FirstOrDefault(candidate => candidate.Id == item.Id);
        if (held is not null && held.DeletedAt is null && held.EditedAt > item.EditedAt)
        {
            return Task.CompletedTask;
        }

        _all = _all.Where(candidate => candidate.Id != item.Id).Append(item with { DeletedAt = null }).ToList();
        return SaveAsync();
    }

    /// <summary>
    /// Takes another device's list in (ADR 50): item by item, the newest edit wins and a newer tombstone removes an item
    /// (<see cref="SharedListMerge"/>). Saves and raises <see cref="Changed"/> only when the list changed.
    /// </summary>
    /// <returns>True when this device's list changed.</returns>
    public async Task<bool> MergeAsync(IReadOnlyList<SyncLaterItem> incoming)
    {
        DateTimeOffset now = _time.GetUtcNow();
        List<SyncLaterItem> merged = SharedListMerge.Merge(_all, incoming, now, SelfId());
        List<SyncLaterItem> before = SharedListMerge.Merge(_all, [], now, SelfId());
        if (merged.SequenceEqual(before, new ItemComparer()))
        {
            return false;
        }

        // Which items this merge ticked off or opened again, found by comparing before and after (never from the state alone).
        HashSet<Guid> doneBefore = before.Where(item => item.DeletedAt is null && item.DoneAt is not null).Select(item => item.Id).ToHashSet();
        List<SyncLaterItem> newlyDone = merged.Where(item => item.DeletedAt is null && item.DoneAt is not null && !doneBefore.Contains(item.Id)).ToList();
        List<Guid> reopened = merged.Where(item => item.DeletedAt is null && item.DoneAt is null && doneBefore.Contains(item.Id)).Select(item => item.Id).ToList();
        _all = merged;
        await SaveAsync();
        if (newlyDone.Count > 0 || reopened.Count > 0)
        {
            DoneChanged?.Invoke(this, new LaterDoneChange(newlyDone, reopened));
        }

        return true;
    }

    private Task SaveAsync()
    {
        if (_tasks is not null)
        {
            return SendToTasks();
        }

        DateTimeOffset tombstonesFrom = _time.GetUtcNow() - SharedListMerge.TombstonesKept;
        // A ticked item older than the records of done tasks goes the normal way: a tombstone with no text, then gone after 30 days.
        DateTimeOffset now = _time.GetUtcNow();
        _all = _all
            .Select(item => item.DeletedAt is null && item.DoneAt is DateTimeOffset doneAt && doneAt < now - DoneKept ? item.Deleted(SharedListMerge.NextStamp(now, item)) : item)
            .Where(item => item.DeletedAt is not DateTimeOffset deletedAt || deletedAt > tombstonesFrom || item.LocalTo is not null)
            .ToList();
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
                && left.DoneAt == right.DoneAt
                && left.TieBreak == right.TieBreak;
        }

        public int GetHashCode(SyncLaterItem item)
        {
            return item.Id.GetHashCode();
        }
    }

    private sealed record Stored(string Data);
}
