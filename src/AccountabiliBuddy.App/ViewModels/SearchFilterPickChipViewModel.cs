using System.Windows.Input;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A label or tag chip of Search filters (Tasks redesign 7.20-7.27): a click cycles any → ✓ include → ✕ leave out → any. A label chip
/// carries its colour's dot and short name ("ePT"); a tag chip reads "#docs".
/// </summary>
public sealed partial class SearchFilterPickChipViewModel : ObservableObject
{
    public SearchFilterPickChipViewModel(Guid? labelId, string? tag, string name, IBrush? dot, Action<SearchFilterPickChipViewModel> cycle)
    {
        LabelId = labelId;
        Tag = tag;
        Name = name;
        Dot = dot;
        CycleCommand = new RelayCommand(() => cycle(this));
    }

    /// <summary>The label (null for a tag chip and for "No label").</summary>
    public Guid? LabelId { get; }

    /// <summary>The tag without "#" (null for a label chip).</summary>
    public string? Tag { get; }

    public bool IsTag => Tag is not null;

    /// <summary>"ePT", "No label", "#docs".</summary>
    public string Name { get; }

    /// <summary>The label's colour; null for tags and "No label".</summary>
    public IBrush? Dot { get; }

    public bool HasDot => Dot is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIncluded), nameof(IsLeftOut), nameof(Mark), nameof(HasMark), nameof(AccessibleName))]
    public partial TaskSearchPick Pick { get; set; }

    public bool IsIncluded => Pick == TaskSearchPick.Include;

    public bool IsLeftOut => Pick == TaskSearchPick.LeaveOut;

    /// <summary>"✓" or "✕" before the name; empty for any.</summary>
    public string Mark => Pick switch
    {
        TaskSearchPick.Include => "✓",
        TaskSearchPick.LeaveOut => "✕",
        _ => string.Empty,
    };

    public bool HasMark => Pick != TaskSearchPick.Any;

    /// <summary>"ePT, include" / "ePT, leave out" / "ePT, any".</summary>
    public string AccessibleName => Name + Pick switch
    {
        TaskSearchPick.Include => ", include",
        TaskSearchPick.LeaveOut => ", leave out",
        _ => ", any",
    };

    public ICommand CycleCommand { get; }
}
