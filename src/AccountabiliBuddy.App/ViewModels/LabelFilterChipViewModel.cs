using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One label of the Tasks page's label row and of its +N menu (Tasks redesign 4b): the same object in both, so a pick, the "not in row"
/// tag and the count show the same everywhere. Kept across rebuilds (updated in place), so a pick fades in over 0.28 s instead of being
/// drawn anew. Picked = the label's colour at 17% with a 1 px border in the full colour (SPEC "Themes and colours").
/// </summary>
public sealed partial class LabelFilterChipViewModel : ObservableObject
{
    // 17% of 255 (hex 2B): a picked label's fill, the same in every theme.
    private const byte PickedAlpha = 0x2B;

    // 25% of 255 (hex 40): the chip under a dragged stack (Tasks redesign 8b.26).
    private const byte DropAlpha = 0x40;

    private readonly Action<Guid> _toggle;

    public LabelFilterChipViewModel(Guid id, Action<Guid> toggle)
    {
        Id = id;
        _toggle = toggle;
        Name = ShortName = CountText = string.Empty;
        Dot = Tint = DropTint = Brushes.Transparent;
        PickCommand = new RelayCommand(() => _toggle(Id));
    }

    public Guid Id { get; }

    /// <summary>The label's full name ("Work - AI"): the row's chip text.</summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>The name under its category heading in the +N menu ("AI" under Work).</summary>
    [ObservableProperty]
    public partial string ShortName { get; set; }

    /// <summary>The label's colour: the dot, and a picked chip's border.</summary>
    [ObservableProperty]
    public partial IBrush Dot { get; set; }

    /// <summary>The label's colour at 17%: a picked chip's (and menu row's) fill.</summary>
    [ObservableProperty]
    public partial IBrush Tint { get; set; }

    /// <summary>The label's colour at 25%: the chip (and menu row) under a dragged stack (8b.26).</summary>
    [ObservableProperty]
    public partial IBrush DropTint { get; set; }

    /// <summary>
    /// Tasks redesign 8b.26 / 8b.28: a dragged stack is over this label's chip, its +N menu row, or (Group by Labels) its column: the chip
    /// and the row light in the label's colour at 25% with a full-colour border.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDropHot { get; set; }

    /// <summary>In the view's label filter (several can be).</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Its whole chip fits in the label row; false = behind +N ("not in row" in the menu, no Tab stop in the row).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotInRow))]
    public partial bool InRow { get; set; } = true;

    public bool IsNotInRow => !InRow;

    /// <summary>"2": its unfinished tasks; blank for none.</summary>
    [ObservableProperty]
    public partial string CountText { get; set; }

    /// <summary>Adds the label to the filter, or takes it off (the chip and its menu row alike).</summary>
    public ICommand PickCommand { get; }

    /// <summary>Takes the label's current name, short name and colour (a rename or recolour since the last build).</summary>
    public void Update(string name, string shortName, Color color, int taskCount)
    {
        Name = name;
        ShortName = shortName;
        if (Dot is not ISolidColorBrush held || held.Color != color)
        {
            Dot = new SolidColorBrush(color);
            Tint = new SolidColorBrush(Color.FromArgb(PickedAlpha, color.R, color.G, color.B));
            DropTint = new SolidColorBrush(Color.FromArgb(DropAlpha, color.R, color.G, color.B));
        }

        CountText = taskCount > 0 ? taskCount.ToString(System.Globalization.CultureInfo.CurrentCulture) : string.Empty;
    }
}
