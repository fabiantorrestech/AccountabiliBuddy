using System.Collections.ObjectModel;
using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Notebook tab: everything written down so it can leave your head. Come-backs (open and ticked), notes kept in
/// Focus mode, tasks left unfinished when a session ended, and tasks done, newest first, with their times; searchable
/// and filtered by kind. Every task, subtask and come-back has a check box (tick it, or untick it to open it again) and a ⋯ menu
/// (rename, add a subtask, label, move, delete); notes can be edited or deleted. "Recently deleted" lists what was deleted in
/// the last 30 days, each with Restore. The tasks come from <see cref="ArchiveBuilder"/> (AR1-fix), the same rows on every device:
/// this PC's history, the other PCs' histories, past tasks kept here and the one task list's finished tasks, with every device's
/// settle records laid over them. A task of another PC can be ticked, unticked and deleted here but not renamed.
/// </summary>
public sealed partial class NotebookViewModel : ObservableObject
{
    private readonly ComebackStore _comebacks;
    private readonly SessionNotesStore _notes;
    private readonly SessionTasksService _tasks;
    private readonly TimeProvider _time;
    private readonly ReceivedTaskHistoryStore? _received;
    private readonly KeptPastStore? _kept;
    private readonly PastSettledStore? _settled;
    private readonly TaskStore? _taskStore;
    private readonly Func<TaskActionsService?>? _actions;
    private readonly Func<IReadOnlyDictionary<Guid, string>>? _deviceNames;
    private readonly Func<bool>? _notesTravel;

    public NotebookViewModel(
        ComebackStore comebacks, SessionNotesStore notes, SessionTasksService tasks, TimeProvider time,
        ReceivedTaskHistoryStore? received = null, KeptPastStore? kept = null, PastSettledStore? settled = null,
        TaskStore? taskStore = null, Func<TaskActionsService?>? actions = null,
        Func<IReadOnlyDictionary<Guid, string>>? deviceNames = null, Func<bool>? notesTravel = null)
    {
        _deviceNames = deviceNames;
        _notesTravel = notesTravel;
        _comebacks = comebacks;
        _notes = notes;
        _tasks = tasks;
        _time = time;
        _received = received;
        _kept = kept;
        _settled = settled;
        _taskStore = taskStore;
        _actions = actions;
        if (_received is not null)
        {
            _received.Changed += (_, _) => OnStoreChanged();
        }

        if (_kept is not null)
        {
            _kept.Changed += (_, _) => OnStoreChanged();
        }

        if (_settled is not null)
        {
            _settled.Changed += (_, _) => OnStoreChanged();
        }

        if (_taskStore is not null)
        {
            _taskStore.Changed += (_, _) => OnStoreChanged();
        }

        _comebacks.Changed += (_, _) => OnStoreChanged();
        _notes.Changed += (_, _) => OnStoreChanged();
        _tasks.Changed += (_, _) => OnStoreChanged();
        if (_tasks.Deleted is not null)
        {
            _tasks.Deleted.Changed += (_, _) => OnStoreChanged();
        }

        Refresh();
    }

    /// <summary>
    /// Whether the Archive shows now (on the Tasks page: open, or a search running). While it doesn't, a change only marks it
    /// stale and <see cref="ShowIfStale"/> rebuilds it when it opens. Null (shown on its own): it always rebuilds. Set by the page.
    /// </summary>
    internal Func<bool>? IsShown { get; set; }

    private bool _stale;
    private bool _refreshQueued;

    /// <summary>Rebuilds the Archive if something changed while it was folded.</summary>
    internal void ShowIfStale()
    {
        if (_stale && IsShown?.Invoke() != false)
        {
            _stale = false;
            Refresh();
        }
    }

