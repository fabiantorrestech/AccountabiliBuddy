using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One task brought along to Move and label (checklist 8c.12-8c.28): the round tick (the dialog's progress, not the task's done state),
/// the title, and its Bin, Label and Tags lines. While it is being edited a proposed change shows after the current value (struck through
/// at 60%): an accent arrow and a lit pill with the new bin or label, tags coming off struck in Drift, tags going on as "+#tag" pills.
/// The dialog fills it in (<see cref="MoveAndLabelViewModel"/>); the row only holds what it shows.
/// </summary>
public sealed partial class MoveAndLabelRowViewModel : ObservableObject
{
    /// <summary>A ticked row (8c.27): dimmed; picked again for editing, less so.</summary>
    private const double TickedOpacity = 0.45;
    private const double TickedEditingOpacity = 0.8;

    public MoveAndLabelRowViewModel(Guid id, Action<MoveAndLabelRowViewModel> tick)
    {
        Id = id;
        Title = string.Empty;
        BinName = string.Empty;
        BinToName = string.Empty;
        LabelName = string.Empty;
        LabelToName = string.Empty;
        LabelDot = Brushes.Transparent;
        LabelToDot = Brushes.Transparent;
        LabelToFill = Brushes.Transparent;
        Tags = [];
        TickCommand = new RelayCommand(() => tick(this));
    }

    public Guid Id { get; }

    public IRelayCommand TickCommand { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    /// <summary>Being edited (8c.22): the accent-soft fill and ring.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowOpacity))]
    public partial bool IsEditing { get; set; }

    /// <summary>Ticked in this dialog (8c.14): Apply changed it, or the user ticked it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowOpacity), nameof(TickTip))]
    public partial bool IsTicked { get; set; }

    public double RowOpacity => IsTicked ? IsEditing ? TickedEditingOpacity : TickedOpacity : 1;

    public string TickTip => IsTicked ? "Done. Click to mark it not done" : "Mark done";

    // ---- Bin ----

    [ObservableProperty]
    public partial MaterialIconKind BinIcon { get; set; }

    [ObservableProperty]
    public partial string BinName { get; set; }

    [ObservableProperty]
    public partial bool HasBinChange { get; set; }

    [ObservableProperty]
    public partial MaterialIconKind BinToIcon { get; set; }

    [ObservableProperty]
    public partial string BinToName { get; set; }

    // ---- Label ----

    /// <summary>The label's full name, or "No label".</summary>
    [ObservableProperty]
    public partial string LabelName { get; set; }

    /// <summary>The task has no label (a hollow ring in place of the dot).</summary>
    [ObservableProperty]
    public partial bool IsUnlabelled { get; set; }

    [ObservableProperty]
    public partial IBrush LabelDot { get; set; }

    [ObservableProperty]
    public partial bool HasLabelChange { get; set; }

    [ObservableProperty]
    public partial string LabelToName { get; set; }

    /// <summary>The new label is No label (the pill shows a hollow ring and the accent look).</summary>
    [ObservableProperty]
    public partial bool IsLabelToNone { get; set; }

    [ObservableProperty]
    public partial IBrush LabelToDot { get; set; }

    /// <summary>The new label's colour at 17% (8c.19).</summary>
    [ObservableProperty]
    public partial IBrush LabelToFill { get; set; }

    // ---- Tags ----

    /// <summary>The task's tags (struck when coming off), then the ones going on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoTags))]
    public partial IReadOnlyList<MoveAndLabelRowTag> Tags { get; set; }

    /// <summary>"none" in Faint.</summary>
    public bool HasNoTags => Tags.Count == 0;
}
