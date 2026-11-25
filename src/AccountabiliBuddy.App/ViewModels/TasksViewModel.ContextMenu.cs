using System.Globalization;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

// Tasks redesign 8a (SPEC "Selection, drag and drop, right-click menu", boards 13 / 19 / 20 / 30): the Tasks page's right-click menu for
// the selection and the select bar in the bottom row. Every change goes through the task actions (one undo step, bulk included, with a
// toast that counts the tasks); into This session only through the session's cap (MoveToSessionAsync). A running session's own tasks are
// left out of the bulk actions, and the heading says how many. Today's rows keep their menus (TasksViewModel.Menus).
public sealed partial class TasksViewModel
{
    /// <summary>The time limits the right-click menu offers, in minutes (8a.65).</summary>
    private static readonly int[] ContextTimeLimits = [10, 15, 25, 50, 90];

    /// <summary>
    /// "Batch edit (N)…", Ctrl+E with 2 or more, or the select bar's "Move or label (N)" asked for Move and label with these tasks (item 8c
    /// opens it; the selection stays meanwhile). Nothing listens yet.
    /// </summary>
    public event EventHandler<IReadOnlyList<Guid>>? MoveAndLabelRequested;

    /// <summary>The select bar's "Move or label (3)"; "Move or label" for one task (the counts rule).</summary>
    public string MoveOrLabelText => SelectionCountText.Action("Move or label", PickedCount);

    /// <summary>The select bar's "Delete (3)"; "Delete" for one task.</summary>
    public string DeleteSelectedText => SelectionCountText.Action("Delete", PickedCount);

    /// <summary>The select bar's ×: nothing selected.</summary>
    [RelayCommand]
    private void StopSelecting() => ClearSelection();

    /// <summary>The select bar's "Move or label (N)": Move and label with the selection (item 8c).</summary>
    [RelayCommand]
    private void MoveOrLabelSelection() => RequestMoveAndLabel(HeldIds(_picked.ToList()));

    /// <summary>The select bar's "Delete (N)": the selected tasks go to Archive › Recently deleted at once, with Undo (no confirm, 8a.75).</summary>
    [RelayCommand]
    private Task DeleteSelectionAsync() => DeleteCountedAsync(HeldIds(_picked.ToList()));

    /// <summary>The row sits in one of the Tasks page's Shelf columns (where the redesign's right-click menu applies).</summary>
    internal bool IsShelfRow(TaskRowKey key)
    {
        return Shelf.Panel.Sections.Any(section => section.Rows.Any(row => row.Key == key));
    }

    /// <summary>
    /// A right-click on a Shelf row: a row outside the selection is selected alone first (as in Explorer, 8a.25), then the menu for the whole
    /// selection. Null when the redesign's menu doesn't apply (a running session's own task alone, a step's row, Auto-saved): the row's own
    /// menu opens instead.
    /// </summary>
    internal IReadOnlyList<TaskMenuEntry>? OpenContextMenu(TaskListRowViewModel row, TaskSectionViewModel? column)
    {
        if (row.OwnerKey is not null || row.Kind == TaskRowKind.AutoSaved || !IsShelfRow(row.Key))
        {
            return null;
        }

        if (!_picked.Contains(row.Key))
        {
            _picked.Clear();
            _picked.Add(row.Key);
            _anchorPanel = Shelf.Panel;
            _anchorKey = row.Key;
            if (IsDetailOpen)
            {
                CloseDetail();
            }

            ShowPicked();
        }

        return ContextMenuFor(_picked.ToList(), column);
    }

