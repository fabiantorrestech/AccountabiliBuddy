using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One label in the <see cref="LabelPickerViewModel"/>'s pane: its dot, its short name (without its category's "Work - " prefix; the full name is
/// the accessible name), its tag ("Current" / "N have it") and, picked, its own colour at 17% with a 1 px border in the full colour (the same
/// in every theme, checklist 3.25). Unpicked rows keep a transparent 1 px border so nothing shifts.
/// </summary>
public sealed partial class LabelPickerItemViewModel : ObservableObject
{
    // 17% of the label's colour behind a picked row (hex alpha 2B).
    private const byte PickedAlpha = 0x2B;

    private readonly IBrush _pickedFill;

    public LabelPickerItemViewModel(Guid labelId, string fullName, string shortName, string categoryName, Color color, string tag, Action<LabelPickerItemViewModel> pick)
    {
        LabelId = labelId;
        FullName = fullName;
        ShortName = shortName;
        CategoryName = categoryName;
        Tag = tag;
        Dot = new SolidColorBrush(color);
        _pickedFill = new SolidColorBrush(Color.FromArgb(PickedAlpha, color.R, color.G, color.B));
        PickCommand = new RelayCommand(() => pick(this));
    }

    public Guid LabelId { get; }

    /// <summary>The label's whole name ("Work - AI"): the accessible name, and what a search reads.</summary>
    public string FullName { get; }

    /// <summary>The name under its own category heading ("AI").</summary>
    public string ShortName { get; }

    /// <summary>Its category's name ("" for Other labels): a search reads it too.</summary>
    public string CategoryName { get; }

    /// <summary>"Current" when every task being changed has it, "N have it" when some do, "" when none.</summary>
    public string Tag { get; }

    public bool HasTag => Tag.Length > 0;

    /// <summary>The label's colour, in full.</summary>
    public IBrush Dot { get; }

    public IRelayCommand PickCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fill), nameof(Line))]
    public partial bool IsPicked { get; set; }

    public IBrush Fill => IsPicked ? _pickedFill : Brushes.Transparent;

    public IBrush Line => IsPicked ? Dot : Brushes.Transparent;

    /// <summary>A case-insensitive part of "category name + full name" (so "work" finds every Work label and "ai" finds "Work - AI").</summary>
    public bool Matches(string query)
    {
        string haystack = CategoryName.Length > 0 ? CategoryName + " " + FullName : FullName;
        return haystack.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }
}
