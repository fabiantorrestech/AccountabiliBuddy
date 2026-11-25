using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One row of Options › Layout (0.1.154): a column of the view showing now (a bin, or a label column with its dot and category icon), its
/// grip and its fold toggle. The row is kept while the list is open (moved, not drawn again), so the keyboard stays on it after a move.
/// </summary>
public sealed partial class TaskLayoutRowViewModel : ObservableObject
{
    private readonly Func<string, Task> _toggleFold;

    public TaskLayoutRowViewModel(string key, Func<string, Task> toggleFold)
    {
        Key = key;
        _toggleFold = toggleFold;
        Name = FullName = CategoryName = string.Empty;
    }

    /// <summary>The column's key ("bin:next", "label:…", "label:none").</summary>
    public string Key { get; }

    /// <summary>What the row shows: a bin's name, a label's short name, "No label".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    public partial string Name { get; set; }

    /// <summary>The tooltip on the name: a label's full name ("Work - AI").</summary>
    [ObservableProperty]
    public partial string FullName { get; set; }

    /// <summary>A bin's icon, or a label column's category icon; null for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon), nameof(IconKind))]
    public partial MaterialIconKind? Icon { get; set; }

    public bool HasIcon => Icon is not null;

    public MaterialIconKind IconKind => Icon ?? MaterialIconKind.Tray;

    /// <summary>The category's name, the icon's tooltip; empty for a bin.</summary>
    [ObservableProperty]
    public partial string CategoryName { get; set; }

    /// <summary>A label column's colour dot; null for a bin and No label.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDot))]
    public partial IBrush? Dot { get; set; }

    public bool HasDot => Dot is not null;

    /// <summary>Folded to a rail on this PC.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FoldIcon), nameof(FoldTip), nameof(StateText), nameof(HasStateText), nameof(AccessibleName))]
    public partial bool IsFolded { get; set; }

    /// <summary>Later with no task at all: a rail whatever its fold.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(HasStateText))]
    public partial bool IsEmptyRail { get; set; }

    /// <summary>The toggle's icon: an open column, or a folded one.</summary>
    public MaterialIconKind FoldIcon => IsFolded ? MaterialIconKind.ArrowCollapseHorizontal : MaterialIconKind.ViewColumnOutline;

    public string FoldTip => IsFolded ? "Folded to a rail at the right · click to open" : "Open · click to fold it to a rail at the right";

    /// <summary>"folded" or "empty" beside the name; empty while it is an open column.</summary>
    public string StateText => IsFolded ? "folded" : IsEmptyRail ? "empty" : string.Empty;

    public bool HasStateText => StateText.Length > 0;

    public string AccessibleName => $"{Name}, {(IsFolded ? "folded" : "open")} (Alt+Up or Alt+Down moves it)";

    [RelayCommand]
    private Task ToggleFold() => _toggleFold(Key);
}
