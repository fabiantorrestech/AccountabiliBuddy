using System.Globalization;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using AccountabiliBuddy.Core.Tasks;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

// Tasks redesign 3.3: what a row shows beyond its title (detail line, ID, highlight, time, quick actions), Windows multi-select,
// dragging rows within and between sections (Alt+↑/↓ too), and what the open task reads (chips, reminders line, sync line, steps).
// Every change goes through the task actions (undo) or the existing store paths; into This session only through the session's cap.
public sealed partial class TasksViewModel
{
    // The task list's live tasks as read at the start of the current rebuild (rows look their task up here), and each task's next
    // reminder occurrence that is still to ring or up.
    private Dictionary<Guid, TaskItem>? _rebuildStored;
    private Dictionary<Guid, TaskReminderEntry> _nextReminders = [];

    // The row the last plain or Ctrl+click was on, and its panel: Shift+click selects from there.
    private TaskSectionsPanelViewModel? _anchorPanel;
    private TaskRowKey? _anchorKey;

    /// <summary>A task counts as untouched after this many weeks (Settings › Tasks); set by the app, else the default.</summary>
    public Func<int>? UntouchedWeeks { get; set; }

    /// <summary>The title bar's sync light (device names and state), for the open task's sync line; set by the app.</summary>
    public Func<SyncLightViewModel?>? SyncLight { get; set; }

    // What a row shows besides its title, line and label (see MakeRow).
    private readonly record struct RowExtras(
        string NumberText, IReadOnlyList<TaskRowBit> Bits, bool IsHighlighted, string TimeText, TaskRowAction? Left, TaskRowAction? Right, bool CanDrag);

    private RowExtras ExtrasOf(TaskRowKey key, TaskItem? item, bool isQueued, bool isTicked, bool selectable)
    {
        bool live = _tasks.IsLive;
        bool isSessionRow = key.Kind == TaskRowKind.Session && !isQueued;
        string number = item?.Number is TaskNumber taskNumber ? taskNumber.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
        IReadOnlyList<TaskRowBit> bits = item is null ? [] : BitsOf(item, withTime: !isSessionRow);
        string time = isSessionRow && item?.EstimateMinutes is int minutes ? EstimateText.Of(minutes) : string.Empty;
        bool canDrag = selectable && !isTicked && key.Kind switch
        {
            TaskRowKind.Session => true,
            TaskRowKind.Queued or TaskRowKind.Later or TaskRowKind.ComeBack => item is not null,
            _ => false,
        };
        return new RowExtras(number, bits, item?.Highlight == true && !isTicked, time, LeftActionOf(key, item, isQueued, isTicked), RightActionOf(key, item, isQueued, isTicked, live), canDrag);
    }

    // ← on a Shelf row: to Queued ("Queue it for today"); the target on a Queued row: into This session (the session's cap decides).
    private TaskRowAction? LeftActionOf(TaskRowKey key, TaskItem? item, bool isQueued, bool isTicked)
    {
        if (isTicked || item is null || _actions is null)
        {
            return null;
        }

        Guid id = key.Id;
        if (key.Kind == TaskRowKind.Queued || isQueued)
        {
            return new TaskRowAction(MaterialIconKind.Target, "Put it in this session", () => MoveQueuedToSessionAsync(id));
        }

        return TaskBins.IsShelf(item.Bin) && key.Kind is TaskRowKind.Later or TaskRowKind.ComeBack
            ? new TaskRowAction(MaterialIconKind.ArrowLeft, "Queue it for today", () => ActAsync(actions => actions.MoveAsync([id], TaskBin.Queued)))
            : null;
    }

    // → on a Today row: to the Shelf's Next. Not for a running session's own tasks (they leave it only as the session allows).
    private TaskRowAction? RightActionOf(TaskRowKey key, TaskItem? item, bool isQueued, bool isTicked, bool live)
    {
        if (isTicked || item is null || _actions is null || item.Bin is not (TaskBin.Session or TaskBin.Queued))
        {
            return null;
        }

        bool ownSessionTask = key.Kind == TaskRowKind.Session && !isQueued;
        if (ownSessionTask && live)
        {
            return null;
        }

        Guid id = key.Id;
        return new TaskRowAction(MaterialIconKind.ArrowRight, "Move to the Shelf (Next)", () => ActAsync(actions => actions.MoveAsync([id], TaskBin.Next)));
    }