    /// <summary>
    /// The right-click menu's entries for <paramref name="keys"/> (8a.29-8a.51), each with its <see cref="TaskMenuEntry.Id"/> and key. One
    /// task: its title as the heading, "Edit…" first and today's Reminders &amp; due date, Rename and Copy ID after Link to goal (O5). Null
    /// for one task the redesign's menu doesn't apply to.
    /// </summary>
    internal IReadOnlyList<TaskMenuEntry>? ContextMenuFor(IReadOnlyList<TaskRowKey> keys, TaskSectionViewModel? column)
    {
        List<TaskRowKey> held = keys.Where(key => MenuTask(key) is not null).ToList();
        List<Guid> ids = held.Select(key => key.Id).Distinct().ToList();
        int selected = keys.Select(key => key.Id).Distinct().Count();
        int left = selected - ids.Count;
        if (selected == 0 || selected == 1 && ids.Count == 0)
        {
            return null;
        }

        List<TaskItem> tasks = ids.Select(StoredTask).OfType<TaskItem>().ToList();
        bool one = selected == 1;
        string heading = one ? tasks[0].Title : selected.ToString(CultureInfo.CurrentCulture) + " tasks selected";
        if (left > 0)
        {
            heading += " · " + left.ToString(CultureInfo.CurrentCulture) + " left out";
        }

        var entries = new List<TaskMenuEntry> { new(heading, IsHeading: true) };
        int count = ids.Count;
        if (count > 0)
        {
            entries.AddRange(EditGroup(held, ids, tasks, one));
            entries.Add(TaskMenuEntry.Separator);
            entries.Add(new TaskMenuEntry(
                SelectionCountText.Action("Mark as done", count), new AsyncRelayCommand(() => MarkDoneAsync(ids)), Icon: MaterialIconKind.CheckCircleOutline,
                Shortcut: "D", Id: "done"));
            entries.Add(HighlightEntry(ids, tasks) with { Shortcut = "H" });
            if (BulkSyncChoices(held) is { } sync)
            {
                entries.Add(new TaskMenuEntry(SelectionCountText.Action("Sync", count), Children: sync, Icon: MaterialIconKind.Sync, Id: "sync"));
            }

            entries.Add(TaskMenuEntry.Separator);
        }

        if (column is not null && SelectableRowsOf(column) is { Count: > 0 } columnRows)
        {
            entries.Add(new TaskMenuEntry(
                $"Select all in {ColumnName(column)} ({columnRows.Count.ToString(CultureInfo.CurrentCulture)})", new RelayCommand(() => SelectColumn(column)),
                Icon: MaterialIconKind.CheckboxMultipleMarkedOutline, Shortcut: "Ctrl+A", Id: "selectAll"));
        }

        entries.Add(new TaskMenuEntry(
            SelectionCountText.Action("Clear selection", selected), new RelayCommand(() => ClearSelection()), Icon: MaterialIconKind.Close, Shortcut: "Ctrl+U",
            Id: "clear"));
        if (count > 0)
        {
            entries.Add(TaskMenuEntry.Separator);
            entries.Add(new TaskMenuEntry(
                SelectionCountText.Action("Delete", count), new AsyncRelayCommand(() => DeleteCountedAsync(ids)), Icon: MaterialIconKind.DeleteOutline,
                IsDanger: true, Shortcut: "Del", Id: "delete"));
        }

        return entries;
    }

    // The Edit group (8a.31, 8a.36-8a.42, 8a.50): its heading, then Batch edit / Edit…, Move to, Label, Tags, Time limit, Link to goal, and
    // Rename one by one (2 or more) or one task's Reminders & due date, Rename and Copy ID, all on the guide line.
    private List<TaskMenuEntry> EditGroup(List<TaskRowKey> held, List<Guid> ids, List<TaskItem> tasks, bool one)
    {
        int count = ids.Count;
        var entries = new List<TaskMenuEntry>
        {
            new("Edit", IsHeading: true, Icon: MaterialIconKind.PencilOutline),
            new(count > 1 ? SelectionCountText.Action("Batch edit", count) + "…" : "Edit…", new RelayCommand(() => EditKeys(held)),
                Icon: MaterialIconKind.ViewGridOutline, Shortcut: "Ctrl+E", Id: "edit", IsStrong: true, OnGuide: true),
            new(SelectionCountText.Action("Move to", count), Children: MoveToChoices(ids, tasks), Icon: MaterialIconKind.FolderMoveOutline,
                Shortcut: "M", Id: "move", OnGuide: true),
            new(SelectionCountText.Action("Label", count), Children: LabelChoicesByCategory(ids, tasks), Icon: MaterialIconKind.CircleSlice8,
                Shortcut: "L", Id: "label", OnGuide: true),
            new(SelectionCountText.Action("Tags", count), Children: TagChoicesCounted(ids, tasks), Icon: MaterialIconKind.Pound,
                Shortcut: "T", Id: "tags", OnGuide: true),
            new(SelectionCountText.Action("Time limit", count), Children: TimeLimitChoicesCounted(ids, tasks), Icon: MaterialIconKind.TimerOutline,
                Id: "time", OnGuide: true),
            GoalEntryCounted(ids, tasks),
        };

        if (one && held.Count > 0 && tasks.Count == 1)
        {
            // O5: one task keeps today's Reminders & due date, Rename (F2) and Copy ID, in today's order after Link to goal.
            TaskRowKey key = held[0];
            TaskItem item = tasks[0];
            entries.Add(new TaskMenuEntry(
                "Reminders & due date", ViewAction: "reminders", Icon: MaterialIconKind.BellOutline,
                Value: $"{(item.Reminders ?? []).Count} of {TaskReminder.MaxPerTask}", Id: "reminders", OnGuide: true));
            entries.Add(new TaskMenuEntry(
                "Rename", new RelayCommand(() => BeginDetailEdit(key, "title")), Icon: MaterialIconKind.FormTextbox, Shortcut: "F2", Id: "rename", OnGuide: true));
            if (item.Number is TaskNumber number)
            {
                entries.Add(new TaskMenuEntry(
                    "Copy ID", ViewAction: "copyId", Icon: MaterialIconKind.ContentCopy, Value: number.Value.ToString(CultureInfo.InvariantCulture), Id: "copyId",
                    OnGuide: true));
            }
        }
        else if (count > 1 && RenameEachRequested is not null)
        {
            entries.Add(new TaskMenuEntry(
                SelectionCountText.Action("Rename one by one", count) + "…", new RelayCommand(() => RenameEachRequested?.Invoke(this, ids)),
                Icon: MaterialIconKind.FormTextbox, Id: "rename", OnGuide: true));
        }

        return entries;
    }

