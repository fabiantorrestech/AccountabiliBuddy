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
/// The Archive sheet (Tasks redesign item 6; SPEC "Archive"; boards 04, 32): the tasks that left the Shelf, in three columns like the
/// Shelf's: Unfinished (tasks left open when their session ended, and tasks archived from the task list), Done (newest first, each with
/// "Do again": a fresh copy) and Recently deleted (30 days, each with Restore). History lives here now: its Do again and Bring back are
/// on these rows. Come-backs and session notes are not listed (come-backs are To sort tasks already; notes become To sort tasks).
/// The pills Everything / Unfinished (8) / Done (38) / Recently deleted pick the columns; the search narrows every column by title.
/// Every task and subtask keeps its check box (untick = back to unfinished) and ⋯ menu (rename, add a subtask, label, move, delete).
/// The tasks come from <see cref="ArchiveBuilder"/> (AR1-fix), the same rows on every device: this PC's history, the other PCs' histories,
/// past tasks kept here and the one task list's finished tasks, with every device's settle records laid over them. A task of another PC
/// can be ticked, unticked and deleted here but not renamed. Task text is shown, never logged. Times are 12-hour.
/// </summary>
public sealed partial class NotebookViewModel : ObservableObject
{
    // While the sheet is closed, only the bottom row's counts are kept fresh, at most this often.
    private static readonly TimeSpan CountsDelay = TimeSpan.FromSeconds(1.5);

    private readonly SessionTasksService _tasks;
    private readonly TimeProvider _time;
    private readonly ReceivedTaskHistoryStore? _received;
    private readonly KeptPastStore? _kept;
    private readonly PastSettledStore? _settled;
    private readonly TaskStore? _taskStore;
    private readonly Func<TaskActionsService?>? _actions;
    private readonly Func<IReadOnlyDictionary<Guid, string>>? _deviceNames;

    public NotebookViewModel(
        ComebackStore comebacks, SessionNotesStore notes, SessionTasksService tasks, TimeProvider time,
        ReceivedTaskHistoryStore? received = null, KeptPastStore? kept = null, PastSettledStore? settled = null,
        TaskStore? taskStore = null, Func<TaskActionsService?>? actions = null,
        Func<IReadOnlyDictionary<Guid, string>>? deviceNames = null, Func<bool>? notesTravel = null)
    {
        // Come-backs and session notes are no longer listed (come-backs are To sort tasks; notes become To sort tasks): comebacks, notes and
        // notesTravel stay in the signature so the app's wiring stays as it is.
        _ = comebacks;
        _ = notes;
        _ = notesTravel;
        _deviceNames = deviceNames;
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

        _tasks.Changed += (_, _) => OnStoreChanged();
        if (_tasks.Deleted is not null)
        {
            _tasks.Deleted.Changed += (_, _) => OnStoreChanged();
        }

        UnfinishedColumn = new ArchiveColumnViewModel("Unfinished", MaterialIconKind.CheckboxBlankOutline, "Nothing left unfinished.");
        DoneColumn = new ArchiveColumnViewModel("Done", MaterialIconKind.CheckCircleOutline, "Nothing done yet.");
        DeletedColumn = new ArchiveColumnViewModel("Recently deleted", MaterialIconKind.DeleteClockOutline, "Nothing deleted in the last 30 days.");
        ApplyPill();
        Refresh();
    }

    /// <summary>
    /// Whether the sheet shows now. While it doesn't, a change only marks it stale (and keeps the bottom row's counts fresh) and
    /// <see cref="ShowIfStale"/> rebuilds it when it opens. Null (shown on its own): it always rebuilds. Set by the page.
    /// </summary>
    internal Func<bool>? IsShown { get; set; }

    private bool _stale;
    private bool _refreshQueued;
    private bool _countsQueued;

    /// <summary>Rebuilds the Archive if something changed while it was closed.</summary>
    internal void ShowIfStale()
    {
        if (_stale && IsShown?.Invoke() != false)
        {
            _stale = false;
            Refresh();
        }
    }

