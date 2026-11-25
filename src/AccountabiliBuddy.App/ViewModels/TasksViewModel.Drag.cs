using System.Globalization;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

// Tasks redesign 8b (SPEC "Dragging", "Context-aware drops", "Turning the page while dragging"; boards 14-18): dragging the selection as a
// stack. The pointer work, the hit test, the page-turn hold and the window's resize lock are the view's (Views/TaskStackDrag); this part
// holds what the page shows while dragging (the stack card, the drag bar, the lit targets) and what a drop changes. A drop is one task
// action (one undo step, a toast that counts the tasks) and clears the selection: on a bin only the bin changes, on a label only the label,
// never the tags; the Today pill queues for today (Today › Queued, never This session, so the session's cap is never in play); on its own
// column the stack is reordered there ("Your order" only, never in Group by None). The "choose a bin and label" zone hands the stack to
// Move and label (MoveAndLabelRequested, item 8c) and keeps the selection; Group by None's Archive zone archives it (Archive › Unfinished).
public sealed partial class TasksViewModel
{
    // The stack being dragged, in the page's order (its first task is the top card); empty when no drag is going on.
    private List<TaskRowKey> _stackKeys = [];

    /// <summary>A stack of selected tasks is being dragged on the Tasks page (the bottom row is the drag bar).</summary>
    [ObservableProperty]
    public partial bool IsStackDragging { get; private set; }

    /// <summary>How many tasks the stack holds; 2 or more shows the back cards and the count badge (8b.06, 8b.07).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StackHasMany), nameof(StackCountText))]
    public partial int StackCount { get; private set; }

    public bool StackHasMany => StackCount > 1;

    public string StackCountText => StackCount.ToString(CultureInfo.CurrentCulture);

    /// <summary>The top card's title: the stack's first task (8b.05).</summary>
    [ObservableProperty]
    public partial string StackTitle { get; private set; } = string.Empty;

    /// <summary>The top card's sub-line: the first task's label ("No label" when it has none).</summary>
    [ObservableProperty]
    public partial string StackLabelName { get; private set; } = string.Empty;

    /// <summary>The first task's label colour, for the 7 px dot; null when it has no label (the dot is then Muted2).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StackHasLabelDot))]
    public partial IBrush? StackLabelDot { get; private set; }

    public bool StackHasLabelDot => StackLabelDot is not null;

    /// <summary>The stack is over a drop target: it shrinks to 92% (8b.08); over "choose a bin and label" it stays full size.</summary>
    [ObservableProperty]
    public partial bool IsStackOverTarget { get; private set; }

    /// <summary>Group by None: the drag bar's left zone archives the stack instead of choosing a bin and label (8b.18).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChooseZoneText), nameof(ChooseZoneIcon))]
    public partial bool ChoosesArchive { get; private set; }

    /// <summary>The stack is over the drag bar's left zone (8b.13).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChooseZoneText))]
    public partial bool IsChooseZoneHot { get; private set; }

    /// <summary>The stack is over the drag bar's Cancel zone (8b.15).</summary>
    [ObservableProperty]
    public partial bool IsCancelZoneHot { get; private set; }

    /// <summary>"Drop here to choose a bin and label" / "Let go to choose a bin and label"; in Group by None "… archive them" (8b.12, 8b.13, 8b.18).</summary>
    public string ChooseZoneText => ChoosesArchive
        ? IsChooseZoneHot ? "Let go to archive them" : "Drop here to archive them"
        : IsChooseZoneHot ? "Let go to choose a bin and label" : "Drop here to choose a bin and label";

    public MaterialIconKind ChooseZoneIcon => ChoosesArchive ? MaterialIconKind.ArchiveOutline : MaterialIconKind.ViewGridOutline;

    /// <summary>The ids of the stack being dragged, in the page's order.</summary>
    internal IReadOnlyList<Guid> StackIds => _stackKeys.Select(key => key.Id).Distinct().ToList();