    // ---- The submenus ----

    // Move to (8a.55, 8a.56): This session (through the session's cap), Today (= Queued), Next, To sort, Later; "Current" when every task is
    // there, "2 here" when some are.
    private List<TaskMenuEntry> MoveToChoices(IReadOnlyList<Guid> ids, IReadOnlyList<TaskItem> tasks)
    {
        var entries = new List<TaskMenuEntry>();
        foreach (TaskBin bin in new[] { TaskBin.Session, TaskBin.Queued, TaskBin.Next, TaskBin.Sort, TaskBin.Later })
        {
            TaskBin target = bin;
            string name = bin == TaskBin.Queued ? "Today" : TaskBins.Name(bin);
            int here = tasks.Count(task => task.Bin == bin);
            string? tag = here == 0 ? null : here == tasks.Count ? "Current" : here.ToString(CultureInfo.CurrentCulture) + " here";
            entries.Add(new TaskMenuEntry(
                name, new AsyncRelayCommand(() => MoveCountedAsync(ids, target, name)), Icon: TaskBinIcons.Redesigned(bin), Value: tag, AccentIcon: true,
                Id: "move." + bin.ToString().ToLowerInvariant()));
        }

        return entries;
    }

    // Label (8a.58-8a.60): each category (its icon and name) with its labels on a guide line (dot, short name, "Current" or "N have it"),
    // "Other labels" last; then New label… and No label.
    private List<TaskMenuEntry> LabelChoicesByCategory(IReadOnlyList<Guid> ids, IReadOnlyList<TaskItem> tasks)
    {
        var entries = new List<TaskMenuEntry>();
        foreach (LabelFilterGroupViewModel group in Shelf.MenuGroups)
        {
            entries.Add(new TaskMenuEntry(group.GroupId is null ? "Other labels" : group.Name, IsHeading: true, Icon: group.Icon));
            foreach (LabelFilterChipViewModel label in group.Items)
            {
                Guid labelId = label.Id;
                string fullName = label.Name;
                int having = tasks.Count(task => task.LabelId == labelId);
                entries.Add(new TaskMenuEntry(
                    label.ShortName, new AsyncRelayCommand(() => LabelCountedAsync(ids, labelId, fullName)), Dot: label.Dot, Value: HaveIt(having, tasks.Count),
                    OnGuide: true, Id: "label.item"));
            }
        }

        entries.Add(TaskMenuEntry.Do("New label…", () => AskNewCategory(categoryId => ApplyNewLabelAsync(ids, categoryId)), MaterialIconKind.Plus));
        entries.Add(TaskMenuEntry.Separator);
        int none = tasks.Count(task => task.LabelId is null || LabelOf(task.LabelId) is null);
        entries.Add(new TaskMenuEntry(
            "No label", new AsyncRelayCommand(() => LabelCountedAsync(ids, null, null)), Value: HaveIt(none, tasks.Count), Id: "label.none"));
        return entries;
    }

    // "Current" when every task has it, "1 have it" when some do (board 19's wording), none otherwise.
    private static string? HaveIt(int having, int of)
    {
        return having == 0 ? null : having == of ? "Current" : having.ToString(CultureInfo.CurrentCulture) + " have it";
    }