    // The changes of one tick merge into one rebuild after the tick has drawn. While the sheet is closed only the counts are rebuilt,
    // at most every CountsDelay.
    private void OnStoreChanged()
    {
        if (IsShown?.Invoke() == false)
        {
            _stale = true;
            if (!_countsQueued)
            {
                _countsQueued = true;
                Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                {
                    _countsQueued = false;
                    if (_stale)
                    {
                        RefreshCounts();
                    }
                }, CountsDelay, Avalonia.Threading.DispatcherPriority.Background);
            }

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

    /// <summary>The Tasks page the Archive lives on: its ⋯ menus move, label and delete through it, and it opens and closes the sheet. Set by the page.</summary>
    internal TasksViewModel? Host { get; set; }

    public ArchiveColumnViewModel UnfinishedColumn { get; }

    public ArchiveColumnViewModel DoneColumn { get; }

    public ArchiveColumnViewModel DeletedColumn { get; }

    /// <summary>The columns, in order: Unfinished, Done, Recently deleted.</summary>
    public IReadOnlyList<ArchiveColumnViewModel> Columns => [UnfinishedColumn, DoneColumn, DeletedColumn];

    /// <summary>The pill in force (6.09, 6.15, 6.16).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEverythingPicked), nameof(IsUnfinishedPicked), nameof(IsDonePicked), nameof(IsDeletedPicked))]
    public partial ArchivePill Pill { get; set; } = ArchivePill.Everything;

    public bool IsEverythingPicked => Pill == ArchivePill.Everything;

    public bool IsUnfinishedPicked => Pill == ArchivePill.Unfinished;

    public bool IsDonePicked => Pill == ArchivePill.Done;

    public bool IsDeletedPicked => Pill == ArchivePill.Deleted;

    partial void OnPillChanged(ArchivePill value) => ApplyPill();

    /// <summary>A pill's click: "Everything", "Unfinished", "Done" or "Deleted".</summary>
    [RelayCommand]
    private void PickPill(string pill)
    {
        if (Enum.TryParse(pill, out ArchivePill picked))
        {
            Pill = picked;
            ApplyPill();
        }
    }

    // Everything: Unfinished and Done open, Recently deleted folded to its rail. One pill: only that column, open. (A pick also undoes a
    // header's fold, so the pill always shows what it says.)
    private void ApplyPill()
    {
        UnfinishedColumn.IsShown = Pill is ArchivePill.Everything or ArchivePill.Unfinished;
        DoneColumn.IsShown = Pill is ArchivePill.Everything or ArchivePill.Done;
        DeletedColumn.IsShown = Pill is ArchivePill.Everything or ArchivePill.Deleted;
        UnfinishedColumn.IsFolded = false;
        DoneColumn.IsFolded = false;
        DeletedColumn.IsFolded = Pill != ArchivePill.Deleted;
    }

    /// <summary>The fold button, Esc and a click on the dim: closes the sheet.</summary>
    [RelayCommand]
    private void Close()
    {
        if (Host is not null)
        {
            Host.IsArchiveOpen = false;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching), nameof(ResultsText))]
    public partial string Search { get; set; } = string.Empty;

    public bool IsSearching => Search.Trim().Length > 0;

    /// <summary>6.12: "Results for “gopro”" at the right end of the pill row while the search has text.</summary>
    public string ResultsText => $"Results for “{Search.Trim()}”";

    partial void OnSearchChanged(string value) => Refresh();

    /// <summary>The Unfinished pill: "Unfinished (8)" (the counts rule: counts in parentheses).</summary>
    [ObservableProperty]
    public partial string UnfinishedPillText { get; set; } = "Unfinished (0)";

    /// <summary>The Done pill: "Done (38)".</summary>
    [ObservableProperty]
    public partial string DonePillText { get; set; } = "Done (0)";

    /// <summary>The Tasks page's Archive row (4c.61, 6 item 4): "8 unfinished · 38 done" (tasks only: no come-backs or notes).</summary>
    [ObservableProperty]
    public partial string TaskCountsText { get; set; } = string.Empty;