    // The detail line, in the prototype's order: time limit, steps, due, reminders, #tags, finished or origin, untouched, sync problem.
    private List<TaskRowBit> BitsOf(TaskItem item, bool withTime)
    {
        var bits = new List<TaskRowBit>();
        DateTimeOffset now = _time.GetUtcNow();
        DateOnly today = DateOnly.FromDateTime(LocalNow());
        if (withTime && item.EstimateMinutes is int minutes)
        {
            bits.Add(new TaskRowBit(EstimateText.Of(minutes), MaterialIconKind.TimerOutline, "Time limit"));
        }

        if (item.Steps is { Count: > 0 } steps)
        {
            bits.Add(new TaskRowBit($"{steps.Count(step => step.IsDone)}/{steps.Count} steps", MaterialIconKind.FormatListChecks));
        }

        if (item.DueOn is DateOnly due && !item.IsDone)
        {
            string text = DueText(due, item.DueAt, today);
            TaskRowBitTone tone = due < today ? TaskRowBitTone.Drift : due == today ? TaskRowBitTone.Ink : TaskRowBitTone.Muted;
            bits.Add(new TaskRowBit(due < today ? "Overdue · " + text : text, MaterialIconKind.CalendarBlankOutline, "Done by", Tone: tone));
        }

        if (item.Reminders is { Count: > 0 } reminders && !item.IsDone)
        {
            string text = reminders.Count > 1
                ? reminders.Count.ToString(CultureInfo.InvariantCulture)
                : _nextReminders.TryGetValue(item.Id, out TaskReminderEntry? next) ? TaskReminderText.Rule(next, _time.LocalTimeZone) : "1";
            bits.Add(new TaskRowBit(text, MaterialIconKind.BellOutline, reminders.Count == 1 ? "1 reminder" : $"{reminders.Count} reminders"));
        }

        foreach (string tag in item.Tags ?? [])
        {
            bits.Add(new TaskRowBit("#" + tag, IsChip: true, Tone: TaskRowBitTone.Ink));
        }

        if (item.IsDone && item.DoneAt is DateTimeOffset doneAt)
        {
            bits.Add(new TaskRowBit("Finished " + Clock(Local(doneAt)), MaterialIconKind.Check));
        }
        else if (!string.IsNullOrWhiteSpace(item.Origin))
        {
            bits.Add(new TaskRowBit(item.Origin));
        }

        int weeks = TaskUntouched.ClampWeeks(UntouchedWeeks?.Invoke() ?? TaskUntouched.DefaultWeeks);
        if (TaskUntouched.IsUntouched(item, weeks, now))
        {
            bits.Add(new TaskRowBit($"untouched {TaskUntouched.Weeks(item, now)} wk", Tone: TaskRowBitTone.Faint));
        }

        if (item.LocalTo is Guid keeper && keeper == SelfId?.Invoke())
        {
            bits.Add(new TaskRowBit("only on this PC", MaterialIconKind.CloudOffOutline, "Kept on this PC only", Tone: TaskRowBitTone.Faint));
        }

        if (item.Conflicts is { Count: > 0 })
        {
            bits.Add(new TaskRowBit(
                string.Empty, MaterialIconKind.CloudAlertOutline, "Changed on both devices · click to pick a version", Tone: TaskRowBitTone.Drift, OpensConflict: true));
        }

        return bits;
    }

    // "Today", "Tomorrow" or "Fri 11/13", with the time when it has one ("Fri 11/13 5:00 PM"); always 12-hour.
    private static string DueText(DateOnly due, TimeOnly? at, DateOnly today)
    {
        string day = (due.DayNumber - today.DayNumber) switch
        {
            0 => "Today",
            1 => "Tomorrow",
            _ => due.ToString("ddd M/d", CultureInfo.InvariantCulture),
        };
        return at is TimeOnly time ? $"{day} {time.ToString("h:mm tt", CultureInfo.InvariantCulture)}" : day;
    }

    // Each task's soonest occurrence that still rings (not dismissed or started).
    private Dictionary<Guid, TaskReminderEntry> NextReminders()
    {
        var next = new Dictionary<Guid, TaskReminderEntry>();
        if (_reminders is null)
        {
            return next;
        }

        foreach (TaskReminderEntry entry in _reminders.Entries.Where(entry => !entry.Handled).OrderBy(entry => entry.RingAt))
        {
            next.TryAdd(entry.TaskId, entry);
        }

        return next;
    }