    // Tags (8a.61-8a.64): ☑ all have it, ⊟ some ("1 of 3"), ☐ none; a click puts it on all, or off all when all had it; then New tag… (an
    // inline box: Enter adds the typed tag to all, a leading "#" dropped).
    private List<TaskMenuEntry> TagChoicesCounted(IReadOnlyList<Guid> ids, IReadOnlyList<TaskItem> tasks)
    {
        var items = new List<(bool Shared, TaskMenuEntry Entry)>();
        foreach (string tag in _store!.VisibleTags())
        {
            string name = tag;
            int having = tasks.Count(task => TaskLabels.Has(ShownTagsOf(task.Tags), name));
            bool all = having == tasks.Count && having > 0;
            MaterialIconKind icon = all ? MaterialIconKind.CheckboxMarked : having > 0 ? MaterialIconKind.MinusBoxOutline : MaterialIconKind.CheckboxBlankOutline;
            string? hint = having > 0 && !all ? $"{having} of {tasks.Count}" : null;
            items.Add((IsSharedTag(name), new TaskMenuEntry(
                "#" + name, new AsyncRelayCommand(() => ToggleTagCountedAsync(ids, name, !all)), Icon: icon, Value: hint, AccentIcon: having > 0, Id: "tag")));
        }

        List<TaskMenuEntry> entries = ScopeGrouped(items);
        if (entries.Count > 0)
        {
            entries.Add(TaskMenuEntry.Separator);
        }

        entries.Add(new TaskMenuEntry("New tag…", Icon: MaterialIconKind.Plus, Id: "tag.new", Submit: typed => AddTagCountedAsync(ids, typed)));
        return entries;
    }

    // Time limit (8a.65): 10, 15, 25, 50, 90 min, then No limit; "Current" on the one every task has.
    private List<TaskMenuEntry> TimeLimitChoicesCounted(IReadOnlyList<Guid> ids, IReadOnlyList<TaskItem> tasks)
    {
        var entries = ContextTimeLimits
            .Select(minutes => new TaskMenuEntry(
                EstimateText.Of(minutes), new AsyncRelayCommand(() => TimeLimitCountedAsync(ids, minutes)), Icon: MaterialIconKind.TimerOutline,
                Value: tasks.Count > 0 && tasks.All(task => task.EstimateMinutes == minutes) ? "Current" : null, Id: "time.choice"))
            .ToList();
        entries.Add(TaskMenuEntry.Separator);
        entries.Add(new TaskMenuEntry(
            "No limit", new AsyncRelayCommand(() => TimeLimitCountedAsync(ids, null)), Icon: MaterialIconKind.Close,
            Value: tasks.Count > 0 && tasks.All(task => task.EstimateMinutes is null) ? "Current" : null, Id: "time.none"));
        return entries;
    }

    // Link to goal (8a.66): the user's goals (a flag in the accent), then Unlink; TaskGoalMenu builds it with the count.
    private TaskMenuEntry GoalEntryCounted(IReadOnlyList<Guid> ids, IReadOnlyList<TaskItem> tasks)
    {
        Guid? current = tasks.Count > 0 && tasks.All(task => task.GoalId == tasks[0].GoalId) ? tasks[0].GoalId : null;
        TaskMenuEntry entry = TaskGoalMenu.Entry(LinkableGoals?.Invoke() ?? [], current, (goalId, goalName) => LinkCountedAsync(ids, goalId, goalName), ids.Count);
        List<TaskMenuEntry>? children = entry.Children?.Select(child => child.IsSeparator
            ? child
            : child.Header == "Unlink"
                ? child with { Icon = MaterialIconKind.Close }
                : child with { Icon = MaterialIconKind.FlagOutline, AccentIcon = true, IsChecked = false, Value = child.IsChecked ? "Current" : null }).ToList();
        return entry with { Icon = MaterialIconKind.FlagOutline, Children = children, OnGuide = true, Value = null };
    }

    // ---- What the entries do: one action each (one undo step), a toast that counts the tasks, then the selection clears ----

    private async Task MoveCountedAsync(IReadOnlyList<Guid> ids, TaskBin bin, string name)
    {
        if (bin == TaskBin.Session)
        {
            // Through the session's cap; a full session opens "This session is full" (swap, or Add anyway with a reason), unchanged.
            await MoveManyAsync(ids, bin);
            return;
        }

        await BulkAsync(actions => actions.MoveAsync(ids, bin, TaskToasts.MovedCount(ids.Count, name)));
    }

    private Task MarkDoneAsync(IReadOnlyList<Guid> ids) => BulkAsync(actions => actions.FinishAsync(ids, TaskToasts.MarkedDone(ids.Count)));

