using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Tasks page's filter button: four sections (Status, Category, Due, Sort) shown as pills in a flyout, with a count of the
/// sections that are not at their default. The choices are remembered on this PC ("tasks.view", never synced). The top category
/// chips are a one-tap shortcut for "exactly this one category": they read and set the same state. Nothing here is stored on a task.
/// </summary>
public sealed partial class TaskFilterViewModel : ObservableObject
{
    private static readonly TaskStatusKind[] AllStatuses = Enum.GetValues<TaskStatusKind>();

    private readonly TaskViewStore _store;
    private readonly HashSet<TaskStatusKind> _statuses = [.. AllStatuses];
    private readonly HashSet<Guid> _categoryIds = [];
    private bool _includesNoCategory;
    private IReadOnlyList<TaskCategory> _categories = [];
    private string _categorySignature = "\0";

    public TaskFilterViewModel(TaskViewStore store)
    {
        _store = store;
        Load(store.Load());
        BuildPills();
        ClearCommand = new RelayCommand(Clear);
    }

    /// <summary>Something changed (a pill was picked or Clear pressed): the list should be built again.</summary>
    public event EventHandler? Changed;

    public RelayCommand ClearCommand { get; }

    public TaskDueFilter Due { get; private set; }

    public TaskSortMode Sort { get; private set; }

    /// <summary>The Status section's pills (Come back, Later, Unfinished, Auto-saved, Done); on = shown.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TaskChipViewModel> StatusPills { get; set; } = [];

    /// <summary>The Category section's pills: each category with its colour dot, then "No category".</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TaskChipViewModel> CategoryPills { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<TaskChipViewModel> DuePills { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<TaskChipViewModel> SortPills { get; set; } = [];

    /// <summary>How many of the four sections are not at their default.</summary>
    public int ActiveCount =>
        (_statuses.Count < AllStatuses.Length ? 1 : 0) + (HasCategoryRestriction ? 1 : 0) + (Due != TaskDueFilter.Any ? 1 : 0)
        + (Sort != TaskSortMode.Default ? 1 : 0);

    public bool HasActive => ActiveCount > 0;

    public string BadgeText => ActiveCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Some categories are chosen (or "No category"): only those rows show. With no categories at all there is nothing to restrict
    /// to (and the saved choice is kept untouched, in case the categories simply were not loaded yet).
    /// </summary>
    public bool HasCategoryRestriction => _categories.Count > 0 && (_categoryIds.Count > 0 || _includesNoCategory);

    /// <summary>Rows without a category show: no category restriction, or "No category" is on.</summary>
    public bool ShowsNoCategory => !HasCategoryRestriction || _includesNoCategory;

    /// <summary>The one chosen category (what a new task goes into), or null when none or several are chosen.</summary>
    public Guid? SingleCategoryId => _categoryIds.Count == 1 && !_includesNoCategory ? _categoryIds.First() : null;

    public bool ShowsStatus(TaskStatusKind status) => _statuses.Contains(status);

    public bool ShowsCategory(Guid id) => !HasCategoryRestriction || _categoryIds.Contains(id);

    /// <summary>
    /// The categories that exist now: a chosen category that is truly gone is dropped (and the change kept). An empty list drops
    /// nothing: it may be that the categories failed to load, and the saved choice should not be lost for that.
    /// </summary>
    public void SetCategories(IReadOnlyList<TaskCategory> categories)
    {
        _categories = categories;
        bool dropped = false;
        if (categories.Count > 0)
        {
            HashSet<Guid> known = categories.Select(category => category.Id).ToHashSet();
            dropped = _categoryIds.RemoveWhere(id => !known.Contains(id)) > 0;
        }

        BuildPills();
        if (dropped)
        {
            Save();
            RaiseSummary();
        }
    }

    /// <summary>The top chips: exactly one category, or null for "All" (no category restriction).</summary>
    public void ShowOnlyCategory(Guid? id)
    {
        _categoryIds.Clear();
        _includesNoCategory = false;
        if (id is Guid chosen)
        {
            _categoryIds.Add(chosen);
        }

        Apply();
    }

    /// <summary>True when the chosen categories are exactly this one (the top chip reads as selected).</summary>
    public bool IsOnlyCategory(Guid id) => SingleCategoryId == id;

    private void ToggleStatus(TaskStatusKind status)
    {
        if (_statuses.Contains(status))
        {
            // At least one kind of row stays on, or the list would be empty with no hint why.
            if (_statuses.Count > 1)
            {
                _statuses.Remove(status);
            }
        }
        else
        {
            _statuses.Add(status);
        }

        Apply();
    }

    private void ToggleCategory(Guid id)
    {
        if (!_categoryIds.Remove(id))
        {
            _categoryIds.Add(id);
        }

        Apply();
    }

    private void ToggleNoCategory()
    {
        _includesNoCategory = !_includesNoCategory;
        Apply();
    }

    private void Clear()
    {
        _statuses.Clear();
        _statuses.UnionWith(AllStatuses);
        _categoryIds.Clear();
        _includesNoCategory = false;
        Due = TaskDueFilter.Any;
        Sort = TaskSortMode.Default;
        Apply();
    }