    /// <summary>Builds the sheet again from the stores.</summary>
    public void Refresh()
    {
        using IDisposable timing = Services.TickDiag.Measure("archive rebuild");
        DateTimeOffset now = _time.GetUtcNow();
        ArchiveRows archive = ArchiveBuilder.Build(ArchiveInputNow(now));
        List<TaskHistoryEntry> archived = Archived(archive, now);
        SetCounts(archive, archived);

        string search = Search.Trim();
        bool dotLeft = DotByCheckBox();
        var unfinished = new List<(DateTimeOffset Sort, List<NotebookItemViewModel> Lines)>();
        var done = new List<(DateTimeOffset Sort, List<NotebookItemViewModel> Lines)>();
        foreach (ArchiveTaskRow row in archive.Tasks)
        {
            List<NotebookItemViewModel> lines = TaskLines(row, now, dotLeft);
            (row.IsDone ? done : unfinished).Add((row.Sort, lines));
        }

        foreach (TaskHistoryEntry entry in archived)
        {
            unfinished.Add((entry.When, [ArchivedLine(entry, now, dotLeft)]));
        }

        FillColumn(UnfinishedColumn, unfinished, search);
        FillColumn(DoneColumn, done, search);
        RefreshRecentlyDeleted(search, archive, now);
    }

    // The bottom row's counts only (the sheet is closed).
    private void RefreshCounts()
    {
        DateTimeOffset now = _time.GetUtcNow();
        ArchiveRows archive = ArchiveBuilder.Build(ArchiveInputNow(now));
        SetCounts(archive, Archived(archive, now));
    }

    private void SetCounts(ArchiveRows archive, IReadOnlyList<TaskHistoryEntry> archived)
    {
        int unfinished = archive.Tasks.Count(row => !row.IsDone) + archived.Count;
        int done = archive.Tasks.Count(row => row.IsDone);
        UnfinishedPillText = $"Unfinished ({unfinished.ToString(CultureInfo.CurrentCulture)})";
        DonePillText = $"Done ({done.ToString(CultureInfo.CurrentCulture)})";
        TaskCountsText = $"{unfinished.ToString(CultureInfo.CurrentCulture)} unfinished · {done.ToString(CultureInfo.CurrentCulture)} done";
    }

