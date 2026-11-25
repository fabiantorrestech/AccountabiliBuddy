using System.Collections.ObjectModel;
using System.Collections.Specialized;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Options › Layout (0.1.154): the columns of the Tasks view showing now, in this PC's order for its Group by (Bins: Next, To sort, Later
/// and any other bin the view shows; Labels: every label column, then No label), each with a grip to reorder it and a fold toggle. Moves
/// write <see cref="TaskLayoutPreferences.ColumnOrder"/> through <see cref="TaskColumnOrder"/>, folds
/// <see cref="TaskLayoutStore.SetColumnsFoldedAsync"/>; Reset order clears the grouping's stored order. All of it is this PC's own (the
/// "tasks.layout" document, never synced), the same in both modes, and the page follows at once. Not offered in Group by None.
/// </summary>
public sealed partial class TaskLayoutMenuViewModel : ObservableObject
{
    private readonly TaskLayoutStore _layout;
    private readonly ShelfViewModel _shelf;
    private readonly Dictionary<string, TaskLayoutRowViewModel> _known = new(StringComparer.Ordinal);

    public TaskLayoutMenuViewModel(TaskLayoutStore layout, ShelfViewModel shelf)
    {
        _layout = layout;
        _shelf = shelf;
        _layout.Changed += (_, _) => RebuildIfOpen();
        _shelf.Panel.Sections.CollectionChanged += OnSectionsChanged;
    }

    /// <summary>The panel is open under the page's Options button.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>The columns, top to bottom in this PC's order (left to right when open; folded ones are drawn at the right).</summary>
    public ObservableCollection<TaskLayoutRowViewModel> Rows { get; } = [];

    /// <summary>"Bins", "Labels" or "Tags": the Group by whose columns the list holds (Options › Layout's value).</summary>
    public string GroupName => Grouping switch
    {
        TaskViewConfig.GroupLabel => "Labels",
        TaskViewConfig.GroupTag => "Tags",
        _ => "Bins",
    };

    /// <summary>"Bins view" / "Labels view": whose columns the list holds.</summary>
    public string ViewName => GroupName + " view";

    /// <summary>The hint under the title.</summary>
    public string Hint => _shelf.IsGroupedByLabels
        ? "Drag to reorder · folded columns sit at the right of their page · this PC only"
        : "Drag to reorder · folded columns sit at the right · this PC only";

    /// <summary>The order was changed from the grouping's own: Reset order does something.</summary>
    public bool CanReset => Grouping is { } grouping && _layout.Current.ColumnOrderOf(grouping) is not null;

    /// <summary>The Group by whose order the list edits ("bins", "label", "tag"); null in None (one list: Options leaves Layout out).</summary>
    public string? Grouping => _shelf.HasViews ? TaskColumnOrder.GroupingOf(_shelf.Grouping) : TaskViewConfig.GroupBins;

    /// <summary>Options shows its Layout row (not in Group by None).</summary>
    public bool IsAvailable => Grouping is not null;

    partial void OnIsOpenChanged(bool value)
    {
        if (value)
        {
            Rebuild();
        }
    }

    /// <summary>A row was dragged onto the place of another: it takes that place (the others shift by one).</summary>
    public Task MoveOntoAsync(string key, string targetKey) =>
        StoreOrder(key, stored => TaskColumnOrder.MovedOnto(Keys(), stored, key, targetKey));

    /// <summary>Alt+↑/↓ on a row: one place up (-1) or down (1).</summary>
    public Task MoveByAsync(string key, int step) =>
        StoreOrder(key, stored => TaskColumnOrder.MovedBy(Keys(), stored, key, step));

    /// <summary>The row's fold toggle: open ↔ folded to a rail (this PC only).</summary>
    public Task ToggleFoldAsync(string key) => _layout.SetColumnsFoldedAsync([key], !_layout.Current.IsColumnFolded(key));

    /// <summary>Reset order: the grouping's columns go back to their own order (Next, To sort, Later; the labels by category).</summary>
    [RelayCommand]
    private Task ResetOrder() => Grouping is { } grouping ? _layout.ResetColumnOrderAsync(grouping) : Task.CompletedTask;

    [RelayCommand]
    private void Close() => IsOpen = false;

    private List<string> Keys() => Rows.Select(row => row.Key).ToList();

    private Task StoreOrder(string key, Func<IReadOnlyList<string>?, IReadOnlyList<string>?> move)
    {
        if (Grouping is not { } grouping || move(_layout.Current.ColumnOrderOf(grouping)) is not { } order)
        {
            return Task.CompletedTask;
        }

        // The page turns to the column if it moved to another one (Group by Labels' pages).
        _shelf.Pager.Reveal(key);
        return _layout.SetColumnOrderAsync(grouping, order);
    }

    private void OnSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildIfOpen();

    private void RebuildIfOpen()
    {
        if (IsOpen)
        {
            Rebuild();
        }
    }

    // The rows from the columns shown now, in the stored order; rows already shown are moved, not made again.
    private void Rebuild()
    {
        if (Grouping is null && IsOpen)
        {
            // Group by None was picked meanwhile: one list, nothing to arrange.
            IsOpen = false;
        }

        var wanted = new List<TaskLayoutRowViewModel>();
        if (Grouping is { } grouping)
        {
            TaskLayoutPreferences layout = _layout.Current;
            foreach (TaskSectionViewModel section in TaskColumnOrder.Apply(_shelf.Panel.Sections.ToList(), section => section.Key, layout.ColumnOrderOf(grouping)))
            {
                if (!_known.TryGetValue(section.Key, out TaskLayoutRowViewModel? row))
                {
                    row = new TaskLayoutRowViewModel(section.Key, ToggleFoldAsync);
                    _known[section.Key] = row;
                }

                Fill(row, section, layout);
                wanted.Add(row);
            }
        }

        for (int index = 0; index < wanted.Count; index++)
        {
            int at = Rows.IndexOf(wanted[index]);
            if (at == index)
            {
                continue;
            }

            if (at > index)
            {
                Rows.Move(at, index);
            }
            else
            {
                Rows.Insert(index, wanted[index]);
            }
        }

        while (Rows.Count > wanted.Count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }

        OnPropertyChanged(nameof(GroupName));
        OnPropertyChanged(nameof(ViewName));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(CanReset));
        OnPropertyChanged(nameof(IsAvailable));
    }

    private void Fill(TaskLayoutRowViewModel row, TaskSectionViewModel section, TaskLayoutPreferences layout)
    {
        if (section.IsLabelColumn)
        {
            (Material.Icons.MaterialIconKind Icon, string Name)? band = _shelf.BandOf(section.Key);
            row.Name = section.ShortTitle.Length > 0 ? section.ShortTitle : section.Title;
            row.FullName = section.Title;
            row.Icon = band?.Icon;
            row.CategoryName = band?.Name ?? string.Empty;
            row.Dot = section.Dot;
        }
        else
        {
            row.Name = row.FullName = section.Title;
            row.Icon = section.Icon;
            row.CategoryName = string.Empty;
            row.Dot = null;
        }

        row.IsFolded = layout.IsColumnFolded(section.Key);
        row.IsEmptyRail = section.IsEmptyRail;
    }
}
