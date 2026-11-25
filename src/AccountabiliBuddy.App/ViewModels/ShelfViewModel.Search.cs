using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

// Tasks redesign item 7: the Shelf's Search filters (the panel under the header's search box). The filters live in the view service's
// memory (TaskViewService.SearchFilters): this PC's search state, never stored in a view, never synced (7.35).
public sealed partial class ShelfViewModel : ISearchFiltersHost
{
    // Before the task list exists (no view service), the filters are held here; they narrow nothing then.
    private TaskSearchFilters _localFilters = TaskSearchFilters.Default;
    private SearchFiltersViewModel? _filtersPanel;

    /// <summary>The Search filters panel (built once; its chips are rebuilt each time it opens).</summary>
    public SearchFiltersViewModel FiltersPanel
    {
        get
        {
            if (_filtersPanel is null)
            {
                _filtersPanel = new SearchFiltersViewModel(this, "Shelf", showsBins: true);
                _filtersPanel.CloseRequested += (_, _) => IsSearchFiltersOpen = false;
            }

            return _filtersPanel;
        }
    }

    /// <summary>The Search filters panel is open (the filter button in the header's search box).</summary>
    [ObservableProperty]
    public partial bool IsSearchFiltersOpen { get; set; }

    /// <summary>A filter narrows the Shelf: the filter button shows "on".</summary>
    public bool HasSearchFilters => !SearchFilters.IsDefault;

    /// <inheritdoc />
    public TaskSearchFilters SearchFilters
    {
        get => _views?.SearchFilters ?? _localFilters;
        set
        {
            if (_views is null)
            {
                _localFilters = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _views.SetSearchFilters(value);
            }

            OnPropertyChanged(nameof(SearchFilters));
            OnPropertyChanged(nameof(HasSearchFilters));
        }
    }

    string ISearchFiltersHost.SearchWords
    {
        get => SearchText;
        set => SearchText = value;
    }

    TaskStore? ISearchFiltersHost.SearchStore => _store;

    bool ISearchFiltersHost.IsLabelInRow(Guid labelId) => _views is not null && ShownLabelIds(_views.Working.Labels).Contains(labelId);

    bool ISearchFiltersHost.IsNoLabelInRow => _views?.Working.NoLabel == true;

    void ISearchFiltersHost.TakeOffRow(Guid? labelId)
    {
        if (_views is null)
        {
            return;
        }

        if (labelId is Guid id)
        {
            if (ShownLabelIds(_views.Working.Labels).Contains(id))
            {
                ToggleLabel(id);
            }
        }
        else if (_views.Working.NoLabel)
        {
            ToggleNoLabel();
        }
    }

    /// <summary>The filter button: opens or closes the panel.</summary>
    [RelayCommand]
    private void ToggleSearchFilters() => IsSearchFiltersOpen = !IsSearchFiltersOpen;

    partial void OnIsSearchFiltersOpenChanged(bool value)
    {
        if (value)
        {
            IsLabelsMenuOpen = false;
            FiltersPanel.Rebuild();
        }
    }

    // The words or the label row changed while the panel is open: its chips follow (a typed "#waiting" shows ✓, 7.26).
    private void ShowFiltersState()
    {
        if (IsSearchFiltersOpen)
        {
            FiltersPanel.ShowState();
        }
    }
}
