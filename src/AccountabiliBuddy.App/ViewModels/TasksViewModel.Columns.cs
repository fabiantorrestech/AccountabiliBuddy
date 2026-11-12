using AccountabiliBuddy.App.Services.Tasks;

namespace AccountabiliBuddy.App.ViewModels;

// Round 2 R3 (plan R3.1-R3.3, change note §2): the Tasks page's Today strip (a drop target into Today › Queued, Open Today) and the
// order of the Shelf's columns, which is this PC's own (the "tasks.layout" document, per grouping; never in a view or a sync file,
// plan §3b item 3). Nothing here reaches This session: the strip moves to Queued only, so the session's action cap is never in play.
public sealed partial class TasksViewModel
{
    // The layout's column orders as last applied (a new value means a column moved).
    private IReadOnlyDictionary<string, IReadOnlyList<string>>? _columnOrder;

    /// <summary>The Today strip under the page header.</summary>
    public TodayStripViewModel TodayStrip { get; }

    /// <summary>Open Today (the strip): the window shows the Today tab.</summary>
    public event EventHandler? OpenTodayRequested;

    // A Shelf row dropped on the strip (or its ←): to Today › Queued with Undo in the toast; anything else stays where it is.
    private async Task DropOnTodayAsync(TaskListRowViewModel row)
    {
        Notice = string.Empty;
        if (_actions is null || _store is null)
        {
            return;
        }

        if (await TodayStripDrop.MoveToQueuedAsync(_actions, _store, [row.Id]) == 0)
        {
            Notice = "Only a task on the Shelf (To sort, Next or Later) can go to Today from here.";
        }
    }

    // Alt+←/→ on a column's header: one place left or right among the columns shown, stored for the grouping.
    private Task MoveColumnAsync(string key, int step)
    {
        return StoreColumnOrder(stored => TaskColumnOrder.MovedBy(ShownColumnKeys(), stored, key, step));
    }

    // A column's grip let go over another column: it takes that column's place.
    private Task MoveColumnOntoAsync(string key, string targetKey)
    {
        return StoreColumnOrder(stored => TaskColumnOrder.MovedOnto(ShownColumnKeys(), stored, key, targetKey));
    }

    private Task StoreColumnOrder(Func<IReadOnlyList<string>?, IReadOnlyList<string>?> move)
    {
        string? grouping = Shelf.HasViews ? TaskColumnOrder.GroupingOf(Shelf.Grouping) : Core.Domain.TaskViewConfig.GroupBins;
        if (grouping is null)
        {
            return Task.CompletedTask;
        }

        IReadOnlyList<string>? order = move(_layout.Current.ColumnOrderOf(grouping));
        return order is null ? Task.CompletedTask : _layout.SetColumnOrderAsync(grouping, order);
    }

    private List<string> ShownColumnKeys() => Shelf.Panel.Sections.Select(section => section.Key).ToList();
}
