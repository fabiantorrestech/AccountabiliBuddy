using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

// Tasks redesign 3.5 (spec 03 §C): a task's right-click / ⋯ menu, the bulk menu of a selection, and the Reminders & due date menu.
// Every change goes through the task actions (one undo step per action, bulk included); into This session only through the
// session's cap (MoveToSessionAsync), never around it. A running session's own tasks keep their old menu: they leave the session
// only as the session allows.
public sealed partial class TasksViewModel
{
    /// <summary>The time limits a menu offers, in minutes.</summary>
    private static readonly int[] TimeLimitChoices = [10, 15, 25, 50, 90];

    /// <summary>The reminder settings ("Add new reminders to" is a task's calendar default); set by the app.</summary>
    public TaskReminderSettingsService? ReminderSettings { get; set; }

    /// <summary>
    /// "Rename one by one" was chosen for these tasks (the selection, in no order): the page opens its dialog (M5 task 3). The entry
    /// shows only while something listens.
    /// </summary>
    public event EventHandler<IReadOnlyList<Guid>>? RenameEachRequested;

    /// <summary>
    /// "Delete N" was chosen for these tasks: the page opens its confirm dialog (M5 task 3), which calls <see cref="DeleteManyAsync"/>.
    /// While nothing listens, the menu asks in a small submenu instead.
    /// </summary>
    public event EventHandler<IReadOnlyList<Guid>>? DeleteManyRequested;

    /// <summary>The task list's copy of a row's task when the redesign's menu applies to it; null for rows that keep their old menu.</summary>
    private TaskItem? MenuTask(TaskRowKey key)
    {
        if (_actions is null || key.Kind == TaskRowKind.AutoSaved || _tasks.IsLive && _tasks.IsInChecklist(key.Id))
        {
            return null;
        }

        return StoredTask(key.Id);
    }

    // ---- One task ----

    /// <summary>
    /// The menu of one task the task list holds: Rename, Move ›, Time limit ›, Label ›, Tags ›, Reminders &amp; due date…, Highlight,
    /// Sync devices ›, Delete, Copy ID. ("Rename one by one" is for a selection only.)
    /// </summary>
    private IReadOnlyList<TaskMenuEntry> TaskListMenu(TaskRowKey key, TaskItem item)
    {
        IReadOnlyList<Guid> ids = [key.Id];
        var entries = new List<TaskMenuEntry>
        {
            new("Rename", new RelayCommand(() => BeginDetailEdit(key, "title")), Icon: MaterialIconKind.FormTextbox, Hint: "F2"),
            TaskMenuEntry.Group("Move", MoveChoices(ids, item.Bin, key), MaterialIconKind.ArrowRight),
            TaskMenuEntry.Group("Time limit", TimeLimitChoicesFor(ids, item.EstimateMinutes), MaterialIconKind.TimerOutline),
            TaskMenuEntry.Group("Label", LabelChoicesFor(ids, item.LabelId, markCurrent: true), MaterialIconKind.CircleSlice8),
            TaskMenuEntry.Group("Tags", TagChoicesFor(ids), MaterialIconKind.Pound),
            TaskMenuEntry.View("Reminders & due date…", "reminders", MaterialIconKind.BellOutline),
            HighlightEntry(ids, [item]),
        };
        if (KeepOnEntries(key) is { } devices)
        {
            entries.Add(TaskMenuEntry.Group("Sync devices", devices, MaterialIconKind.Sync));
        }

        entries.Add(TaskMenuEntry.Separator);
        entries.Add(new TaskMenuEntry("Delete", new AsyncRelayCommand(() => DeleteAsync(key)), Icon: MaterialIconKind.DeleteOutline, IsDanger: true, Hint: "Del"));
        if (item.Number is TaskNumber number)
        {
            entries.Add(new TaskMenuEntry(
                "Copy ID", ViewAction: "copyId", Icon: MaterialIconKind.ContentCopy, Hint: number.Value.ToString(CultureInfo.InvariantCulture)));
        }

        return entries;
    }

    // ---- A selection (right-click inside it) ----

