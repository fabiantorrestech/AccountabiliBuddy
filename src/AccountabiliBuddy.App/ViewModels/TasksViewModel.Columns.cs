using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

// Round 2 R3 (plan R3.1-R3.3, change note §2): the Tasks page's Today strip (a drop target into Today › Queued, Open Today) and the
// order of the Shelf's columns, which is this PC's own (the "tasks.layout" document, per grouping; never in a view or a sync file,
// plan §3b item 3). Nothing here reaches This session: the strip moves to Queued only, so the session's action cap is never in play.
public sealed partial class TasksViewModel
{
    // The layout's column orders as last applied (a new value means a column moved).
    private IReadOnlyDictionary<string, IReadOnlyList<string>>? _columnOrder;

    // 0.1.154: the folded columns as last applied (a fold moves a Bins column to the rails at the right).
    private IReadOnlyList<string>? _foldedColumns;

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

    // Ctrl+Alt+←/→ on a column's header: one place left or right, stored for the grouping on this PC (0.1.154: Group by Labels too, never
    // a synced label edit; that reverses 4b.36). An open column passes the open ones and a rail the rails, so every press shows: the rails
    // are drawn at the right (Bins) or at their page's right (Labels) whatever their stored place.
    private Task MoveColumnAsync(string key, int step)
    {
        bool rail = Shelf.Panel.Sections.FirstOrDefault(section => section.Key == key)?.IsRail ?? false;
        List<string> peers = ShownColumnKeys()
            .Where(shown => (Shelf.Panel.Sections.FirstOrDefault(section => section.Key == shown)?.IsRail ?? false) == rail)
            .ToList();
        return StoreColumnOrder(key, stored => TaskColumnOrder.MovedBy(peers, stored, key, step));
    }

    // A column's grip let go over another column: it takes that column's place.
    private Task MoveColumnOntoAsync(string key, string targetKey)
    {
        return StoreColumnOrder(key, stored => TaskColumnOrder.MovedOnto(ShownColumnKeys(), stored, key, targetKey));
    }

    // Group by Bins: the column is drawn as a rail (folded on this PC, or Later while it has no task at all).
    private bool IsBinRail(string key, TaskBin? bin, int rowCount)
    {
        return _layout.Current.IsColumnFolded(key) || (bin == TaskBin.Later && _laterTotal == 0 && rowCount == 0);
    }

    // ---- Tasks redesign 4c: the Shelf's columns as each grouping draws them ----

    // How many Later tasks there are before any search or filter: none makes Later the empty drop rail.
    private int _laterTotal;

    // A Shelf row's sub-line and "Add to Today →" for the grouping in force.
    private readonly record struct ShelfLine(string BinName, MaterialIconKind BinIcon, bool HidesLabel, TaskRowAction? AddToToday);

    // Next first, To sort, Later, anything else after.
    private static int BinPlace(TaskBin? bin) => bin switch
    {
        TaskBin.Next => 0,
        TaskBin.Sort => 1,
        TaskBin.Later => 2,
        _ => 3,
    };

    // Group by Bins: a Next row gets "Add to Today →" (its ← quick action, the same move to Today › Queued). Labels: the bin replaces the
    // label on the sub-line. None: the bin follows the label.
    private ShelfLine ShelfLineOf(TaskBin? shelfBin, TaskRowAction? queueForToday)
    {
        if (shelfBin is not TaskBin bin)
        {
            return new ShelfLine(string.Empty, MaterialIconKind.Tray, false, null);
        }

        if (Shelf.IsGroupedByLabels || Shelf.IsGroupedByNone)
        {
            return new ShelfLine(TaskBins.Name(bin), TaskBinIcons.Redesigned(bin), Shelf.IsGroupedByLabels, null);
        }

        return new ShelfLine(string.Empty, MaterialIconKind.Tray, false, bin == TaskBin.Next ? queueForToday : null);
    }

    // A tag clicked under a task: the page searches it (4c.33); the +N menu closes, the task doesn't open.
    private void SearchForTag(string tag)
    {
        Shelf.IsLabelsMenuOpen = false;
        Shelf.SearchText = "#" + tag;
    }

    // Tasks redesign 7.37: when the search has words and nothing on the Shelf matches, the first column (not a rail) shows the dashed
    // "Search the Archive for “…”" box; the others keep "No matches here.".
    private void OfferArchiveSearch(IReadOnlyList<TaskSectionViewModel> sections)
    {
        bool noneMatch = Shelf.HasViews && Shelf.IsSearching && sections.All(section => section.Rows.Count == 0);
        TaskSectionViewModel? first = noneMatch ? sections.FirstOrDefault(section => !section.IsRail) ?? sections.FirstOrDefault() : null;
        foreach (TaskSectionViewModel section in sections)
        {
            section.HandOffCommand = SearchArchiveCommand;
            section.HandOffWords = ReferenceEquals(section, first) ? Shelf.Search : string.Empty;
        }
    }