    // A tick started, began to fold, was taken back or was stored: every row of that task follows.
    private void OnFinishChanged(object? sender, TaskRowKey key)
    {
        foreach (TaskListRowViewModel row in AllRows().Where(row => row.Key == key))
        {
            row.IsFinishing = _finishes.IsPending(key);
            row.IsCollapsing = _finishes.IsFolding(key);
        }
    }

    /// <summary>The app is quitting: every tick still waiting out its strike-and-fold is stored now.</summary>
    public Task FlushPendingFinishesAsync() => _finishes.FlushAsync();

    // ---- The open task ----

    partial void OnIsDetailOpenChanged(bool value) => MarkOpenRow();

    // The open arrow points up on the row whose task is open (only while the card shows).
    private void MarkOpenRow()
    {
        TaskRowKey? open = IsDetailOpen ? Detail?.Key : null;
        foreach (TaskListRowViewModel row in AllRows())
        {
            row.IsSelected = open == row.Key;
        }
    }

    // What the open task reads besides the row's own store: the task list's copy and the steps in focus.
    private TaskSnapshot? Enrich(TaskSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        TaskItem? item = StoredTask(snapshot.Key.Id);
        HashSet<Guid> focused = (item?.Steps ?? []).Where(step => step.Focus).Select(step => step.Id).ToHashSet();
        if (_tasks.IsLive && snapshot.Key.Kind == TaskRowKind.Session && _tasks.Current.FocusedSteps is { } running)
        {
            focused = running.ToHashSet();
        }

        return snapshot with { Item = item, FocusedSteps = focused };
    }

    internal IReadOnlyList<TaskMenuEntry> LabelMenuFor(TaskRowKey key) => LabelEntries(key);

    internal IReadOnlyList<TaskMenuEntry> TagMenuFor(TaskRowKey key) => LabelsEntries(key) ?? [];

    internal bool CanTag(TaskRowKey key) => CanLabel(key);

    internal IReadOnlyList<string>? TagsOf(TaskRowKey key) => LabelsOf(key);

    /// <summary>The open task's label: the task list's label, else what its row shows.</summary>
    internal (string? Name, string? Color) LabelOf(TaskRowKey key, TaskItem? item)
    {
        if (item is not null)
        {
            TaskLabel? label = LabelOf(item.LabelId);
            return label is null ? (null, null) : (label.Name, TaskLabelColors.Opaque(label.Color));
        }

        TaskListRowViewModel? row = AllRows().FirstOrDefault(candidate => candidate.Key == key);
        return (row?.CategoryName, row?.CategoryColor);
    }

    /// <summary>"Today 3:00 PM · 10 min before" (+ "· 2 more"), "2 reminders · due Fri 11/13", "Due Fri 11/13"; null for none.</summary>
    internal string? ReminderLineOf(TaskItem? item)
    {
        if (item is null)
        {
            return null;
        }

        DateOnly today = DateOnly.FromDateTime(LocalNow());
        int count = item.Reminders?.Count ?? 0;
        string due = item.DueOn is DateOnly dueOn ? DueText(dueOn, item.DueAt, today) : string.Empty;
        if (count > 0 && _nextReminders.TryGetValue(item.Id, out TaskReminderEntry? next))
        {
            DateTime ring = Local(next.RingAt);
            string when = DueText(DateOnly.FromDateTime(ring), TimeOnly.FromDateTime(ring), today);
            string more = count > 1 ? $" · {count - 1} more" : string.Empty;
            return $"{when} · {TaskReminderText.Rule(next, _time.LocalTimeZone)}{more}";
        }

        if (count > 0)
        {
            string reminders = count == 1 ? "1 reminder" : $"{count} reminders";
            return due.Length > 0 ? $"{reminders} · due {due}" : reminders;
        }

        return due.Length > 0 ? "Due " + due : null;
    }