    // A task shows with all its steps when its title or one of its steps matches; newest first.
    private static void FillColumn(ArchiveColumnViewModel column, List<(DateTimeOffset Sort, List<NotebookItemViewModel> Lines)> groups, string search)
    {
        List<List<NotebookItemViewModel>> shown = groups
            .Where(group => search.Length == 0 || group.Lines.Any(line => line.Text.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(group => group.Sort)
            .Select(group => group.Lines)
            .ToList();
        column.SetRows(shown.SelectMany(lines => lines), shown.Count, search.Length > 0);
    }

    // What the builder reads: this PC's history, the other PCs' histories, the tasks kept here, every device's settle records and the one task list.
    private ArchiveInput ArchiveInputNow(DateTimeOffset now)
    {
        return new ArchiveInput(
            _tasks.History.Select(checklist => new SyncTaskHistoryEntry(checklist.OutsideSession ? null : checklist.SessionStart, checklist.Tasks, checklist.OutsideSession)).ToList(),
            (_received?.Devices ?? []).Select(device => new ArchiveReceivedDevice(device.DeviceId, device.Name, device.Entries)).ToList(),
            _kept?.Items ?? [],
            _settled?.ForSync() ?? [],
            Lists,
            now,
            _tasks.IsLive ? _tasks.Current.Tasks.Select(task => task.Id).ToHashSet() : null,
            // Session notes are not shown in the Archive (they become To sort tasks): the builder gets none.
            null,
            Guid.Empty,
            _deviceNames?.Invoke(),
            (_tasks.Deleted?.Items ?? []).Where(item => item.Kind is DeletedKind.HistoryTask or DeletedKind.ForeignPastTask).Select(item => item.Task!).ToList());
    }

    private TaskLists Lists => _taskStore is { IsActive: true } ? _taskStore.Lists : TaskLists.Empty;

    // Tasks archived from the task list (History's "archived", the last 30 days it listed them): Archive › Unfinished. A task the builder
    // already lists (by id) shows once.
    private List<TaskHistoryEntry> Archived(ArchiveRows archive, DateTimeOffset now)
    {
        HashSet<Guid> listed = archive.Tasks.Select(row => row.Id).ToHashSet();
        return TaskHistoryRules.List(Lists, now).Removed
            .Where(entry => entry.Kind == TaskHistoryKind.Archived && entry.Task.Title.Length > 0 && !listed.Contains(entry.Task.Id))
            .ToList();
    }

    // Settings › Label dot: "By the check box" (and "Pill before", which a column draws the same way) moves the dot left of the box (4c.39).
    private bool DotByCheckBox()
    {
        TaskLabelStyle style = TaskLabelStyles.Migrated(Host?.TaskSettings?.Current.LabelStyle ?? TaskLabelStyle.UnderTask);
        return style is TaskLabelStyle.ByCheckBox or TaskLabelStyle.PillBefore;
    }

    // The device's own Recently deleted (Undo: steps, Later items, come-backs, tasks) plus what every device can see: tasks deleted from
    // the one task list and past tasks deleted anywhere. A task the device's own list already holds shows once, as its own line.
    private void RefreshRecentlyDeleted(string search, ArchiveRows archive, DateTimeOffset now)
    {
        IReadOnlyList<DeletedItem> stored = _tasks.Deleted?.Items ?? [];
        var lines = new List<(DateTimeOffset At, RecentlyDeletedRowViewModel Row)>();
        bool Matches(string title) => search.Length == 0 || title.Contains(search, StringComparison.OrdinalIgnoreCase);

        // The lines every device builds from synced data (AR1b): the same list on each PC. Restore brings a task back through its History
        // entry (as History's Bring back did), or (a past task) through a settle record stamped over the delete; never a raw restore.
        foreach (ArchiveDeletedRow row in archive.Deleted.Where(row => Matches(row.Title)))
        {
            string on = row.DeviceName is null ? string.Empty : $" · on {row.DeviceName}";
            lines.Add((row.DeletedAt, new RecentlyDeletedRowViewModel(row, $"Deleted {Day(row.DeletedAt, now)}{on}", RestoreOf(row, stored))));
        }

        // What only this PC knows (steps, Later items, auto-saved items, a task deleted outside a session) stays listed here, marked as this PC's.
        HashSet<Guid> synced = archive.Deleted.Select(row => row.Id).ToHashSet();
        foreach (DeletedItem item in stored.Where(item => !synced.Contains(item.ItemId) && !RestoredElsewhere(item) && !_tasks.ExistsAgain(item) && Matches(item.Title)))
        {
            lines.Add((item.DeletedAt, new RecentlyDeletedRowViewModel(item, $"{KindPrefix(item.Kind)}Deleted {Day(item.DeletedAt, now)} · this PC only", _tasks)));
        }

        List<RecentlyDeletedRowViewModel> rows = lines.OrderByDescending(line => line.At).Select(line => line.Row).ToList();
        DeletedColumn.SetRows(rows, rows.Count, search.Length > 0);
    }

    // "Step · " before a deleted step's line; nothing before a task's.
    private static string KindPrefix(DeletedKind kind)
    {
        return kind switch
        {
            DeletedKind.Step or DeletedKind.HistoryStep or DeletedKind.LaterStep => "Step · ",
            DeletedKind.Later => "Later · ",
            DeletedKind.ComeBack => "Come back to · ",
            DeletedKind.AutoSaved => "Auto-saved · ",
            _ => string.Empty,
        };
    }

    // (AR1b review: a line of this PC only is hidden once its item exists again, brought back on another device.)
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

    private const string UnfinishedKind = "Unfinished task";
    private const string DoneKind = "Done task";
    private const string UnfinishedSubtaskKind = "Unfinished subtask";
    private const string DoneSubtaskKind = "Done subtask";

    // One Archive task (ArchiveBuilder): what was done, or left open when its session ended, on this PC or another; then its steps.
    private List<NotebookItemViewModel> TaskLines(ArchiveTaskRow row, DateTimeOffset now, bool dotLeft)
    {
        Guid id = row.Id;
        bool isDone = row.IsDone;
        // Its label as it is now (as it was when the session ended, if it has since been deleted).
        (string? labelName, string? labelColor) = _tasks.ShownCategory(row.CategoryId, row.Category, row.CategoryColor);
        string on = row.Source switch
        {
            ArchiveSource.Received => $" · on {row.DeviceName}",
            ArchiveSource.Kept => $" · kept here from {row.DeviceName}",
            _ => string.Empty,
        };
        string state = row.DoneAt is DateTimeOffset doneAt ? $"Finished {Day(doneAt, now)}"
            : row.Outside ? "Left open"
            : $"Left open in a session {Day(row.SessionStart ?? row.Sort, now)}";
        TaskRowKey key = new(isDone ? TaskRowKind.Done : TaskRowKind.Past, id);
        bool canEdit = row.CanEdit;
        string? hint = canEdit ? null : row.DeviceName is not null ? $"Edit it on {row.DeviceName}" : null;
        Func<Task>? doAgain = isDone ? DoAgainOf(row) : null;
        var lines = new List<NotebookItemViewModel>
        {
            new(
                isDone ? DoneKind : UnfinishedKind,
                row.Title,
                state + on,
                row.Sort,
                toggle: ToggleOf(row),
                rename: canEdit ? title => _tasks.EditHistoryAsync(id, title) : null,
                delete: row.CanDelete ? () => _tasks.EditHistoryAsync(id, null) : null,
                isTicked: isDone,
                addStep: canEdit ? title => _tasks.AddHistoryStepAsync(id, title) : null,
                menu: row.Source == ArchiveSource.TaskList
                    ? null
                    : item => WithoutEdits(Host?.MenuFor(key, rename: item.BeginRename, addStep: item.BeginAddStep) ?? [], canEdit),
                categoryColor: labelColor,
                editHint: hint)
            {
                LabelName = labelName ?? string.Empty,
                DotByCheckBox = dotLeft,
                DoAgainCommand = doAgain is null ? null : new AsyncRelayCommand(doAgain),
            },
        };

        int order = 0;
        foreach (ArchiveStepRow step in row.Steps)
        {
            // Just after its task in the newest-first list.
            order++;
            Guid stepId = step.Id;
            bool stepDone = step.DoneAt is not null;
            string stepWhen = step.DoneAt is DateTimeOffset stepTime ? $"Done {Day(stepTime, now)}" : "Left open";
            if (!canEdit)
            {
                // A step of another PC's task (or of the task list) is ticked and edited where it lives.
                lines.Add(new NotebookItemViewModel(
                    stepDone ? DoneSubtaskKind : UnfinishedSubtaskKind, step.Title, stepWhen, row.Sort - TimeSpan.FromTicks(order),
                    isSubtask: true, isTicked: stepDone, editHint: hint));
                continue;
            }

            lines.Add(new NotebookItemViewModel(
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
                ]));
        }

        return lines;
    }

    // The check box. A past task (this PC's, another PC's or kept here) ticks as it always did. A task of the task list: one still in
    // Finished today unticks back to its bin; one already settled into History has no way back here (Do again makes a fresh copy).
    private Func<Task>? ToggleOf(ArchiveTaskRow row)
    {
        if (row.CanTick)
        {
            return () => _tasks.SetTickedAsync(row.Id, !row.IsDone);
        }

        if (row.Source == ArchiveSource.TaskList && row.IsDone && _actions?.Invoke() is { } actions
            && Lists.Tasks.Any(item => item.Id == row.Id && item.Task.IsLive && item.Task.Bin == TaskBin.Done))
        {
            return () => actions.ReopenAsync([row.Id]);
        }

        return null;
    }

    // "Do again" (6.23): a fresh copy in Queued with a new id and number and its origin noted, as History's Do again did. A task settled
    // into History goes through History's own Do again; any other done task (Finished today, or a past session's) is copied by the same
    // rule's parts: its label, tags, time limit, note and steps (open again).
    private Func<Task>? DoAgainOf(ArchiveTaskRow row)
    {
        if (_actions?.Invoke() is not { } actions || _taskStore is not { IsActive: true })
        {
            return null;
        }

        TaskLists lists = Lists;
        if (lists.Log.Any(item => item.Id == row.Id && item.Entry.IsLive && item.Entry.Kind == TaskHistoryKind.Finished))
        {
            return () => actions.DoAgainAsync(row.Id);
        }

        TaskItem? live = lists.Tasks.FirstOrDefault(item => item.Id == row.Id && item.Task.IsLive)?.Task;
        Guid? labelId = live?.LabelId ?? (row.CategoryId is Guid category && lists.Labels.Any(item => item.Label.IsLive && item.Label.Id == category) ? category : null);
        IReadOnlyList<string> steps = row.Steps.Select(step => step.Title).Where(title => title.Trim().Length > 0).ToList();
        return () => CopyAsync(actions, row.Title, labelId, live?.Tags, live?.Note, live?.EstimateMinutes, TaskOrigins.DoneAgain(live?.Number), steps);
    }

    private static async Task CopyAsync(
        TaskActionsService actions, string title, Guid? labelId, IReadOnlyList<string>? tags, string? note, int? estimate, string origin, IReadOnlyList<string> steps)
    {
        Guid id = Guid.NewGuid();
        if (!await actions.AddAsync(id, title, TaskBin.Queued, labelId, tags, note, origin, estimate))
        {
            return;
        }

        foreach (string step in steps)
        {
            await actions.AddStepAsync(id, step);
        }
    }

    // A task archived from the task list: Archive › Unfinished. Its box brings it back and ticks it; its ⋯ menu brings it back (History's
    // Bring back: the same task, to the bin it left).
    private NotebookItemViewModel ArchivedLine(TaskHistoryEntry entry, DateTimeOffset now, bool dotLeft)
    {
        Guid id = entry.Task.Id;
        TaskLabel? label = entry.Task.LabelId is Guid labelId && _taskStore is { } store
            ? store.Lists.Labels.Select(item => item.Label).FirstOrDefault(item => item.IsLive && item.Id == labelId && store.LabelShown(item))
            : null;
        TaskActionsService? actions = _actions?.Invoke();
        return new NotebookItemViewModel(
            UnfinishedKind,
            entry.Task.Title,
            $"Archived {Day(entry.When, now)}",
            entry.When,
            toggle: actions is null ? null : async () =>
            {
                if (await actions.BringBackAsync(id))
                {
                    await actions.FinishAsync([id]);
                }
            },
            menu: actions is null ? null : _ => [TaskMenuEntry.Async("Bring back", () => actions.BringBackAsync(id), MaterialIconKind.ArrowULeftTop)],
            categoryColor: label is null ? null : TaskLabelColors.Opaque(label.Color))
        {
            LabelName = label?.Name ?? string.Empty,
            DotByCheckBox = dotLeft,
        };
    }

    // A task of another PC is edited where it lives: its menu keeps move and delete, but not rename, steps or its label (the label is that PC's).
    private static IReadOnlyList<TaskMenuEntry> WithoutEdits(IReadOnlyList<TaskMenuEntry> entries, bool canEdit)
    {
        return canEdit ? entries : entries.Where(entry => entry.Header is not ("Rename" or "Add subtask" or "Label")).ToList();
    }

    // 6.24 / 6.26: "today, 4:05 PM", "yesterday", "Tue 11/17" (12-hour, local time).
    private string Day(DateTimeOffset time, DateTimeOffset now)
    {
        DateTime local = TimeZoneInfo.ConvertTime(time, _time.LocalTimeZone).DateTime;
        DateTime today = TimeZoneInfo.ConvertTime(now, _time.LocalTimeZone).DateTime.Date;
        if (local.Date == today)
        {
            return "today, " + local.ToString("h:mm tt", CultureInfo.InvariantCulture);
        }

        return local.Date == today.AddDays(-1) ? "yesterday" : local.ToString("ddd M/d", CultureInfo.InvariantCulture);
    }
}
