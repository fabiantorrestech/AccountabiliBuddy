using System.Globalization;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

// Tasks redesign item 7: the Archive's search words and Search filters (the panel under the Archive's search box; no Bins, the pills do
// that job). When reads the finished time (Done), the time a task was left open or archived (Unfinished) and the deleted time (Recently
// deleted). This PC's search state, held here in memory: never stored, never synced (7.35).
public sealed partial class NotebookViewModel : ISearchFiltersHost
{
    private SearchFiltersViewModel? _filtersPanel;

    /// <summary>The Archive's Search filters panel (built once; its chips are rebuilt each time it opens).</summary>
    public SearchFiltersViewModel FiltersPanel
    {
        get
        {
            if (_filtersPanel is null)
            {
                _filtersPanel = new SearchFiltersViewModel(this, "Archive", showsBins: false);
                _filtersPanel.CloseRequested += (_, _) => IsSearchFiltersOpen = false;
            }

            return _filtersPanel;
        }
    }

    /// <summary>The Archive's Search filters panel is open.</summary>
    [ObservableProperty]
    public partial bool IsSearchFiltersOpen { get; set; }

    /// <summary>A filter narrows the Archive: the filter button shows "on".</summary>
    public bool HasSearchFilters => !SearchFilters.IsDefault;

    /// <inheritdoc />
    public TaskSearchFilters SearchFilters
    {
        get => _searchFilters;
        set
        {
            _searchFilters = value.ForArchive();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSearchFilters));
            Refresh();
        }
    }

    private TaskSearchFilters _searchFilters = TaskSearchFilters.Default;

    string ISearchFiltersHost.SearchWords
    {
        get => Search;
        set => Search = value;
    }

    TaskStore? ISearchFiltersHost.SearchStore => _taskStore;

    bool ISearchFiltersHost.IsLabelInRow(Guid labelId) => false;

    bool ISearchFiltersHost.IsNoLabelInRow => false;

    void ISearchFiltersHost.TakeOffRow(Guid? labelId)
    {
    }

    /// <summary>
    /// "Search the Archive for “…”" (7.40): the Shelf's words and filters (Bins dropped; When now reads the finished or deleted time), the
    /// Everything pill, and the sheet's panel closed.
    /// </summary>
    internal void SearchFor(string words, TaskSearchFilters filters)
    {
        IsSearchFiltersOpen = false;
        Pill = ArchivePill.Everything;
        SearchFilters = filters;
        Search = words;
    }

    /// <summary>The filter button in the Archive's search box: opens or closes the panel.</summary>
    [RelayCommand]
    private void ToggleSearchFilters() => IsSearchFiltersOpen = !IsSearchFiltersOpen;

    partial void OnIsSearchFiltersOpenChanged(bool value)
    {
        if (value)
        {
            FiltersPanel.Rebuild();
        }
    }

    private void ShowFiltersState()
    {
        if (IsSearchFiltersOpen)
        {
            FiltersPanel.ShowState();
        }
    }

    // The test every Archive line passes (the words and the filters); null when nothing narrows.
    private Func<TaskSearchItem, bool>? SearchMatcher(DateTimeOffset now)
    {
        if (!TaskSearch.Narrows(Search, SearchFilters))
        {
            return null;
        }

        TaskStore? store = _taskStore is { IsActive: true } active ? active : null;
        var context = new TaskSearchContext(
            store?.Lists.Labels.Select(item => item.Label).ToList() ?? [],
            store is null ? null : TaskLabelGroupBook.Live(store.Lists.LabelGroups),
            store?.LabelShown,
            store?.TagShown,
            now,
            _time.LocalTimeZone,
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
        return TaskSearch.Matcher(Search, SearchFilters, context);
    }

    // The task list's own copies by id (live tasks, then History entries): an Archive row of the task list searches its note and tags too.
    private Dictionary<Guid, TaskItem> KnownTasks()
    {
        var known = new Dictionary<Guid, TaskItem>();
        TaskLists lists = Lists;
        foreach (var item in lists.Tasks.Where(item => item.Task.IsLive))
        {
            known[item.Id] = item.Task;
        }

        foreach (var item in lists.Log.Where(item => item.Entry.IsLive))
        {
            known.TryAdd(item.Entry.Task.Id, item.Entry.Task);
        }

        return known;
    }

    // An Archive task as the search sees it: its title and steps, the task list's note and tags when it has a copy, its label (a past
    // session's category id read back as its label), and When = its finished time, else the time it was left open.
    private TaskSearchItem SearchItemOf(ArchiveTaskRow row, Dictionary<Guid, TaskItem> known)
    {
        TaskItem? copy = known.GetValueOrDefault(row.Id);
        return new TaskSearchItem(
            row.Title, row.Steps.Select(step => step.Title).ToList(), copy?.Note, copy?.Tags ?? [], copy?.LabelId ?? LabelIdOf(row.CategoryId), null,
            row.DoneAt ?? row.Sort);
    }

    // A Recently deleted line: the task list's copy when there is one, else just its title; When = the time it was deleted.
    private static TaskSearchItem DeletedSearchItem(Guid id, string title, DateTimeOffset deletedAt, Dictionary<Guid, TaskItem> known)
    {
        return known.TryGetValue(id, out TaskItem? copy)
            ? TaskSearchItem.Of(copy) with { Title = title, Bin = null, When = deletedAt }
            : new TaskSearchItem(title, [], null, [], null, null, deletedAt);
    }

    // A past session's category id is either a label's id or the id that label's category had on this PC (TaskStore.CategoryIds).
    private Guid? LabelIdOf(Guid? categoryId)
    {
        if (categoryId is not Guid id || _taskStore is not { IsActive: true } store)
        {
            return null;
        }

        if (store.Lists.Labels.Any(item => item.Label.IsLive && item.Label.Id == id))
        {
            return id;
        }

        foreach ((Guid labelId, Guid category) in store.CategoryIds)
        {
            if (category == id)
            {
                return labelId;
            }
        }

        return null;
    }
}