    /// <summary>
    /// "Search the Archive for “…”" (7.40): the Archive sheet opens with the same words and filters (Bins dropped; When now reads the
    /// finished or deleted time) and shows "Results for “…”".
    /// </summary>
    [RelayCommand]
    private void SearchArchive()
    {
        Shelf.IsSearchFiltersOpen = false;
        Archive.SearchFor(Shelf.Search, Shelf.SearchFilters);
        IsArchiveOpen = true;
    }

    // Group by Labels: one column per label in the +N menu's order (an empty label too), then No label, each with the rows the view found.
    private static List<(string Key, string Title, TaskBin? Bin, Guid? LabelId, List<TaskListRowViewModel> Rows)> LabelGroups(
        IReadOnlyList<LabelColumnSpec> columns, List<(string Key, string Title, TaskBin? Bin, Guid? LabelId, List<TaskListRowViewModel> Rows)> found)
    {
        var byKey = found.ToDictionary(group => group.Key, StringComparer.Ordinal);
        var groups = columns
            .Select(column => (column.Key, column.Name, (TaskBin?)null, column.LabelId, byKey.TryGetValue(column.Key, out var group) ? group.Rows : []))
            .ToList();
        HashSet<string> placed = columns.Select(column => column.Key).ToHashSet(StringComparer.Ordinal);
        groups.AddRange(found.Where(group => !placed.Contains(group.Key)));
        return groups;
    }

    // What an empty column says: a search's "No matches here.", a label column's drop hint, the label row's "Nothing with these labels here.".
    private string EmptyTextOf(string key, IReadOnlyDictionary<string, LabelColumnSpec> labelColumns)
    {
        if (Shelf.IsSearching)
        {
            return "No matches here.";
        }

        if (labelColumns.TryGetValue(key, out LabelColumnSpec? column))
        {
            return "No tasks · drop here to label them " + column.ShortName;
        }

        return Shelf.FiltersByLabel ? "Nothing with these labels here." : TaskSectionViewModel.DefaultEmptyText;
    }

    // Each grouping's column: Bins fold (kept on this PC) and share the width (Next and To sort by the split, Later 0.8; Later a rail while
    // it has no task at all); a label column has its dot and short name and folds too (0.1.154, a header click or Options › Layout; its own key, so the
    // Labels view's folds are apart from the Bins view's); None's one list is "All tasks · Newest first".
    private void ShapeColumn(TaskSectionViewModel section, TaskBin? bin, int rowCount, LabelColumnSpec? labelColumn)
    {
        bool byBins = !Shelf.HasViews || Shelf.IsGroupedByBins;
        section.CanFold = byBins || labelColumn is not null;
        section.StoresFold = byBins || labelColumn is not null;
        section.IsLabelColumn = labelColumn is not null;
        section.ShortTitle = labelColumn?.ShortName ?? section.Title;
        section.HeaderNote = string.Empty;
        section.ColumnWeight = bin == TaskBin.Later ? 0.8 : 1;
        section.SplitRole = !byBins ? string.Empty : bin switch
        {
            TaskBin.Next => "lead",
            TaskBin.Sort => "trail",
            _ => string.Empty,
        };
        section.IsEmptyRail = byBins && bin == TaskBin.Later && _laterTotal == 0 && rowCount == 0;
        if (labelColumn is not null)
        {
            section.Dot = labelColumn.Dot;
            // No label's rail shows its band's icon (a label's rail shows its dot).
            section.Icon = labelColumn.Dot is null ? labelColumn.BandIcon : null;
        }

        if (Shelf.HasViews && Shelf.IsGroupedByNone)
        {
            section.Title = "All tasks";
            section.Icon = MaterialIconKind.FormatListBulleted;
            section.HeaderNote = "Newest first";
        }
    }

    private Task StoreColumnOrder(string key, Func<IReadOnlyList<string>?, IReadOnlyList<string>?> move)
    {
        string? grouping = Shelf.HasViews ? TaskColumnOrder.GroupingOf(Shelf.Grouping) : Core.Domain.TaskViewConfig.GroupBins;
        if (grouping is null)
        {
            return Task.CompletedTask;
        }

        IReadOnlyList<string>? order = move(_layout.Current.ColumnOrderOf(grouping));
        if (order is null)
        {
            return Task.CompletedTask;
        }

        // PG1: a column moved across a page edge takes its page with it (the next rebuild turns to the page that holds it).
        Shelf.Pager.Reveal(key);

        return _layout.SetColumnOrderAsync(grouping, order);
    }

    // The columns shown, in the order last stored: a second Ctrl+Alt+←/→ before the page has been rebuilt builds on the first.
    private List<string> ShownColumnKeys()
    {
        string? grouping = Shelf.HasViews ? TaskColumnOrder.GroupingOf(Shelf.Grouping) : Core.Domain.TaskViewConfig.GroupBins;
        List<string> shown = Shelf.Panel.Sections.Select(section => section.Key).ToList();
        return grouping is null ? shown : TaskColumnOrder.Apply(shown, key => key, _layout.Current.ColumnOrderOf(grouping));
    }
}
