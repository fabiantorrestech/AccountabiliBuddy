using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Shelf column of the Tasks page (spec 03 §B, shared behaviour §1): its header ("Shelf ▾" with the saved views, "Bins ▾" =
/// Group by, unfold, hide), the search pill, the label chips (+, All, each label, No label), the weekly tidy-up card, then its
/// sections (<see cref="Panel"/>: To sort, Next, Later, or label / tag groups). What it shows comes from the active view
/// (<see cref="TaskViewService"/>); its rows are built by <see cref="TasksViewModel"/>, which owns the lists. Before the task list
/// exists (the old lists still the source) only the search narrows it and grouping stays by bins.
/// </summary>
public sealed partial class ShelfViewModel : ObservableObject
{
    /// <summary>The Shelf's bins, in order.</summary>
    public static readonly IReadOnlyList<TaskBin> Bins = [TaskBin.Sort, TaskBin.Next, TaskBin.Later];

    private readonly Action _hide;
    private readonly Action _toggleToday;
    private readonly Func<bool> _isTodayHidden;
    private TaskViewService? _views;
    private TaskTidyUpService? _tidyUp;
    private TaskStore? _store;

    public ShelfViewModel(TaskSectionsPanelViewModel panel, Action hide, Action toggleToday, Func<bool> isTodayHidden)
    {
        Panel = panel;
        _hide = hide;
        _toggleToday = toggleToday;
        _isTodayHidden = isTodayHidden;
        SearchText = CountText = TidyTitle = TidySub = string.Empty;
        GroupText = "Bins";
    }

    public TaskSectionsPanelViewModel Panel { get; }

    /// <summary>The search or a view changed what the Shelf shows: the page builds it again.</summary>
    public event EventHandler? Changed;

    /// <summary>The task list is there: views, Group by and the label chips work.</summary>
    public bool HasViews => _views is not null;

    [ObservableProperty]
    public partial string SearchText { get; set; }

    /// <summary>The words the Shelf is narrowed to; empty for none.</summary>
    public string Search => SearchText.Trim();

    public bool IsSearching => Search.Length > 0;

    [ObservableProperty]
    public partial IReadOnlyList<TaskChipViewModel> LabelChips { get; set; } = [];

    /// <summary>"Bins", "Label", "Tag" or "None": the Group by button's text.</summary>
    [ObservableProperty]
    public partial string GroupText { get; set; }

    /// <summary>"10 tasks": the rail's line.</summary>
    [ObservableProperty]
    public partial string CountText { get; set; }

    /// <summary>The labels editor (the chip row's "+") is open under the chips.</summary>
    [ObservableProperty]
    public partial bool IsManagingLabels { get; set; }

    [ObservableProperty]
    public partial bool HasTidyCard { get; set; }

    /// <summary>"Weekly tidy-up · 5 untouched for 3+ weeks".</summary>
    [ObservableProperty]
    public partial string TidyTitle { get; set; }

    /// <summary>"About 2 minutes · Mondays".</summary>
    [ObservableProperty]
    public partial string TidySub { get; set; }

    /// <summary>From now on the Shelf follows the task list's views and its weekly tidy-up (ADR 76).</summary>
    public void UseTaskModel(TaskStore store, TaskViewService views, TaskTidyUpService tidyUp)
    {
        _store = store;
        _views = views;
        _tidyUp = tidyUp;
        _views.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _tidyUp.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(HasViews));
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearching));
        if (_views is null)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else if (_views.Search != value)
        {
            _views.SetSearch(value);
        }
    }

    /// <summary>What the active view shows of the Shelf's bins, grouped; null before the task list exists.</summary>
    public TaskViewResult? Query() => _views?.Query(Bins, showEmptyBins: true);

    /// <summary>The header's hide button (and Ctrl+\): the Shelf folds into its rail.</summary>
    [RelayCommand]
    private void Hide() => _hide();

    [RelayCommand]
    private void ToggleManageLabels() => IsManagingLabels = !IsManagingLabels;

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    /// <summary>The tidy-up card's ×: not this week.</summary>
    [RelayCommand]
    private Task DismissTidyAsync() => _tidyUp?.DismissAsync() ?? Task.CompletedTask;

    /// <summary>The tidy-up card's "Tidy up": the step-by-step flow is not built yet (HANDOFF-QUESTIONS 103), so this stays off.</summary>
    [RelayCommand(CanExecute = nameof(CanStartTidy))]
    private void StartTidy()
    {
    }

    private static bool CanStartTidy() => false;

    /// <summary>The header, chips and card from the task list as it is now (the page calls this as it rebuilds).</summary>
    public void Refresh()
    {
        // A view opened with its own search words: the pill shows them.
        if (_views is not null && _views.Search != SearchText)
        {
            SearchText = _views.Search;
        }

        GroupText = (_views?.Working.Group ?? TaskViewConfig.GroupBins) switch
        {
            TaskViewConfig.GroupLabel => "Label",
            TaskViewConfig.GroupTag => "Tag",
            TaskViewConfig.GroupNone => "None",
            _ => "Bins",
        };
        LabelChips = BuildChips();
        TidyUpCard? card = _tidyUp?.Card();
        HasTidyCard = card is not null;
        TidyTitle = card?.Title ?? string.Empty;
        TidySub = card?.Sub ?? string.Empty;
    }

    // "All", each live label (its dot), "No label"; one picked at a time (the View menu, 3.5, does several).
    private List<TaskChipViewModel> BuildChips()
    {
        if (_views is null || _store is null)
        {
            return [];
        }

        TaskViewConfig working = _views.Working;
        bool noRestriction = working.Labels is not { Count: > 0 } && !working.NoLabel;
        var chips = new List<TaskChipViewModel> { new("All", noRestriction, () => PickLabel(null, false)) };
        foreach (TaskLabel label in _store.Lists.Labels.Select(item => item.Label).Where(label => label.IsLive && label.Name.Trim().Length > 0)
                     .OrderBy(label => label.Position))
        {
            Guid id = label.Id;
            bool only = working.Labels is { Count: 1 } labels && labels[0] == id && !working.NoLabel;
            chips.Add(new TaskChipViewModel(label.Name, only, () => PickLabel(id, false), TaskCategoryBrushes.Of(TaskLabelColors.Opaque(label.Color))));
        }

        bool onlyNone = working.NoLabel && working.Labels is not { Count: > 0 };
        chips.Add(new TaskChipViewModel("No label", onlyNone, () => PickLabel(null, true)));
        return chips;
    }

    private Task PickLabel(Guid? id, bool noLabel)
    {
        _views?.Modify(config => config with { Labels = id is Guid one ? [one] : null, NoLabel = noLabel });
        return Task.CompletedTask;
    }

    /// <summary>"Shelf ▾": the saved views (the active one ticked), Group by, and Hide / Show Today. The full View menu is 3.5.</summary>
    public IReadOnlyList<TaskMenuEntry> ViewMenu()
    {
        var entries = new List<TaskMenuEntry>();
        if (_views is not null)
        {
            entries.Add(new TaskMenuEntry("Views", IsHeading: true));
            foreach (TaskView view in _views.Views)
            {
                Guid id = view.Id;
                entries.Add(new TaskMenuEntry(
                    view.Name, new RelayCommand(() => _views.Select(id)), IsChecked: id == _views.ActiveId, Icon: MaterialIconKind.BookmarkOutline));
            }

            entries.Add(TaskMenuEntry.Group("Group by", GroupMenu(), MaterialIconKind.TuneVariant));
            entries.Add(TaskMenuEntry.Separator);
        }

        entries.Add(_isTodayHidden()
            ? TaskMenuEntry.Do("Show Today", _toggleToday, MaterialIconKind.ArrowExpandRight)
            : TaskMenuEntry.Do("Hide Today", _toggleToday, MaterialIconKind.ArrowCollapseLeft));
        return entries;
    }

    /// <summary>"Bins ▾": Group by Bins, Label, Tag or None (the view's working copy; saving it is the View menu's, 3.5).</summary>
    public IReadOnlyList<TaskMenuEntry> GroupMenu()
    {
        if (_views is null)
        {
            return [new TaskMenuEntry("Bins", IsChecked: true)];
        }

        string current = _views.Working.Group;
        return
        [
            GroupEntry("Bins", TaskViewConfig.GroupBins, current),
            GroupEntry("Label", TaskViewConfig.GroupLabel, current),
            GroupEntry("Tag", TaskViewConfig.GroupTag, current),
            GroupEntry("None", TaskViewConfig.GroupNone, current),
        ];
    }

    private TaskMenuEntry GroupEntry(string text, string group, string current)
    {
        return new TaskMenuEntry(text, new RelayCommand(() => _views?.Modify(config => config with { Group = group })), IsChecked: group == current);
    }
}