    /// <summary>
    /// The bulk menu for the selected rows (two or more): Move to…, Time limit, Label, Tags, Highlight, Rename one by one, Sync, Delete N.
    /// Each acts on every selected task the task list holds as one action (one undo step) and then clears the selection; a running
    /// session's own tasks and rows the task list doesn't hold are left out (the heading says how many). Empty for fewer than two.
    /// </summary>
    internal IReadOnlyList<TaskMenuEntry> SelectionMenu()
    {
        List<TaskRowKey> keys = _picked.ToList();
        if (keys.Count < 2)
        {
            return [];
        }

        List<TaskRowKey> held = keys.Where(key => MenuTask(key) is not null).ToList();
        List<Guid> ids = held.Select(key => key.Id).Distinct().ToList();
        int left = keys.Select(key => key.Id).Distinct().Count() - ids.Count;
        string heading = ids.Count == 1 ? "1 task" : ids.Count + " tasks";
        var entries = new List<TaskMenuEntry> { new(heading, IsHeading: true, Hint: left > 0 ? left + " left out" : null) };
        if (ids.Count == 0)
        {
            return entries;
        }

        List<TaskItem> tasks = ids.Select(StoredTask).OfType<TaskItem>().ToList();
        entries.Add(TaskMenuEntry.Group("Move to…", MoveChoices(ids, null, null), MaterialIconKind.ArrowRight));
        entries.Add(TaskMenuEntry.Group("Time limit", TimeLimitChoicesFor(ids, null), MaterialIconKind.TimerOutline));
        entries.Add(TaskMenuEntry.Group("Label", LabelChoicesFor(ids, null, markCurrent: false), MaterialIconKind.CircleSlice8));
        entries.Add(TaskMenuEntry.Group("Tags", TagChoicesFor(ids), MaterialIconKind.Pound));
        entries.Add(HighlightEntry(ids, tasks));
        if (RenameEachRequested is not null && ids.Count > 1)
        {
            entries.Add(TaskMenuEntry.Do("Rename one by one", () => { RenameEachRequested?.Invoke(this, ids); }, MaterialIconKind.FormTextbox));
        }

        if (BulkSyncChoices(held) is { } sync)
        {
            entries.Add(TaskMenuEntry.Group("Sync", sync, MaterialIconKind.Sync));
        }

        entries.Add(TaskMenuEntry.Separator);
        entries.Add(BulkDeleteEntry(ids));
        return entries;
    }

    /// <summary>
    /// Deletes the tasks as one action (one undo step): each goes to History as deleted, where Bring back can revive it for 30 days.
    /// The selection clears. Used by the Delete N confirm (the menu's own, or the dialog of M5 task 3).
    /// </summary>
    public Task DeleteManyAsync(IReadOnlyList<Guid> ids)
    {
        return BulkAsync(actions => actions.DeleteAsync(ids));
    }

    // "Delete N": the dialog when one listens, else a submenu that asks once.
    private TaskMenuEntry BulkDeleteEntry(IReadOnlyList<Guid> ids)
    {
        string text = "Delete " + ids.Count;
        if (DeleteManyRequested is not null)
        {
            return new TaskMenuEntry(text, new RelayCommand(() => DeleteManyRequested?.Invoke(this, ids)), Icon: MaterialIconKind.DeleteOutline, IsDanger: true, Hint: "Del");
        }

        return new TaskMenuEntry(
            text,
            Children:
            [
                new TaskMenuEntry("They stay in History for 30 days", IsHeading: true),
                new TaskMenuEntry($"Delete {ids.Count} tasks", new AsyncRelayCommand(() => DeleteManyAsync(ids)), Icon: MaterialIconKind.DeleteOutline, IsDanger: true),
            ],
            Icon: MaterialIconKind.DeleteOutline,
            IsDanger: true);
    }

    // "Sync": the same choices as one task's Sync devices (all devices, this PC only, or one other device), for each selected task that
    // can be kept on one device (ADR 72's local items); null when none can, or this PC is in no sync group.
    private List<TaskMenuEntry>? BulkSyncChoices(IReadOnlyList<TaskRowKey> keys)
    {
        List<(LocalItemKind Kind, Guid Id)> local = keys
            .Select(key => (Kind: LocalKindOf(key), key.Id))
            .Where(pair => pair.Kind is not null)
            .Select(pair => (pair.Kind!.Value, pair.Id))
            .ToList();
        if (local.Count == 0 || SetLocal is null || SelfId?.Invoke() is not Guid self)
        {
            return null;
        }

        var entries = new List<TaskMenuEntry>
        {
            new("All my devices (synced)", new AsyncRelayCommand(() => SetLocalManyAsync(local, null)), Icon: MaterialIconKind.Devices),
            new("Only this PC", new AsyncRelayCommand(() => SetLocalManyAsync(local, self)), Icon: MaterialIconKind.Laptop),
        };
        foreach ((Guid id, string name) in KeepOnDevices?.Invoke() ?? [])
        {
            Guid device = id;
            entries.Add(new TaskMenuEntry($"Only {name}", new AsyncRelayCommand(() => SetLocalManyAsync(local, device))));
        }

        return entries;
    }