    private void Apply()
    {
        BuildPills();
        RaiseSummary();
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(HasActive));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(HasCategoryRestriction));
    }

    // The pills are made once (the categories' pills again only when the categories change) and then updated in place: a click or a
    // store change must not replace the button under the pointer or the keyboard focus.
    private void BuildPills()
    {
        if (StatusPills.Count == 0)
        {
            StatusPills = AllStatuses
                .Select(status => new TaskChipViewModel(StatusLabel(status), false, () => Run(() => ToggleStatus(status))))
                .ToList();
            DuePills = Enum.GetValues<TaskDueFilter>()
                .Select(due => new TaskChipViewModel(DueLabel(due), false, () => Run(() => { Due = due; Apply(); })))
                .ToList();
            SortPills = Enum.GetValues<TaskSortMode>()
                .Select(sort => new TaskChipViewModel(SortLabel(sort), false, () => Run(() => { Sort = sort; Apply(); })))
                .ToList();
        }

        string signature = string.Join('|', _categories.Select(category => $"{category.Id}:{category.Name}:{category.Color}"));
        if (signature != _categorySignature)
        {
            _categorySignature = signature;
            var categoryPills = new List<TaskChipViewModel>();
            foreach (TaskCategory category in _categories)
            {
                Guid id = category.Id;
                categoryPills.Add(new TaskChipViewModel(
                    category.Name, false, () => Run(() => ToggleCategory(id)), TaskCategoryBrushes.Of(category.Color)));
            }

            categoryPills.Add(new TaskChipViewModel("No category", false, () => Run(ToggleNoCategory)));
            CategoryPills = categoryPills;
        }

        UpdateSelection();
    }

    private void UpdateSelection()
    {
        for (int index = 0; index < AllStatuses.Length; index++)
        {
            StatusPills[index].IsSelected = _statuses.Contains(AllStatuses[index]);
        }

        for (int index = 0; index < _categories.Count; index++)
        {
            CategoryPills[index].IsSelected = _categoryIds.Contains(_categories[index].Id);
        }

        CategoryPills[^1].IsSelected = _includesNoCategory;
        TaskDueFilter[] dues = Enum.GetValues<TaskDueFilter>();
        for (int index = 0; index < dues.Length; index++)
        {
            DuePills[index].IsSelected = Due == dues[index];
        }

        TaskSortMode[] sorts = Enum.GetValues<TaskSortMode>();
        for (int index = 0; index < sorts.Length; index++)
        {
            SortPills[index].IsSelected = Sort == sorts[index];
        }
    }

    private static Task Run(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private void Load(TaskViewPreferences? saved)
    {
        if (saved is null)
        {
            return;
        }

        // A partial or hand-edited document has null lists and odd names: anything unusable falls back to the default.
        var statuses = (saved.Statuses ?? []).Select(name => name is not null && Enum.TryParse(name, out TaskStatusKind kind) && Enum.IsDefined(kind) ? kind : (TaskStatusKind?)null)
            .OfType<TaskStatusKind>().ToList();
        if (statuses.Count > 0)
        {
            _statuses.Clear();
            _statuses.UnionWith(statuses);
        }

        // Category ids that no longer exist are dropped by SetCategories once the categories are known.
        _categoryIds.UnionWith(saved.CategoryIds ?? []);
        _includesNoCategory = saved.NoCategory;
        Due = saved.Due is not null && Enum.TryParse(saved.Due, out TaskDueFilter due) && Enum.IsDefined(due) ? due : TaskDueFilter.Any;
        Sort = saved.Sort is not null && Enum.TryParse(saved.Sort, out TaskSortMode sort) && Enum.IsDefined(sort) ? sort : TaskSortMode.Default;
    }

    private void Save()
    {
        // Every status on is saved as the empty list, the same as "never chosen".
        var statuses = _statuses.Count == AllStatuses.Length ? [] : _statuses.Select(status => status.ToString()).ToList();
        _ = _store.SaveAsync(new TaskViewPreferences(statuses, _categoryIds.ToList(), _includesNoCategory, Due.ToString(), Sort.ToString()));
    }

    private static string StatusLabel(TaskStatusKind status) => status switch
    {
        TaskStatusKind.ComeBack => "Come back",
        TaskStatusKind.Later => "Later",
        TaskStatusKind.Unfinished => "Unfinished",
        TaskStatusKind.AutoSaved => "Auto-saved",
        _ => "Done",
    };

    private static string DueLabel(TaskDueFilter due) => due switch
    {
        TaskDueFilter.Any => "Any",
        TaskDueFilter.Overdue => "Overdue",
        TaskDueFilter.Today => "Today",
        TaskDueFilter.ThisWeek => "This week",
        _ => "No date",
    };

    private static string SortLabel(TaskSortMode sort) => sort switch
    {
        TaskSortMode.Default => "Default",
        TaskSortMode.DueAscending => "Due date ↑",
        TaskSortMode.DueDescending => "Due date ↓",
        TaskSortMode.MostRecent => "Most recent",
        _ => "Oldest",
    };
}