    /// <summary>
    /// The open task's sync line. Per task the model knows only a conflict and a device it is kept on, so the rest is the sync
    /// light's own state (the same names, "Synced · Pixel 8, Home desktop").
    /// </summary>
    internal (string Text, MaterialIconKind Icon, bool Problem) SyncLineOf(TaskItem? item)
    {
        if (item?.Conflicts is { Count: > 0 })
        {
            return ("Changed on both devices", MaterialIconKind.CloudAlertOutline, true);
        }

        if (item?.LocalTo is Guid keeper)
        {
            return keeper == SelfId?.Invoke()
                ? ("Kept on this PC only", MaterialIconKind.CloudOffOutline, false)
                : ("Kept on another device", MaterialIconKind.CloudOffOutline, false);
        }

        SyncLightViewModel? light = SyncLight?.Invoke();
        if (light is null || light.Devices.Count <= 1)
        {
            return ("On this PC · no other devices linked", MaterialIconKind.CloudOutline, false);
        }

        if (light.IsOffline)
        {
            return (light.TipText, MaterialIconKind.CloudAlertOutline, true);
        }

        bool behind = light.TipText.StartsWith("Behind", StringComparison.Ordinal);
        return (light.TipText, behind ? MaterialIconKind.CloudOffOutline : MaterialIconKind.CloudCheckOutline, behind);
    }

    internal Task ToggleHighlightAsync(TaskRowKey key)
    {
        return ActAsync(actions => actions.ToggleHighlightAsync([key.Id]));
    }

    /// <summary>Steps can be dragged on a task of the next session's list or the running checklist, a Later item and a Queued task.</summary>
    internal bool CanMoveSteps(TaskRowKey key)
    {
        return key.Kind is TaskRowKind.Session or TaskRowKind.Later || key.Kind == TaskRowKind.Queued && _actions is not null;
    }

    /// <summary>The target on an open step: only on a task of the running session (its cap decides).</summary>
    internal bool CanFocusSteps(TaskRowKey key)
    {
        return key.Kind == TaskRowKind.Session && _tasks.IsLive;
    }

    internal async Task FocusStepAsync(Guid stepId)
    {
        Notice = string.Empty;
        if (await _tasks.FocusStepAsync(stepId) == SessionFocusResult.Full)
        {
            Notice = "This session is full. Finish one of its actions or move one to Queued first.";
        }
    }

    internal Task MoveStepAsync(TaskRowKey key, Guid stepId, Guid targetStepId, bool after)
    {
        return key.Kind switch
        {
            TaskRowKind.Session => _tasks.MoveToAsync(stepId, targetStepId, after),
            TaskRowKind.Later => EditLaterAsync(key.Id, item => item with { Subtasks = Moved(item.Subtasks ?? [], stepId, targetStepId, after) }),
            TaskRowKind.Queued => ActAsync(actions => actions.MoveStepAsync(key.Id, stepId, targetStepId, after)),
            _ => Task.CompletedTask,
        };
    }

    private static List<SessionSubtask> Moved(IReadOnlyList<SessionSubtask> steps, Guid stepId, Guid targetStepId, bool after)
    {
        List<SessionSubtask> list = steps.ToList();
        SessionSubtask? moving = list.FirstOrDefault(step => step.Id == stepId);
        if (moving is null || stepId == targetStepId || list.All(step => step.Id != targetStepId))
        {
            return list;
        }

        list.Remove(moving);
        list.Insert(list.FindIndex(step => step.Id == targetStepId) + (after ? 1 : 0), moving);
        return list;
    }

    // ---- Windows multi-select (spec 05 §12): click opens or closes and clears; Ctrl+click toggles; Shift+click selects a range ----

    // A row clicked where no panel is known (Enter on it): it opens.
    private void OnRowClick(TaskListRowViewModel row, TaskRowClick click)
    {
        OnPanelRowClick(null, row, click);
    }

    private void OnPanelRowClick(TaskSectionsPanelViewModel? panel, TaskListRowViewModel row, TaskRowClick click)
    {
        if (row.Kind == TaskRowKind.AutoSaved)
        {
            return; // not selectable: its menu is all it has
        }

        switch (click)
        {
            case TaskRowClick.Toggle:
                if (!_picked.Remove(row.Key))
                {
                    _picked.Add(row.Key);
                }

                Anchor(panel, row);
                break;
            case TaskRowClick.Range or TaskRowClick.AddRange when panel is not null && _anchorPanel == panel && _anchorKey is TaskRowKey anchor:
                List<TaskListRowViewModel> order = panel.VisibleRows().ToList();
                int from = order.FindIndex(candidate => candidate.Key == anchor);
                int to = order.FindIndex(candidate => candidate.Key == row.Key);
                if (from < 0 || to < 0)
                {
                    return;
                }

                if (click == TaskRowClick.Range)
                {
                    _picked.Clear();
                }

                foreach (TaskListRowViewModel picked in order.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1))
                {
                    _picked.Add(picked.Key);
                }

                break;
            case TaskRowClick.Range or TaskRowClick.AddRange:
                _picked.Add(row.Key);
                Anchor(panel, row);
                break;
            default:
                bool wasSelecting = _picked.Count > 0;
                ClearSelection();
                Anchor(panel, row);
                if (IsDetailOpen && _selectedKey == row.Key && !wasSelecting)
                {
                    CloseDetail();
                }
                else
                {
                    SelectRow(row);
                }

                return;
        }