    // The changes of one tick merge into one rebuild after the tick has drawn; none at all while the Archive is folded.
    private void OnStoreChanged()
    {
        if (IsShown?.Invoke() == false)
        {
            _stale = true;
            return;
        }

        if (_refreshQueued)
        {
            return;
        }

        _refreshQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            Refresh();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>The Tasks page the Archive lives on: its ⋯ menus move, label and delete through it. Set by the page.</summary>
    internal TasksViewModel? Host { get; set; }

    /// <summary>The kinds to show: "Everything" or one kind.</summary>
    public static IReadOnlyList<string> Filters { get; } = ["Everything", "Come-backs", "Notes", "Unfinished tasks", "Done tasks"];

    [ObservableProperty]
    public partial string Filter { get; set; } = "Everything";

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    public ObservableCollection<NotebookItemViewModel> Items { get; } = new();

    /// <summary>What was deleted in the last 30 days, newest first.</summary>
    public ObservableCollection<RecentlyDeletedRowViewModel> RecentlyDeleted { get; } = new();

    [ObservableProperty]
    public partial bool IsRecentlyDeletedOpen { get; set; }

    /// <summary>E.g. "Recently deleted · 3".</summary>
    [ObservableProperty]
    public partial string RecentlyDeletedHeader { get; set; } = "Recently deleted";

    public bool HasRecentlyDeleted => RecentlyDeleted.Count > 0;

    /// <summary>A search shows the group open, so a hit can't hide in a folded one.</summary>
    public bool ShowsRecentlyDeleted => IsRecentlyDeletedOpen || Search.Trim().Length > 0;

    partial void OnIsRecentlyDeletedOpenChanged(bool value) => OnPropertyChanged(nameof(ShowsRecentlyDeleted));

    [RelayCommand]
    private void ToggleRecentlyDeleted() => IsRecentlyDeletedOpen = !IsRecentlyDeletedOpen;

    /// <summary>E.g. "3 open come-backs · 12 notes · 4 unfinished · 31 done".</summary>
    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    public bool IsEmpty => Items.Count == 0;

    partial void OnFilterChanged(string value) => Refresh();

    partial void OnSearchChanged(string value) => Refresh();

    /// <summary>Builds the list again from the stores.</summary>
    public void Refresh()
    {
        using IDisposable timing = Services.TickDiag.Measure("archive rebuild");
        ArchiveRows archive = ArchiveBuilder.Build(ArchiveInputNow());
        List<NotebookItemViewModel> all = [.. Comebacks(), .. Notes(archive), .. Tasks(archive)];
        int open = _comebacks.All.Count(comeback => comeback.DoneAt is null);
        SummaryText = $"{open} open come-backs · {archive.Notes.Count} notes · {all.Count(item => item.Kind == UnfinishedKind)} unfinished · {all.Count(item => item.Kind == DoneKind)} done";

        string search = Search.Trim();
        IEnumerable<NotebookItemViewModel> shown = all
            .Where(item => Filter switch
            {
                "Come-backs" => item.Kind == ComebackKind,
                "Notes" => item.Kind == NoteKind,
                "Unfinished tasks" => item.Kind is UnfinishedKind or UnfinishedSubtaskKind,
                "Done tasks" => item.Kind is DoneKind or DoneSubtaskKind,
                _ => true,
            })
            .Where(item => search.Length == 0 || item.Text.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Sort);
        Items.Clear();
        foreach (NotebookItemViewModel item in shown)
        {
            Items.Add(item);
        }

        OnPropertyChanged(nameof(IsEmpty));
        RefreshRecentlyDeleted(search, archive);
    }

    // What the builder reads: this PC's history, the other PCs' histories, the tasks kept here, every device's settle records and the one task list.
    private ArchiveInput ArchiveInputNow()
    {
        return new ArchiveInput(
            _tasks.History.Select(checklist => new SyncTaskHistoryEntry(checklist.OutsideSession ? null : checklist.SessionStart, checklist.Tasks, checklist.OutsideSession)).ToList(),
            (_received?.Devices ?? []).Select(device => new ArchiveReceivedDevice(device.DeviceId, device.Name, device.Entries)).ToList(),
            _kept?.Items ?? [],
            _settled?.ForSync() ?? [],
            _taskStore is { IsActive: true } ? _taskStore.Lists : TaskLists.Empty,
            _time.GetUtcNow(),
            _tasks.IsLive ? _tasks.Current.Tasks.Select(task => task.Id).ToHashSet() : null,
            _notes.Records,
            _notes.Self() ?? Guid.Empty,
            _deviceNames?.Invoke(),
            (_tasks.Deleted?.Items ?? []).Where(item => item.Kind is DeletedKind.HistoryTask or DeletedKind.ForeignPastTask).Select(item => item.Task!).ToList());
    }

    // The device's own Recently deleted (Undo: steps, Later items, come-backs, tasks) plus what every device can see: tasks deleted from
    // the one task list and past tasks deleted anywhere. A task the device's own list already holds shows once, as its own line.
    private void RefreshRecentlyDeleted(string search, ArchiveRows archive)
    {
        RecentlyDeleted.Clear();
        IReadOnlyList<DeletedItem> stored = _tasks.Deleted?.Items ?? [];
        var lines = new List<(DateTimeOffset At, RecentlyDeletedRowViewModel Row)>();
        bool Matches(string title) => search.Length == 0 || title.Contains(search, StringComparison.OrdinalIgnoreCase);

        // The lines every device builds from synced data (AR1b): the same list on each PC. Restore brings a task back through its History
        // entry, or (a past task) through a settle record stamped over the delete; never a raw restore.
        foreach (ArchiveDeletedRow row in archive.Deleted.Where(row => Matches(row.Title)))
        {
            string on = row.DeviceName is null ? string.Empty : $" · on {row.DeviceName}";
            lines.Add((row.DeletedAt, new RecentlyDeletedRowViewModel(row, $"Deleted {When(row.DeletedAt)}{on}", RestoreOf(row, stored))));
        }

        // What only this PC knows (steps, Later items, auto-saved items, a task deleted outside a session) stays listed here, marked as this PC's.
        HashSet<Guid> synced = archive.Deleted.Select(row => row.Id).ToHashSet();
        foreach (DeletedItem item in stored.Where(item => !synced.Contains(item.ItemId) && !RestoredElsewhere(item) && Matches(item.Title)))
        {
            lines.Add((item.DeletedAt, new RecentlyDeletedRowViewModel(item, $"Deleted {When(item.DeletedAt)} · this PC only", _tasks)));
        }

        foreach ((DateTimeOffset _, RecentlyDeletedRowViewModel row) in lines.OrderByDescending(line => line.At))
        {
            RecentlyDeleted.Add(row);
        }

        RecentlyDeletedHeader = $"Recently deleted · {RecentlyDeleted.Count}";
        OnPropertyChanged(nameof(HasRecentlyDeleted));
        OnPropertyChanged(nameof(ShowsRecentlyDeleted));
    }

    // This PC's copy of a past task that another device has since brought back (a newer Done or Open record over the delete) is not a deleted item any more.
    private bool RestoredElsewhere(DeletedItem item)
    {
        return item.Kind is DeletedKind.HistoryTask or DeletedKind.ForeignPastTask
            && _settled?.Of(item.ItemId) is { } record && record.State != SyncPastSettled.Deleted && record.EditedAt >= item.DeletedAt;
    }

    // How Restore works for a synced line: its History entry (the task list), this PC's own copy of a past task, or a settle record stamped over the delete.
    private Func<Task<bool>>? RestoreOf(ArchiveDeletedRow row, IReadOnlyList<DeletedItem> stored)
    {
        if (row.EntryId is Guid entry)
        {
            return _actions?.Invoke() is { } actions ? () => actions.BringBackAsync(entry) : null;
        }

        DeletedItem? mine = stored.FirstOrDefault(item => item.ItemId == row.Id && item.Kind is DeletedKind.HistoryTask or DeletedKind.ForeignPastTask);
        if (mine is not null)
        {
            return async () => (await _tasks.RestoreAsync(mine.EntryId)).Restored;
        }

        return row.CanRestoreByStamp ? () => _tasks.RestorePastByStampAsync(row.Id, row.RestoreAs!) : null;
    }

    private const string ComebackKind = "Come-back";
    private const string NoteKind = "Note";
    private const string UnfinishedKind = "Unfinished task";
    private const string DoneKind = "Done task";
    private const string UnfinishedSubtaskKind = "Unfinished subtask";
    private const string DoneSubtaskKind = "Done subtask";

    private IEnumerable<NotebookItemViewModel> Comebacks()
    {
        return _comebacks.All.Select(comeback => new NotebookItemViewModel(
            ComebackKind,
            comeback.Text,
            comeback.DoneAt is DateTimeOffset done ? $"Parked {When(comeback.CreatedAt)} · ticked {When(done)}" : $"Parked {When(comeback.CreatedAt)} · open",
            comeback.CreatedAt,
            toggle: () => comeback.DoneAt is null ? _comebacks.TickAsync(comeback.Id) : _comebacks.UntickAsync(comeback.Id),
            rename: text => _comebacks.EditAsync(comeback.Id, text),
            delete: () => _tasks.DeleteComebackAsync(comeback.Id),
            isTicked: comeback.DoneAt is not null,
            menu: item => Host?.MenuFor(new TaskRowKey(TaskRowKind.ComeBack, comeback.Id), rename: item.BeginRename) ?? []));
    }

    // Every device's notes (AR1b, ADR 87): one from another device says "from <device>" (or "from another device" when it was removed and
    // not remembered); this PC's own are unmarked. Editing or deleting any of them is a stamped change that reaches every device.
    private IEnumerable<NotebookItemViewModel> Notes(ArchiveRows archive)
    {
        bool travels = _notesTravel?.Invoke() ?? true;
        return archive.Notes.Select(note => new NotebookItemViewModel(
            NoteKind,
            note.Text,
            note.FromText is null ? $"Noted {When(note.At)}" : $"Noted {When(note.At)} · {note.FromText}",
            note.At,
            rename: text => _notes.EditAsync(note.Id, text),
            delete: () => _notes.DeleteAsync(note.Id),
            editWarning: travels ? "Saving changes it on all your devices" : null,
            deleteWarning: travels ? "Deletes it on all your devices" : null));
    }

    // The Archive's tasks (ArchiveBuilder): what was done, and what was left open when its session ended, on this PC and the others.
    // Each task is followed by its steps (set in, just after it).
    private IEnumerable<NotebookItemViewModel> Tasks(ArchiveRows archive)
    {
        foreach (ArchiveTaskRow row in archive.Tasks)
        {
            Guid id = row.Id;
            bool isDone = row.IsDone;
            // Its category as it is now (as it was when the session ended, if it has since been deleted), e.g. "Work · Session of …".
            (string? categoryName, string? categoryColor) = TaskCategoryDisplay.Resolve(_tasks.Categories, row.CategoryId, row.Category, row.CategoryColor);
            string categoryText = categoryName is not null ? $"{categoryName} · " : string.Empty;
            string where = row.Source == ArchiveSource.TaskList ? "From your task list"
                : row.Outside ? "Done outside a session"
                : $"Session of {When(row.SessionStart ?? row.Sort)}";
            string on = row.Source switch
            {
                ArchiveSource.Received => $" · on {row.DeviceName}",
                ArchiveSource.Kept => $" · kept here from {row.DeviceName}",
                _ => string.Empty,
            };
            string state = row.DoneAt is DateTimeOffset done ? $"done {When(done)}" : "left open";
            TaskRowKey key = new(isDone ? TaskRowKind.Done : TaskRowKind.Past, id);
            bool canEdit = row.CanEdit;
            string? hint = canEdit ? null : row.DeviceName is not null ? $"Edit it on {row.DeviceName}" : "Edit it on the Tasks page";
            yield return new NotebookItemViewModel(
                isDone ? DoneKind : UnfinishedKind,
                row.Title,
                $"{categoryText}{where} · {state}{on}",
                row.Sort,
                toggle: row.CanTick ? () => _tasks.SetTickedAsync(id, !isDone) : null,
                rename: canEdit ? title => _tasks.EditHistoryAsync(id, title) : null,
                delete: row.CanDelete ? () => _tasks.EditHistoryAsync(id, null) : null,
                isTicked: isDone,
                addStep: canEdit ? title => _tasks.AddHistoryStepAsync(id, title) : null,
                menu: row.Source == ArchiveSource.TaskList
                    ? null
                    : item => WithoutEdits(Host?.MenuFor(key, rename: item.BeginRename, addStep: item.BeginAddStep) ?? [], canEdit),
                categoryColor: categoryColor,
                editHint: hint);
            int order = 0;
            foreach (ArchiveStepRow step in row.Steps)
            {
                // Just after its task in the newest-first list.
                order++;
                Guid stepId = step.Id;
                bool stepDone = step.DoneAt is not null;
                string stepWhen = step.DoneAt is DateTimeOffset stepTime ? $"Done {When(stepTime)}" : "Left open";
                if (!canEdit)
                {
                    // A step of another PC's task (or of the task list) is ticked and edited where it lives.
                    yield return new NotebookItemViewModel(
                        stepDone ? DoneSubtaskKind : UnfinishedSubtaskKind,
                        step.Title,
                        stepWhen,
                        row.Sort - TimeSpan.FromTicks(order),
                        isSubtask: true,
                        isTicked: stepDone,
                        editHint: hint);
                    continue;
                }

                yield return new NotebookItemViewModel(
                    stepDone ? DoneSubtaskKind : UnfinishedSubtaskKind,
                    step.Title,
                    stepWhen,
                    row.Sort - TimeSpan.FromTicks(order),
                    toggle: () => _tasks.SetTickedAsync(stepId, !stepDone),
                    rename: title => _tasks.EditHistoryAsync(stepId, title),
                    delete: () => _tasks.EditHistoryAsync(stepId, null),
                    isSubtask: true,
                    isTicked: stepDone,
                    addStep: title => _tasks.AddHistoryStepAsync(id, title, stepId),
                    menu: item =>
                    [
                        TaskMenuEntry.Do("Rename", item.BeginRename, MaterialIconKind.PencilOutline),
                        TaskMenuEntry.Do("Add a step after", item.BeginAddStep, MaterialIconKind.PlaylistPlus),
                        TaskMenuEntry.Separator,
                        TaskMenuEntry.Async("Delete", () => _tasks.EditHistoryAsync(stepId, null), MaterialIconKind.DeleteOutline),
                    ]);
            }
        }
    }

    // A task of another PC is edited where it lives: its menu keeps move and delete, but not rename, steps or its label (the label is that PC's).
    private static IReadOnlyList<TaskMenuEntry> WithoutEdits(IReadOnlyList<TaskMenuEntry> entries, bool canEdit)
    {
        return canEdit ? entries : entries.Where(entry => entry.Header is not ("Rename" or "Add subtask" or "Label")).ToList();
    }

    private string When(DateTimeOffset time)
    {
        DateTime local = TimeZoneInfo.ConvertTime(time, _time.LocalTimeZone).DateTime;
        return $"{local.ToString("ddd", CultureInfo.CurrentCulture)} {local:M/d} {local.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
    }
}
