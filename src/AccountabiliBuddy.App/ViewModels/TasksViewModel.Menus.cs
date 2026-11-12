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
    /// The shared row menu (round 2, brief §3.6 / §4.2, boards Tasks-PC-Task-Menu and Today-PC-Task-Menu) of one task the task list
    /// holds, the same on Tasks and Today: the task's title as a small heading; Move ›, Time limit ›, Label ›, Tags ›, Reminders &amp;
    /// due date ›, Highlight; then Rename, Copy ID, Sync devices ›; then Delete (red, last). Each shows what it is set to now at the
    /// right; no shortcut letters (F2, Del and H still work on the row through <see cref="TaskMenuEntry.Shortcut"/>). Moves follow the
    /// same rules as before (into This session only through the session's cap). "Rename one by one" is for a selection only.
    /// </summary>
    private IReadOnlyList<TaskMenuEntry> TaskListMenu(TaskRowKey key, TaskItem item)
    {
        IReadOnlyList<Guid> ids = [key.Id];
        var entries = new List<TaskMenuEntry>
        {
            new(item.Title, IsHeading: true),
            new("Move", Children: MoveChoices(ids, item.Bin, key), Icon: MaterialIconKind.ArrowRight, Value: TaskBins.Name(item.Bin)),
            new("Time limit", Children: TimeLimitChoicesFor(ids, item.EstimateMinutes), Icon: MaterialIconKind.TimerOutline,
                Value: item.EstimateMinutes is int minutes ? EstimateText.Of(minutes) : null),
            new("Label", Children: LabelChoicesFor(ids, item.LabelId, markCurrent: true), Icon: MaterialIconKind.CircleSlice8, Value: LabelOf(item.LabelId)?.Name),
            new("Tags", Children: TagChoicesFor(ids), Icon: MaterialIconKind.Pound, Value: TagsValue(item.Tags)),
            new("Reminders & due date", ViewAction: "reminders", Icon: MaterialIconKind.BellOutline,
                Value: $"{(item.Reminders ?? []).Count} of {TaskReminder.MaxPerTask}", OpensMore: true),
            HighlightEntry(ids, [item]),
            TaskMenuEntry.Separator,
            new("Rename", new RelayCommand(() => BeginDetailEdit(key, "title")), Icon: MaterialIconKind.FormTextbox, Shortcut: "F2"),
        };
        if (item.Number is TaskNumber number)
        {
            entries.Add(new TaskMenuEntry(
                "Copy ID", ViewAction: "copyId", Icon: MaterialIconKind.ContentCopy, Value: number.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (KeepOnEntries(key) is { } devices)
        {
            entries.Add(new TaskMenuEntry("Sync devices", Children: devices, Icon: MaterialIconKind.Sync, Value: SyncValue(devices)));
        }

        entries.Add(TaskMenuEntry.Separator);
        entries.Add(new TaskMenuEntry("Delete", new AsyncRelayCommand(() => DeleteAsync(key)), Icon: MaterialIconKind.DeleteOutline, IsDanger: true, Shortcut: "Del"));
        return entries;
    }

    // The Sync devices entry's value: where the task is kept now ("All", "This PC", a device's name); null when none is ticked.
    private static string? SyncValue(IReadOnlyList<TaskMenuEntry> devices)
    {
        string? chosen = devices.FirstOrDefault(entry => entry.IsChecked)?.Header;
        if (chosen is null)
        {
            return null;
        }

        if (chosen.StartsWith("All ", StringComparison.Ordinal))
        {
            return "All";
        }

        string name = chosen.StartsWith("Only ", StringComparison.Ordinal) ? chosen["Only ".Length..] : chosen;
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name;
    }

    // The Tags entry's value: the first tag, and how many more ("#Release +1"); null for none.
    private static string? TagsValue(IReadOnlyList<string>? tags)
    {
        if (tags is not { Count: > 0 })
        {
            return null;
        }

        return tags.Count == 1 ? "#" + tags[0] : $"#{tags[0]} +{tags.Count - 1}";
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
        var entries = new List<TaskMenuEntry> { new(heading, IsHeading: true, Value: left > 0 ? left + " left out" : null) };
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
            return new TaskMenuEntry(text, new RelayCommand(() => DeleteManyRequested?.Invoke(this, ids)), Icon: MaterialIconKind.DeleteOutline, IsDanger: true, Shortcut: "Del");
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
            MoveEntry(ids, TaskBin.Session, current),
            MoveEntry(ids, TaskBin.Queued, current),
            MoveEntry(ids, TaskBin.Sort, current),
            MoveEntry(ids, TaskBin.Next, current),
            MoveEntry(ids, TaskBin.Later, current),
            MoveEntry(ids, TaskBin.Done, current),
        };
        if (key is TaskRowKey row && (row.Kind == TaskRowKind.Later || row.Kind == TaskRowKind.Session && !_tasks.IsLive))
        {
            entries.Add(TaskMenuEntry.Separator);
            entries.Add(TaskMenuEntry.Group("Put in plan", PlanEntries(row), MaterialIconKind.ViewAgendaOutline));
        }

        return entries;
    }

    private TaskMenuEntry MoveEntry(IReadOnlyList<Guid> ids, TaskBin bin, TaskBin? current)
    {
        MaterialIconKind icon = TaskBinIcons.Of(bin);
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
                // "This session is full": a swap or "Add anyway" for each that found no room; closing leaves them where they were.
                OfferSwap(outcome.NeedsRoom);
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
            entries.Add(new TaskMenuEntry("#" + name, new AsyncRelayCommand(() => BulkAsync(actions => actions.ToggleTagAsync(ids, name))), Icon: icon, Value: hint));
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
            Icon: all ? MaterialIconKind.StarOffOutline : MaterialIconKind.StarOutline, Shortcut: "H");
    }

    // One action through the task actions, then the selection clears (the rows it named may have moved).
    private async Task BulkAsync(Func<TaskActionsService, Task> run)
    {
        Notice = string.Empty;
        await ActAsync(run);
        ClearSelection();
    }

    // ---- The dialogs the menus and rows open (M5 task 3) ----

    /// <summary>The device a version came from in "Changed on both devices" (name and kind, pc / phone / watch); set by the app.</summary>
    public Func<Guid, (string Name, string Kind)?>? DeviceOf { get; set; }

    /// <summary>
    /// A row's sync-problem cloud or the open task's sync line asked for "Changed on both devices" for this task: the page opens its
    /// dialog (<see cref="ConflictDialogFor"/>).
    /// </summary>
    public event EventHandler<Guid>? ConflictRequested;

    internal void RequestConflict(Guid id)
    {
        ConflictRequested?.Invoke(this, id);
    }

    /// <summary>"Changed on both devices" for a task the task list holds with a conflict; null otherwise.</summary>
    public ConflictDialogViewModel? ConflictDialogFor(Guid id)
    {
        if (_store is null || _actions is null || StoredTask(id)?.Conflicts is not { Count: > 0 })
        {
            return null;
        }

        return new ConflictDialogViewModel(_store, _actions, id, deviceId => DeviceOf?.Invoke(deviceId), SelfId?.Invoke(), _time);
    }

    /// <summary>
    /// "Rename one by one" for these tasks, in the order the task list keeps them (Today's bins, then the Shelf's); Rename all renames
    /// them as one action and clears the selection. Null when none of them is in the task list.
    /// </summary>
    public RenameOneByOneViewModel? RenameEachDialog(IReadOnlyList<Guid> ids)
    {
        List<(Guid Id, string Title)> tasks = InListOrder(ids).Select(task => (task.Id, task.Title)).ToList();
        return tasks.Count == 0 ? null : new RenameOneByOneViewModel(titles => BulkAsync(actions => actions.RenameEachAsync(titles)), tasks);
    }

    /// <summary>"Delete N tasks?" for these tasks; Delete runs <see cref="DeleteManyAsync"/>. Null when none of them is in the task list.</summary>
    public DeleteTasksViewModel? DeleteManyDialog(IReadOnlyList<Guid> ids)
    {
        List<TaskItem> tasks = InListOrder(ids);
        return tasks.Count == 0 ? null : new DeleteTasksViewModel(tasks.Select(task => task.Title).ToList(), () => DeleteManyAsync(tasks.Select(task => task.Id).ToList()));
    }

    private List<TaskItem> InListOrder(IReadOnlyList<Guid> ids)
    {
        List<TaskBin> order = [.. TaskBins.Today, .. TaskBins.Shelf];
        return ids.Distinct().Select(StoredTask).OfType<TaskItem>()
            .OrderBy(task => order.IndexOf(task.Bin) is int at and >= 0 ? at : order.Count)
            .ThenBy(task => task.Position)
            .ToList();
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