    /// <summary>
    /// A Shelf row moved 6 px with the left button down (8b.01, 8b.02): a selected row starts a drag of the whole selection, an unselected one
    /// selects just itself first. The rows dim, the drop targets light, the toast goes (8b.10) and the page count freezes (8b.45). False when
    /// the row can't be dragged this way (a step's row, Auto-saved, a running session's own task, not a Shelf row).
    /// </summary>
    internal bool BeginStackDrag(TaskListRowViewModel row)
    {
        if (IsStackDragging || row.OwnerKey is not null || row.Kind == TaskRowKind.AutoSaved || !IsShelfRow(row.Key) || MenuTask(row.Key) is null)
        {
            return false;
        }

        if (!_picked.Contains(row.Key))
        {
            _picked.Clear();
            _picked.Add(row.Key);
            Anchor(Shelf.Panel, row);
            if (IsDetailOpen)
            {
                CloseDetail();
            }

            ShowPicked();
        }

        List<TaskListRowViewModel> order = Shelf.Panel.Sections.SelectMany(section => section.Rows).ToList();
        var rank = new Dictionary<TaskRowKey, int>();
        for (int index = 0; index < order.Count; index++)
        {
            rank.TryAdd(order[index].Key, index);
        }

        List<TaskRowKey> keys = _picked.Where(key => MenuTask(key) is not null)
            .OrderBy(key => rank.TryGetValue(key, out int at) ? at : int.MaxValue)
            .ToList();
        if (keys.Count == 0)
        {
            return false;
        }

        _stackKeys = keys;
        TaskListRowViewModel top = order.FirstOrDefault(candidate => candidate.Key == keys[0]) ?? row;
        StackTitle = top.Title;
        StackLabelName = top.HasCategory ? top.CategoryName! : "No label";
        StackLabelDot = top.HasCategory ? top.CategoryBrush : null;
        StackCount = keys.Select(key => key.Id).Distinct().Count();
        ChoosesArchive = Shelf.IsGroupedByNone;
        IsStackOverTarget = false;
        IsChooseZoneHot = IsCancelZoneHot = false;

        var stack = keys.ToHashSet();
        foreach (TaskListRowViewModel shelfRow in order)
        {
            shelfRow.IsDragSource = stack.Contains(shelfRow.Key);
        }

        foreach (TaskSectionViewModel column in Shelf.Panel.Sections)
        {
            column.IsStackLit = TakesStack(column);
            column.IsStackHot = false;
        }

        Toasts.DismissCommand.Execute(null);
        Notice = string.Empty;
        Shelf.Pager.IsFrozen = true;
        Shelf.IsStackDragging = true;
        TodayStrip.IsStackDrag = true;
        TodayStrip.IsLit = true;
        IsStackDragging = true;
        return true;
    }

    /// <summary>The drag is over: everything it lit goes back, the page count follows the width again. The selection stays.</summary>
    internal void EndStackDrag()
    {
        if (!IsStackDragging)
        {
            return;
        }

        foreach (TaskListRowViewModel row in Shelf.Panel.Sections.SelectMany(section => section.Rows))
        {
            row.IsDragSource = false;
            row.DropMark = 0;
        }

        foreach (TaskSectionViewModel column in Shelf.Panel.Sections)
        {
            column.IsStackLit = false;
            column.IsStackHot = false;
        }

        foreach (LabelFilterChipViewModel chip in Shelf.RowChips)
        {
            chip.IsDropHot = false;
        }

        _stackKeys = [];
        IsStackOverTarget = false;
        IsChooseZoneHot = IsCancelZoneHot = false;
        Shelf.IsNoLabelDropHot = false;
        Shelf.IsMoreDropHot = false;
        Shelf.IsStackDragging = false;
        Shelf.Pager.IsFrozen = false;
        TodayStrip.IsHot = false;
        TodayStrip.IsLit = false;
        TodayStrip.IsStackDrag = false;
        IsStackDragging = false;
    }

    /// <summary>
    /// The stack is over <paramref name="target"/> now (null: nothing that takes it): that place lights (8b.21, 8b.26, 8b.28), the others go
    /// back to lit; on its own column the line shows where the stack would go.
    /// </summary>
    internal void ShowStackTarget(StackDropTarget? target)
    {
        foreach (TaskSectionViewModel column in Shelf.Panel.Sections)
        {
            column.IsStackHot = target is { Kind: StackDropKind.Column } && ReferenceEquals(target.Column, column);
            foreach (TaskListRowViewModel row in column.Rows)
            {
                row.DropMark = target is { Kind: StackDropKind.Column, Row: { } at } && ReferenceEquals(at, row) ? (target.After ? 1 : -1) : 0;
            }
        }

        // The label under the stack lights in its own colour: its chip, its +N row and, in Group by Labels, the hot column's own chip.
        Guid? hotLabel = target switch
        {
            { Kind: StackDropKind.Label, LabelId: Guid id } => id,
            { Kind: StackDropKind.Column, Column: { IsLabelColumn: true } column } when TryLabelOf(column, out Guid? id) => id,
            _ => null,
        };
        foreach (LabelFilterChipViewModel chip in Shelf.RowChips)
        {
            chip.IsDropHot = hotLabel == chip.Id;
        }

        Shelf.IsNoLabelDropHot = target is { Kind: StackDropKind.NoLabel };
        Shelf.IsMoreDropHot = target is { Kind: StackDropKind.More };
        TodayStrip.IsHot = target is { Kind: StackDropKind.Today };
        IsChooseZoneHot = target is { Kind: StackDropKind.Choose };
        IsCancelZoneHot = target is { Kind: StackDropKind.Cancel };
        IsStackOverTarget = target is not null && (target.Kind != StackDropKind.Choose || ChoosesArchive);
    }

