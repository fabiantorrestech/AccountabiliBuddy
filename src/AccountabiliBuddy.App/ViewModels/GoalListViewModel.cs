using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Goals list pane (round 2, change note §4a "Goal list", "List view switch"): the fixed This week card, then "Your goals" (folds
/// the list: "8 goals hidden · the This week card still counts them") with the view switch, remembered in "goals.ui":
/// <list type="bullet">
/// <item><b>By area</b> (the default): active goals in area blocks, then "2 paused goals · show them in All" when any are paused.</item>
/// <item><b>Current</b>: active goals only, in one block, no sections.</item>
/// <item><b>All</b>: every goal in one block, the paused ones at the end, faded, with "since &lt;date&gt;" and a Paused pill.</item>
/// </list>
/// Rows are kept by goal and updated in place; blocks are rebuilt only when what they hold changes (a view switch, a fold, a goal
/// added, moved or paused), so a refresh never moves what the pointer is on.
/// </summary>
public sealed partial class GoalListViewModel : ObservableObject
{
    private readonly Dictionary<Guid, GoalRowViewModel> _rows = [];
    private readonly Action<GoalRowViewModel> _select;
    private readonly Func<GoalListView, Task> _setView;
    private readonly Func<Guid, Task> _toggleArea;
    private string _structure = string.Empty;
    private string _chipsKey = string.Empty;
    private Guid? _areaFilter;
    private (IReadOnlyList<GoalFacts> Facts, GoalLists Lists, GoalUiPreferences Ui, DateOnly Today, GoalWeekStart WeekStart)? _last;

    public GoalListViewModel(Action<GoalRowViewModel> select, Func<GoalListView, Task> setView, Func<Guid, Task> toggleArea, Action jumpToBehind)
    {
        _select = select;
        _setView = setView;
        _toggleArea = toggleArea;
        ThisWeek = new GoalThisWeekViewModel(jumpToBehind);
    }

    public GoalThisWeekViewModel ThisWeek { get; }

    /// <summary>The blocks shown (area blocks, or the one block of Current / All).</summary>
    public ObservableCollection<GoalAreaBlockViewModel> Blocks { get; } = [];

    /// <summary>Every shown row in list order (the collapsed icon column, Tab order, jumps).</summary>
    public ObservableCollection<GoalRowViewModel> ShownRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsByArea), nameof(IsCurrent), nameof(IsAll))]
    public partial GoalListView View { get; set; }

    public bool IsByArea => View == GoalListView.ByArea;

    public bool IsCurrent => View == GoalListView.Current;

    public bool IsAll => View == GoalListView.All;

    /// <summary>The "Your goals" count: active goals (By area, Current) or every goal (All).</summary>
    [ObservableProperty]
    public partial int Count { get; set; }

    /// <summary>"Your goals" is open (closed: the hidden line shows instead; for this visit only).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGoalsClosed), nameof(GoalsCaretAngle), nameof(GoalsToggleTip))]
    public partial bool IsGoalsOpen { get; set; } = true;

    public bool IsGoalsClosed => !IsGoalsOpen;

    public double GoalsCaretAngle => IsGoalsOpen ? 0 : -90;

    public string GoalsToggleTip => IsGoalsOpen ? "Hide your goals" : "Show your goals";

    /// <summary>"8 goals hidden · the This week card still counts them".</summary>
    [ObservableProperty]
    public partial string HiddenText { get; set; } = string.Empty;

    /// <summary>"2 paused goals · show them in All" (By area only, when any goal is paused).</summary>
    [ObservableProperty]
    public partial string? PausedLinkText { get; set; }

    public bool HasPausedLink => PausedLinkText is not null;

    /// <summary>The list is collapsed to its icons (a small window; set by the page's layout).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFull))]
    public partial bool IsCompact { get; set; }

    public bool IsFull => !IsCompact;

    /// <summary>The rows by goal (kept between refreshes).</summary>
    public IReadOnlyDictionary<Guid, GoalRowViewModel> Rows => _rows;

    /// <summary>
    /// Takes the goals' facts for today, the lists (areas, order, pauses) and the page's UI state, and updates the rows in place,
    /// rebuilding the blocks only when their content changed.
    /// </summary>
    /// <summary>The area chips under the page header: All, then each area that has a goal (board Goals-PC).</summary>
    public ObservableCollection<GoalAreaChipViewModel> AreaChips { get; } = [];

    /// <summary>The area the chips row shows alone (this visit only); null = All.</summary>
    public Guid? AreaFilter => _areaFilter;

    /// <summary>Picks an area chip (null = All) and redraws the list.</summary>
    public void PickArea(Guid? areaId)
    {
        _areaFilter = areaId;
        if (_last is { } last)
        {
            Update(last.Facts, last.Lists, last.Ui, last.Today, last.WeekStart);
        }
    }

    public void Update(IReadOnlyList<GoalFacts> facts, GoalLists lists, GoalUiPreferences ui, DateOnly today, GoalWeekStart weekStart)
    {
        _last = (facts, lists, ui, today, weekStart);
        View = ui.ListView;
        Dictionary<Guid, GoalFacts> byId = facts.ToDictionary(item => item.Goal.Id);
        foreach (Guid gone in _rows.Keys.Where(id => !byId.ContainsKey(id)).ToList())
        {
            _rows.Remove(gone);
        }

        foreach (GoalFacts item in facts)
        {
            if (!_rows.TryGetValue(item.Goal.Id, out GoalRowViewModel? row))
            {
                row = new GoalRowViewModel(item.Goal.Id, _select);
                _rows[item.Goal.Id] = row;
            }

            row.Update(item);
        }

        ThisWeek.Update(facts, today, weekStart);
        // The area chips (board Goals-PC): an area that lost its last goal, or was deleted, falls back to All.
        List<GoalArea> chipAreas = lists.LiveAreas.Where(area => facts.Any(item => item.Goal.AreaId == area.Id)).ToList();
        if (_areaFilter is Guid picked && chipAreas.All(area => area.Id != picked))
        {
            _areaFilter = null;
        }

        string chipsKey = string.Join("|", chipAreas.Select(area => $"{area.Id}:{area.Name}:{area.Icon}")) + "@" + _areaFilter;
        if (chipsKey != _chipsKey)
        {
            _chipsKey = chipsKey;
            AreaChips.Clear();
            AreaChips.Add(new GoalAreaChipViewModel(null, "All", null, _areaFilter is null, new RelayCommand(() => PickArea(null))));
            foreach (GoalArea area in chipAreas)
            {
                Guid areaId = area.Id;
                AreaChips.Add(new GoalAreaChipViewModel(areaId, area.Name, GoalIcons.Of(area.Icon), _areaFilter == areaId, new RelayCommand(() => PickArea(areaId))));
            }
        }

        // Options › List › Sort "Behind first": the goals behind this period lead their group; the rest keep your order.
        IEnumerable<GoalFacts> ordered = ui.Sort == GoalListSort.BehindFirst
            ? facts.OrderBy(item => item.Status == GoalPeriodStatus.Behind ? 0 : 1)
            : facts;
        List<GoalFacts> inArea = ordered.Where(item => _areaFilter is null || item.Goal.AreaId == _areaFilter).ToList();
        List<GoalFacts> active = inArea.Where(item => !item.IsPaused).ToList();
        List<GoalFacts> paused = inArea.Where(item => item.IsPaused).ToList();
        HashSet<Guid> collapsed = (ui.CollapsedAreas ?? []).ToHashSet();
        var blocks = new List<(Guid? AreaId, string Title, string Icon, bool Folded, List<Guid> Ids)>();
        switch (View)
        {
            case GoalListView.Current:
                blocks.Add((null, "Current", string.Empty, false, active.Select(item => item.Goal.Id).ToList()));
                break;
            case GoalListView.All:
                blocks.Add((null, "All", string.Empty, false, [.. active.Select(item => item.Goal.Id), .. paused.Select(item => item.Goal.Id)]));
                break;
            default:
                foreach (GoalArea area in lists.LiveAreas)
                {
                    List<Guid> ids = active.Where(item => item.Goal.AreaId == area.Id).Select(item => item.Goal.Id).ToList();
                    if (ids.Count > 0)
                    {
                        blocks.Add((area.Id, area.Name, area.Icon, collapsed.Contains(area.Id), ids));
                    }
                }

                break;
        }

        Count = View == GoalListView.All ? inArea.Count : active.Count;
        HiddenText = $"{GoalTexts.Plural(Count, "goal")} hidden · the This week card still counts them";
        PausedLinkText = View == GoalListView.ByArea && paused.Count > 0 ? $"{GoalTexts.Plural(paused.Count, "paused goal")} · show them in All" : null;
        OnPropertyChanged(nameof(HasPausedLink));

        string structure = string.Join("|", blocks.Select(block => $"{block.AreaId}:{block.Title}:{block.Folded}:{string.Join(",", block.Ids)}"));
        if (structure == _structure)
        {
            return;
        }

        _structure = structure;
        Blocks.Clear();
        ShownRows.Clear();
        foreach ((Guid? areaId, string title, string icon, bool folded, List<Guid> ids) in blocks)
        {
            var block = new GoalAreaBlockViewModel(areaId, title, GoalIcons.Of(icon), ToggleArea, EditArea) { IsFolded = folded };
            foreach (Guid id in ids)
            {
                block.Rows.Add(_rows[id]);
                ShownRows.Add(_rows[id]);
            }

            block.RowsChanged();
            Blocks.Add(block);
        }
    }

    /// <summary>R5b hook: ✎ on an area header opens Edit area.</summary>
    public event EventHandler<Guid>? EditAreaRequested;

    [RelayCommand]
    private Task SetView(GoalListView view)
    {
        return _setView(view);
    }

    [RelayCommand]
    private Task ShowAll()
    {
        return _setView(GoalListView.All);
    }

    [RelayCommand]
    private void ToggleGoals()
    {
        IsGoalsOpen = !IsGoalsOpen;
    }

    private void ToggleArea(GoalAreaBlockViewModel block)
    {
        if (block.AreaId is Guid id)
        {
            _ = _toggleArea(id);
        }
    }

    private void EditArea(GoalAreaBlockViewModel block)
    {
        if (block.AreaId is Guid id)
        {
            EditAreaRequested?.Invoke(this, id);
        }
    }
}