    private Task LabelCountedAsync(IReadOnlyList<Guid> ids, Guid? labelId, string? fullName)
    {
        return BulkAsync(actions => actions.SetLabelAsync(ids, labelId, TaskToasts.LabelledCount(ids.Count, fullName)));
    }

    private Task ToggleTagCountedAsync(IReadOnlyList<Guid> ids, string tag, bool adding)
    {
        return BulkAsync(actions => actions.ToggleTagAsync(ids, tag, TaskToasts.TagCount(ids.Count, tag, adding)));
    }

    private async Task AddTagCountedAsync(IReadOnlyList<Guid> ids, string typed)
    {
        if (TaskLabels.Clean([typed.Trim().TrimStart('#')]) is not [string tag])
        {
            return;
        }

        await ScopeNewTagAsync(tag, onlyHere: false);
        await BulkAsync(actions => actions.AddTagsAsync(ids, [tag], TaskToasts.TagCount(ids.Count, tag, added: true)));
    }

    private Task TimeLimitCountedAsync(IReadOnlyList<Guid> ids, int? minutes)
    {
        return BulkAsync(actions => actions.SetTimeLimitAsync(ids, minutes, TaskToasts.TimeLimitCount(ids.Count, minutes)));
    }

    private Task LinkCountedAsync(IReadOnlyList<Guid> ids, Guid? goalId, string? goalName)
    {
        return BulkAsync(actions => actions.SetGoalAsync(ids, goalId, goalName, TaskToasts.LinkedCount(ids.Count, goalId is null ? null : goalName ?? string.Empty)));
    }

    /// <summary>Delete (N) from the menu, the select bar or Del: to Archive › Recently deleted at once (30 days), one undo step (8a.75, 8a.77).</summary>
    private Task DeleteCountedAsync(IReadOnlyList<Guid> ids)
    {
        return ids.Count == 0 ? Task.CompletedTask : BulkAsync(actions => actions.DeleteAsync(ids, TaskToasts.DeletedToArchive(ids.Count)));
    }

    // ---- Ctrl+E, Ctrl+A, the column ----

    /// <summary>
    /// Ctrl+E (8a.09): with 2 or more selected, Move and label; with one, the task editor (and the selection clears). False when nothing is
    /// selected on the Tasks page.
    /// </summary>
    public bool EditSelection()
    {
        List<TaskRowKey> keys = _picked.Where(IsShelfRow).ToList();
        if (keys.Count == 0)
        {
            return false;
        }

        EditKeys(keys.Where(key => MenuTask(key) is not null).ToList());
        return true;
    }

    private void EditKeys(IReadOnlyList<TaskRowKey> keys)
    {
        List<Guid> ids = keys.Select(key => key.Id).Distinct().ToList();
        if (ids.Count > 1)
        {
            RequestMoveAndLabel(ids);
            return;
        }

        if (keys.Count == 1)
        {
            TaskRowKey key = keys[0];
            ClearSelection();
            IsDetailOpen = true;
            Select(key);
            ShowEditorWhereOpened();
        }
    }

    private void RequestMoveAndLabel(IReadOnlyList<Guid> ids)
    {
        if (ids.Count > 0)
        {
            MoveAndLabelRequested?.Invoke(this, ids);
        }
    }

    // The selected tasks the task list holds (a running session's own are left out).
    private List<Guid> HeldIds(IReadOnlyList<TaskRowKey> keys)
    {
        return keys.Where(key => MenuTask(key) is not null).Select(key => key.Id).Distinct().ToList();
    }

    /// <summary>Ctrl+A in a Shelf column (8a.04), or the menu's "Select all in …": every task that column shows, and nothing else.</summary>
    internal void SelectColumn(TaskSectionViewModel column)
    {
        _picked.Clear();
        foreach (TaskListRowViewModel row in SelectableRowsOf(column))
        {
            _picked.Add(row.Key);
        }

        if (IsDetailOpen && _picked.Count > 0)
        {
            CloseDetail();
        }

        ShowPicked();
    }

    private static List<TaskListRowViewModel> SelectableRowsOf(TaskSectionViewModel column)
    {
        return column.ShowsRows ? column.Rows.Where(row => row.Kind != TaskRowKind.AutoSaved && row.OwnerKey is null).ToList() : [];
    }

    // The column's name in "Select all in …" (8a.46): the bin, a label column's short name ("No label" for its own), or "the list" in Group by None.
    private string ColumnName(TaskSectionViewModel column)
    {
        if (Shelf.IsGroupedByNone)
        {
            return "the list";
        }

        return column.IsLabelColumn ? column.ShortTitle : column.Title;
    }
}
