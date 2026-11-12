using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One soft block of the Goals list (round 2, brief §3.4, §3.5): By area has one per area, with its header (16.5 px bold, the
/// area's icon, a count pill, ✎ on hover or keyboard focus → Edit area, R5b) that folds it (remembered per area on this PC, in
/// "goals.ui"); Current and All have one block without a header.
/// </summary>
public sealed partial class GoalAreaBlockViewModel : ObservableObject
{
    private readonly Action<GoalAreaBlockViewModel> _toggleFold;
    private readonly Action<GoalAreaBlockViewModel> _edit;

    public GoalAreaBlockViewModel(Guid? areaId, string title, MaterialIconKind icon, Action<GoalAreaBlockViewModel> toggleFold, Action<GoalAreaBlockViewModel> edit)
    {
        AreaId = areaId;
        Title = title;
        Icon = icon;
        _toggleFold = toggleFold;
        _edit = edit;
    }

    /// <summary>The area, or null for a block without a header (Current, All).</summary>
    public Guid? AreaId { get; }

    public string Title { get; }

    public MaterialIconKind Icon { get; }

    public bool ShowsHeader => AreaId is not null;

    public ObservableCollection<GoalRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsRows))]
    public partial bool IsFolded { get; set; }

    public bool ShowsRows => !IsFolded && Rows.Count > 0;

    public string CountText => Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string FoldTip => IsFolded ? $"Show {Title}" : $"Fold {Title}";

    /// <summary>"Edit Work" (the ✎'s name and tooltip).</summary>
    public string EditTip => $"Edit {Title}";

    public void RowsChanged()
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(ShowsRows));
    }

    [RelayCommand]
    private void ToggleFold()
    {
        _toggleFold(this);
        OnPropertyChanged(nameof(FoldTip));
    }

    [RelayCommand]
    private void Edit()
    {
        _edit(this);
    }
}
