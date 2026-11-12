using AccountabiliBuddy.Core.Domain;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The flyout behind a label's colour dot on the Labels screen (spec 05 §16): the 12-colour set, a hex or rgba box and an opacity slider
/// (20 to 100 %), with a preview chip. It works on a draft that is kept when the flyout closes (saving rebuilds the rows); Esc leaves the
/// colour as it was. Colours take the forms <see cref="TaskLabelColors"/> accepts: "#RRGGBB" when opaque, "rgba(r,g,b,a)" otherwise.
/// </summary>
public sealed partial class LabelColorEditorViewModel : ObservableObject
{
    private readonly Action<string> _save;
    private string _original;
    private bool _cancelled;
    private bool _quiet;

    /// <param name="save">Keeps a changed colour.</param>
    public LabelColorEditorViewModel(string color, Action<string> save)
    {
        _save = save;
        _original = TaskLabelColors.Typed(color) ?? TaskLabelColors.Set[0];
        Swatches = TaskLabelColors.Set.Select(hex => new ColorPresetViewModel(hex, PickSwatch)).ToList();
        ColorText = _original;
        PreviewBrush = Brushes.Transparent;
        Show(_original);
    }

    /// <summary>The 12-colour set.</summary>
    public IReadOnlyList<ColorPresetViewModel> Swatches { get; }

    /// <summary>The draft as stored ("#2F6BEA", "rgba(47,107,234,0.6)").</summary>
    public string Draft { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IBrush PreviewBrush { get; set; }

    /// <summary>The hex or rgba box.</summary>
    [ObservableProperty]
    public partial string ColorText { get; set; }

    /// <summary>The box holds something that isn't a colour; the last good colour stays the draft.</summary>
    [ObservableProperty]
    public partial bool IsColorBad { get; set; }

    /// <summary>Opacity in percent, 20 to 100.</summary>
    [ObservableProperty]
    public partial double OpacityPercent { get; set; }

    /// <summary>The flyout opens: the draft starts from the colour the label has now.</summary>
    public void Begin(string color)
    {
        _original = TaskLabelColors.Typed(color) ?? _original;
        _cancelled = false;
        Show(_original);
    }

    /// <summary>Esc: closing the flyout keeps nothing.</summary>
    public void Cancel()
    {
        _cancelled = true;
    }

    /// <summary>The flyout closed: a changed colour is kept, unless Esc closed it.</summary>
    public void Finish()
    {
        if (!_cancelled && !string.Equals(Draft, _original, StringComparison.OrdinalIgnoreCase))
        {
            _original = Draft;
            _save(Draft);
        }
    }

    private void PickSwatch(string hex)
    {
        Show(TaskLabelColors.WithOpacity(hex, OpacityPercent / 100.0));
    }

    // keepText: the box is being typed in; its text stays as typed.
    private void Show(string color, bool keepText = false)
    {
        Draft = color;
        PreviewBrush = TaskCategoryBrushes.Of(TaskLabelColors.ArgbHex(color));
        _quiet = true;
        if (!keepText)
        {
            ColorText = color;
        }

        OpacityPercent = Math.Round(TaskLabelColors.OpacityOf(color) * 100.0);
        _quiet = false;
        IsColorBad = false;
        foreach (ColorPresetViewModel swatch in Swatches)
        {
            swatch.IsSelected = TaskLabelColors.SameOpaque(swatch.Hex, color);
        }
    }

    partial void OnColorTextChanged(string value)
    {
        if (_quiet)
        {
            return;
        }

        if (TaskLabelColors.Typed(value) is string typed)
        {
            Show(typed, keepText: true);
        }
        else
        {
            IsColorBad = true;
        }
    }

    partial void OnOpacityPercentChanged(double value)
    {
        if (_quiet)
        {
            return;
        }

        Show(TaskLabelColors.WithOpacity(Draft, value / 100.0));
    }
}
