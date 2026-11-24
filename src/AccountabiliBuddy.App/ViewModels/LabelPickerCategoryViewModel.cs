using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One row of the <see cref="LabelPickerViewModel"/>'s category rail: a label group (ADR 93) or "Other labels" (the labels with no category,
/// or whose category is missing or deleted). A click shows its labels and clears the search.
/// </summary>
public sealed partial class LabelPickerCategoryViewModel : ObservableObject
{
    /// <summary>How faint a rail row is while a search finds nothing in it (checklist 3.61).</summary>
    public const double DimmedOpacity = 0.35;

    public LabelPickerCategoryViewModel(Guid? groupId, string name, MaterialIconKind icon, IReadOnlyList<LabelPickerItemViewModel> labels, Action<LabelPickerCategoryViewModel> select)
    {
        GroupId = groupId;
        Name = name;
        Icon = icon;
        Labels = labels;
        SelectCommand = new RelayCommand(() => select(this));
    }

    /// <summary>The label group; null for "Other labels".</summary>
    public Guid? GroupId { get; }

    public bool IsOther => GroupId is null;

    public string Name { get; }

    public MaterialIconKind Icon { get; }

    /// <summary>Its labels, in the label list's order.</summary>
    public IReadOnlyList<LabelPickerItemViewModel> Labels { get; }

    public string CountText => Labels.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    public IRelayCommand SelectCommand { get; }

    /// <summary>The category the label pane shows (none is selected while a search runs).</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>It holds the picked label: a small accent dot before the count.</summary>
    [ObservableProperty]
    public partial bool HasPick { get; set; }

    /// <summary>A search runs and finds none of its labels.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowOpacity))]
    public partial bool IsDimmed { get; set; }

    public double RowOpacity => IsDimmed ? DimmedOpacity : 1;
}