    /// <summary>
    /// A Shelf column takes the stack: a bin (or its rail) or a label column some of the stack would change for, or the stack's own column
    /// when it can be reordered there. Group by None's list takes nothing (8b.29).
    /// </summary>
    internal bool TakesStack(TaskSectionViewModel column)
    {
        if (Shelf.IsGroupedByNone || _stackKeys.Count == 0)
        {
            return false;
        }

        List<TaskItem> tasks = StackTasks();
        if (column.IsLabelColumn)
        {
            return TryLabelOf(column, out Guid? labelId) && (!tasks.All(task => HasLabel(task, labelId)) || CanReorderIn(column));
        }

        return column.Bin is TaskBin.Sort or TaskBin.Next or TaskBin.Later && (!tasks.All(task => task.Bin == column.Bin) || CanReorderIn(column));
    }

    /// <summary>A drop on <paramref name="column"/> would reorder the stack there (8b.35): it is already all in that column and can be reordered.</summary>
    internal bool WouldReorderStack(TaskSectionViewModel column)
    {
        if (!CanReorderIn(column) || _stackKeys.Count == 0)
        {
            return false;
        }

        List<TaskItem> tasks = StackTasks();
        return column.IsLabelColumn
            ? TryLabelOf(column, out Guid? labelId) && tasks.All(task => HasLabel(task, labelId))
            : column.Bin is TaskBin bin && tasks.All(task => task.Bin == bin);
    }

    /// <summary>
    /// The stack was let go over <paramref name="target"/> (null: empty space). The drag ends first (the selection stays); then the drop
    /// applies at once as one task action with a counted toast and Undo, and the selection clears (8b.31-8b.37). Cancel, a page arrow or
    /// empty space change nothing (8b.17, 8b.44); "choose a bin and label" opens Move and label with the stack (8b.16).
    /// </summary>
    internal async Task DropStackAsync(StackDropTarget? target)
    {
        List<Guid> ids = StackIds.ToList();
        List<TaskItem> tasks = StackTasks();
        bool takes = target is { Kind: StackDropKind.Column, Column: { } column } && TakesStack(column);
        EndStackDrag();
        if (target is null || ids.Count == 0 || _actions is null)
        {
            return;
        }

        switch (target.Kind)
        {
            case StackDropKind.Column when takes && target.Column is { IsLabelColumn: true } labelColumn && TryLabelOf(labelColumn, out Guid? labelId):
                if (tasks.All(task => HasLabel(task, labelId)))
                {
                    await ReorderStackAsync(ids, labelColumn, target);
                }
                else
                {
                    await LabelStackAsync(tasks, labelId, labelId is Guid id ? Shelf.ChipOf(id)?.Name ?? labelColumn.Title : null);
                }

                break;
            case StackDropKind.Column when takes && target.Column is { Bin: TaskBin bin } binColumn:
                if (tasks.All(task => task.Bin == bin))
                {
                    await ReorderStackAsync(ids, binColumn, target);
                }
                else
                {
                    // Only the tasks not there yet move, and the toast counts those ("Moved 2 tasks to Later").
                    await MoveCountedAsync(tasks.Where(task => task.Bin != bin).Select(task => task.Id).ToList(), bin, TaskBins.Name(bin));
                }

                break;
            case StackDropKind.Label:
                await LabelStackAsync(tasks, target.LabelId, target.LabelName);
                break;
            case StackDropKind.NoLabel:
                await LabelStackAsync(tasks, null, null);
                break;
            case StackDropKind.Today:
                await QueueStackForTodayAsync(ids);
                break;
            case StackDropKind.Choose when ChoosesArchiveFor(target):
                await BulkAsync(actions => actions.ArchiveAsync(ids, TaskToasts.ArchivedCount(ids.Count)));
                break;
            case StackDropKind.Choose:
                RequestMoveAndLabel(ids);
                break;
        }
    }