    private async Task SetLocalManyAsync(IReadOnlyList<(LocalItemKind Kind, Guid Id)> items, Guid? keeper)
    {
        if (SetLocal is null)
        {
            return;
        }

        int refused = 0;
        foreach ((LocalItemKind kind, Guid id) in items)
        {
            if (!await SetLocal(kind, id, keeper))
            {
                refused++;
            }
        }

        ClearSelection();
        Notice = refused > 0
            ? $"{refused} of them can't be kept on one device right now."
            : keeper is null ? "They sync to all your devices again." : keeper == SelfId?.Invoke() ? "Kept on this PC only." : "Sent to that device: kept there only, once they sync.";
        RebuildLists();
    }

    // ---- The submenus (one task or a selection) ----

    // Move: This session (the session's cap decides), Queued, To sort, Next, Later, Finished; the bin a single task is in is ticked.
    // "Put in plan" follows where the old menu offered it (a Later task, a task of the next session's list while none runs).
    private List<TaskMenuEntry> MoveChoices(IReadOnlyList<Guid> ids, TaskBin? current, TaskRowKey? key)
    {
        var entries = new List<TaskMenuEntry>
        {
            MoveEntry(ids, TaskBin.Session, current, MaterialIconKind.PlayCircleOutline),
            MoveEntry(ids, TaskBin.Queued, current, MaterialIconKind.PlaylistPlus),
            MoveEntry(ids, TaskBin.Sort, current, MaterialIconKind.Tray),
            MoveEntry(ids, TaskBin.Next, current, MaterialIconKind.Tray),
            MoveEntry(ids, TaskBin.Later, current, MaterialIconKind.Tray),
            MoveEntry(ids, TaskBin.Done, current, MaterialIconKind.CheckCircleOutline),
        };
        if (key is TaskRowKey row && (row.Kind == TaskRowKind.Later || row.Kind == TaskRowKind.Session && !_tasks.IsLive))
        {
            entries.Add(TaskMenuEntry.Separator);
            entries.Add(TaskMenuEntry.Group("Put in plan", PlanEntries(row), MaterialIconKind.ViewAgendaOutline));
        }

        return entries;
    }

    private TaskMenuEntry MoveEntry(IReadOnlyList<Guid> ids, TaskBin bin, TaskBin? current, MaterialIconKind icon)
    {
        string text = bin == TaskBin.Done ? "Finished" : TaskBins.Name(bin);
        if (current == bin)
        {
            return new TaskMenuEntry(text, IsChecked: true, Icon: icon);
        }

        return new TaskMenuEntry(text, new AsyncRelayCommand(() => MoveManyAsync(ids, bin)), Icon: icon);
    }

    // A move from a menu. Into This session only through the session's add (its cap): what finds no room stays and says so.
    private async Task MoveManyAsync(IReadOnlyList<Guid> ids, TaskBin bin)
    {
        Notice = string.Empty;
        if (_actions is null)
        {
            return;
        }

        if (bin == TaskBin.Session)
        {
            TaskMoveOutcome outcome = await _actions.MoveToSessionAsync(ids);
            ClearSelection();
            if (outcome.NeedsRoom.Count > 0)
            {
                Notice = outcome.NeedsRoom.Count == ids.Count
                    ? "This session is full. Finish one of its tasks or move one to Queued first."
                    : $"This session is full: {outcome.NeedsRoom.Count} of them stayed where they were.";
            }

            return;
        }

        await BulkAsync(actions => actions.MoveAsync(ids, bin));
    }

    // Time limit: 10, 15, 25, 50, 90 min and No limit; a single task's own is ticked.
    private List<TaskMenuEntry> TimeLimitChoicesFor(IReadOnlyList<Guid> ids, int? current)
    {
        bool single = ids.Count == 1;
        var entries = TimeLimitChoices
            .Select(minutes => new TaskMenuEntry(
                EstimateText.Of(minutes), new AsyncRelayCommand(() => BulkAsync(actions => actions.SetTimeLimitAsync(ids, minutes))),
                IsChecked: single && current == minutes, Icon: MaterialIconKind.TimerOutline))
            .ToList();
        entries.Add(new TaskMenuEntry(
            "No limit", new AsyncRelayCommand(() => BulkAsync(actions => actions.SetTimeLimitAsync(ids, null))),
            IsChecked: single && current is null, Icon: MaterialIconKind.TimerOffOutline));
        return entries;
    }