        // Selecting closes the open task, as the prototype does.
        if (IsDetailOpen)
        {
            CloseDetail();
        }

        ShowPicked();
    }

    private void Anchor(TaskSectionsPanelViewModel? panel, TaskListRowViewModel row)
    {
        _anchorPanel = panel;
        _anchorKey = row.Key;
    }

    // Ctrl+A: every row the panel shows (open sections; the Shelf's filters and search already narrowed them).
    private void SelectAllIn(TaskSectionsPanelViewModel panel)
    {
        _picked.Clear();
        foreach (TaskListRowViewModel row in panel.VisibleRows().Where(row => row.Kind != TaskRowKind.AutoSaved))
        {
            _picked.Add(row.Key);
        }

        ShowPicked();
    }

    /// <summary>Esc, a plain click, or the page left: nothing selected. False when nothing was.</summary>
    public bool ClearSelection()
    {
        if (_picked.Count == 0)
        {
            return false;
        }

        _picked.Clear();
        ShowPicked();
        return true;
    }

    /// <summary>The selected tasks' ids, in no order (the bulk menu, 3.5).</summary>
    public IReadOnlyList<Guid> SelectedIds => _picked.Select(key => key.Id).Distinct().ToList();

    private void ShowPicked()
    {
        foreach (TaskListRowViewModel row in AllRows())
        {
            row.IsPicked = _picked.Contains(row.Key);
        }

        PickedCount = _picked.Count;
    }

    // ---- Dragging rows (and Alt+↑/↓) ----

    /// <summary>
    /// A row dropped in a section. Within one section it is reordered (This session's own order through the session list; the
    /// task list's order elsewhere). Into This session only through the session's cap (a full session says so and nothing moves);
    /// into Queued or a Shelf bin as a task-list move. A running session's own tasks don't leave it this way, and Finished today,
    /// Reminders due today and the label or tag groups take no drops from elsewhere.
    /// </summary>
    private async Task DropAsync(TaskRowDrop drop)
    {
        Notice = string.Empty;
        TaskListRowViewModel row = drop.Row;
        Guid id = row.Id;
        if (ReferenceEquals(drop.From, drop.To) || drop.From.Key == drop.To.Key && drop.From.Bin == drop.To.Bin)
        {
            await ReorderAsync(drop);
            return;
        }

        TaskItem? item = StoredTask(id);
        if (item is null || _actions is null || row.Kind is TaskRowKind.Done or TaskRowKind.Past or TaskRowKind.AutoSaved)
        {
            return;
        }

        if (_tasks.IsLive && row.Kind == TaskRowKind.Session && _tasks.IsInChecklist(id))
        {
            Notice = "A task of the running session stays in it. Tick it, or swap it out when you bring another one in.";
            return;
        }

        switch (drop.To.Bin)
        {
            case TaskBin.Session:
                await MoveQueuedToSessionAsync(id);
                break;
            case TaskBin.Queued:
            case TaskBin.Sort or TaskBin.Next or TaskBin.Later:
                await _actions.MoveAsync([id], drop.To.Bin.Value);
                break;
            default:
                Notice = "Drop it on This session, Queued, To sort, Next or Later.";
                break;
        }
    }

    // Within one section: This session's (and Queued's, while no session runs) order is the session list's; elsewhere the task list's.
    private Task ReorderAsync(TaskRowDrop drop)
    {
        TaskListRowViewModel row = drop.Row;
        if (row.Kind == TaskRowKind.Session)
        {
            return drop.Target is { Kind: TaskRowKind.Session } target && target.Id != row.Id
                ? _tasks.MoveToAsync(row.Id, target.Id, drop.After)
                : Task.CompletedTask;
        }

        if (StoredTask(row.Id) is null)
        {
            return Task.CompletedTask;
        }

        Guid? beforeId = drop.Target is null ? null : drop.After ? drop.NextId : drop.Target.Id;
        return beforeId == row.Id ? Task.CompletedTask : ActAsync(actions => actions.ReorderAsync(row.Id, beforeId));
    }
}