    // A label drop (8b.32, 8b.36): only the label changes, on the tasks that don't have it yet ("Labelled 2 tasks Work - SDA"); when they all
    // have it nothing changes, and the selection clears as after any drop.
    private Task LabelStackAsync(IReadOnlyList<TaskItem> tasks, Guid? labelId, string? fullName)
    {
        List<Guid> changing = tasks.Where(task => !HasLabel(task, labelId)).Select(task => task.Id).ToList();
        if (changing.Count == 0)
        {
            ClearSelection();
            return Task.CompletedTask;
        }

        return LabelCountedAsync(changing, labelId, fullName);
    }

    // Group by None's left zone is the Archive zone (checked again at the drop: the grouping can't change mid-drag, so this is what showed).
    private bool ChoosesArchiveFor(StackDropTarget target) => target.Kind == StackDropKind.Choose && Shelf.IsGroupedByNone;

    // The Today pill (8b.24): the Shelf tasks go to Today › Queued, "Moved 3 tasks to Today" with Undo; never into This session.
    private Task QueueStackForTodayAsync(IReadOnlyList<Guid> ids)
    {
        if (_store is not { } store)
        {
            return Task.CompletedTask;
        }

        IReadOnlyList<Guid> movable = TodayStripDrop.Movable(store.Lists, ids);
        return movable.Count == 0
            ? Task.CompletedTask
            : BulkAsync(actions => actions.MoveAsync(movable, TaskBin.Queued, TaskToasts.MovedCount(movable.Count, "Today")));
    }

    // A stack let go on its own column (8b.35): it goes before the first task under the drop line that isn't in the stack (the end when
    // there is none), in the stack's order; "Reordered 3 tasks" with Undo.
    private Task ReorderStackAsync(IReadOnlyList<Guid> ids, TaskSectionViewModel column, StackDropTarget target)
    {
        if (!CanReorderIn(column))
        {
            return Task.CompletedTask;
        }

        var stack = ids.ToHashSet();
        List<TaskListRowViewModel> rows = column.Rows.ToList();
        int start = target.Row is { } row && rows.IndexOf(row) is int at and >= 0 ? at + (target.After ? 1 : 0) : rows.Count;
        Guid? beforeId = rows.Skip(start).FirstOrDefault(candidate => !stack.Contains(candidate.Id))?.Id;

        // Let go where the stack already is (in its order, just before that task): nothing to change, no undo step.
        List<Guid> now = rows.Select(row => row.Id).ToList();
        List<Guid> after = now.Where(id => !stack.Contains(id)).ToList();
        int insertAt = beforeId is Guid before ? after.IndexOf(before) : after.Count;
        after.InsertRange(insertAt < 0 ? after.Count : insertAt, ids.Where(now.Contains));
        if (after.SequenceEqual(now))
        {
            ClearSelection();
            return Task.CompletedTask;
        }

        return BulkAsync(actions => actions.ReorderManyAsync(ids, beforeId, TaskToasts.ReorderedCount(ids.Count)));
    }

    // A reorder shows only in "Your order", never in Group by None's newest-first list, and only in an open column (not a rail).
    private bool CanReorderIn(TaskSectionViewModel column)
    {
        return !Shelf.IsGroupedByNone && Shelf.IsYourOrder && column.ShowsColumn;
    }

    private List<TaskItem> StackTasks()
    {
        return StackIds.Select(StoredTask).OfType<TaskItem>().ToList();
    }

    // A task has the label (null: no label, or one that no longer exists).
    private bool HasLabel(TaskItem task, Guid? labelId)
    {
        return labelId is null ? task.LabelId is null || LabelOf(task.LabelId) is null : task.LabelId == labelId;
    }

    /// <summary>The label a Group by Labels column is about ("label:&lt;id&gt;", or "label:none" = null). False for any other column.</summary>
    internal static bool TryLabelOf(TaskSectionViewModel column, out Guid? labelId)
    {
        labelId = null;
        if (!column.IsLabelColumn)
        {
            return false;
        }

        if (column.Key == "label:none")
        {
            return true;
        }

        if (column.Key.StartsWith("label:", StringComparison.Ordinal) && Guid.TryParse(column.Key["label:".Length..], out Guid id))
        {
            labelId = id;
            return true;
        }

        return false;
    }
}