    // Label: each label with its dot, No label, New label…; a single task's own is ticked.
    private List<TaskMenuEntry> LabelChoicesFor(IReadOnlyList<Guid> ids, Guid? current, bool markCurrent)
    {
        var entries = new List<TaskMenuEntry>();
        foreach (TaskLabel label in LiveLabels().Where(label => label.Name.Trim().Length > 0))
        {
            Guid id = label.Id;
            entries.Add(new TaskMenuEntry(
                label.Name, new AsyncRelayCommand(() => BulkAsync(actions => actions.SetLabelAsync(ids, id))),
                IsChecked: markCurrent && current == id, Dot: TaskCategoryBrushes.Of(TaskLabelColors.Opaque(label.Color))));
        }

        entries.Add(new TaskMenuEntry(
            "No label", new AsyncRelayCommand(() => BulkAsync(actions => actions.SetLabelAsync(ids, null))), IsChecked: markCurrent && current is null));
        entries.Add(TaskMenuEntry.Do("New label…", () => AskNewCategory(categoryId => ApplyNewLabelAsync(ids, categoryId)), MaterialIconKind.Plus));
        return entries;
    }

    // A label made from "New label…" (a category of the session list underneath, ADR 76): found by its name in the task list.
    private Task ApplyNewLabelAsync(IReadOnlyList<Guid> ids, Guid? categoryId)
    {
        string? name = _tasks.Categories.FirstOrDefault(category => category.Id == categoryId)?.Name;
        Guid? labelId = LiveLabels().FirstOrDefault(label => name is not null && TaskLabel.SameName(label.Name, name))?.Id ?? LabelIdOf(categoryId);
        return labelId is null ? Task.CompletedTask : BulkAsync(actions => actions.SetLabelAsync(ids, labelId));
    }

    // Tags: every tag in the task list, ticked when all the tasks have it, a dash when some do ("2 of 3"); a click puts it on all of
    // them, or takes it off all when they all had it. New tag… adds one to all.
    private List<TaskMenuEntry> TagChoicesFor(IReadOnlyList<Guid> ids)
    {
        List<TaskItem> tasks = ids.Select(StoredTask).OfType<TaskItem>().ToList();
        var entries = new List<TaskMenuEntry>();
        foreach (string tag in TaskLabels.AllOf(LiveTasks().Select(task => task.Tags)))
        {
            string name = tag;
            int having = tasks.Count(task => TaskLabels.Has(task.Tags, name));
            MaterialIconKind icon = having == tasks.Count && having > 0
                ? MaterialIconKind.CheckboxMarked
                : having > 0 ? MaterialIconKind.MinusBoxOutline : MaterialIconKind.CheckboxBlankOutline;
            string? hint = tasks.Count > 1 && having > 0 && having < tasks.Count ? $"{having} of {tasks.Count}" : null;
            entries.Add(new TaskMenuEntry("#" + name, new AsyncRelayCommand(() => BulkAsync(actions => actions.ToggleTagAsync(ids, name))), Icon: icon, Hint: hint));
        }

        entries.Add(TaskMenuEntry.Do("New tag…", () => AskNewLabel(name => BulkAsync(actions => actions.AddTagsAsync(ids, [name]))), MaterialIconKind.Plus));
        return entries;
    }

    // Highlight, or Remove highlight when every one of them has it (the H key).
    private TaskMenuEntry HighlightEntry(IReadOnlyList<Guid> ids, IReadOnlyList<TaskItem> tasks)
    {
        bool all = tasks.Count > 0 && tasks.All(task => task.Highlight);
        return new TaskMenuEntry(
            all ? "Remove highlight" : "Highlight", new AsyncRelayCommand(() => BulkAsync(actions => actions.SetHighlightAsync(ids, !all))),
            Icon: all ? MaterialIconKind.StarOffOutline : MaterialIconKind.StarOutline, Hint: "H");
    }

    // One action through the task actions, then the selection clears (the rows it named may have moved).
    private async Task BulkAsync(Func<TaskActionsService, Task> run)
    {
        Notice = string.Empty;
        await ActAsync(run);
        ClearSelection();
    }

    // ---- Reminders & due date ----

    /// <summary>The task has a Reminders &amp; due date menu: the task list holds it (and it isn't the running session's own).</summary>
    internal bool HasReminderMenu(TaskRowKey key) => _actions is not null && MenuTask(key) is not null;

    /// <summary>The Reminders &amp; due date menu of a task the task list holds; null for any other row.</summary>
    internal ReminderMenuViewModel? ReminderMenuFor(TaskRowKey key)
    {
        if (_actions is null || !HasReminderMenu(key))
        {
            return null;
        }

        return new ReminderMenuViewModel(
            key.Id, StoredTask, _actions, () => ReminderSettings?.Current.NewRemindersTo ?? TaskCalendarDestination.GoogleCalendar,
            _reminders is null ? null : _reminders.RingTest, _time);
    }
}
