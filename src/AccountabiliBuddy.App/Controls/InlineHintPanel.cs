using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// One line of three parts (the add box, Tasks redesign 5.40-5.41): a lead (the chips) at its own width, a fill (the text) that takes the
/// rest, and a hint at the right edge that is the first thing to go: it shows only while the fill keeps at least <see cref="MinFillWidth"/>.
/// A hint that doesn't fit is laid out past the right edge and clipped (no visibility flip, so the layout never loops). Children in that
/// order; a missing or hidden one takes no room.
/// </summary>
public sealed class InlineHintPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<InlineHintPanel, double>(nameof(Spacing), 8);

    public static readonly StyledProperty<double> MinFillWidthProperty = AvaloniaProperty.Register<InlineHintPanel, double>(nameof(MinFillWidth), 120);

    private bool _hintFits;

    static InlineHintPanel()
    {
        AffectsMeasure<InlineHintPanel>(SpacingProperty, MinFillWidthProperty);
        ClipToBoundsProperty.OverrideDefaultValue<InlineHintPanel>(true);
    }

    /// <summary>The gap between the parts.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>The room the fill keeps before the hint goes.</summary>
    public double MinFillWidth
    {
        get => GetValue(MinFillWidthProperty);
        set => SetValue(MinFillWidthProperty, value);
    }

    /// <summary>The hint shows (it had room at the last measure).</summary>
    public bool HintFits => _hintFits;

    protected override Size MeasureOverride(Size availableSize)
    {
        (Control? lead, Control? fill, Control? hint) = Parts();
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        lead?.Measure(unbounded);
        hint?.Measure(unbounded);
        double leadWidth = WidthOf(lead);
        double hintWidth = WidthOf(hint);
        double leadGap = leadWidth > 0 ? Spacing : 0;
        double width = availableSize.Width;
        _hintFits = hintWidth > 0 && (double.IsInfinity(width) || width - leadWidth - leadGap - hintWidth - Spacing >= MinFillWidth);
        double hintRoom = _hintFits ? hintWidth + Spacing : 0;
        double fillRoom = double.IsInfinity(width) ? double.PositiveInfinity : Math.Max(0, width - leadWidth - leadGap - hintRoom);
        fill?.Measure(new Size(fillRoom, availableSize.Height));
        double fillWidth = WidthOf(fill);
        double height = Math.Max(HeightOf(lead), Math.Max(HeightOf(fill), HeightOf(hint)));
        double desired = leadWidth + leadGap + fillWidth + hintRoom;
        return new Size(double.IsInfinity(width) ? desired : Math.Min(desired, width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        (Control? lead, Control? fill, Control? hint) = Parts();
        double leadWidth = WidthOf(lead);
        double leadGap = leadWidth > 0 ? Spacing : 0;
        double hintWidth = WidthOf(hint);
        lead?.Arrange(new Rect(0, 0, leadWidth, finalSize.Height));
        double fillLeft = leadWidth + leadGap;
        double hintRoom = _hintFits ? hintWidth + Spacing : 0;
        fill?.Arrange(new Rect(fillLeft, 0, Math.Max(0, finalSize.Width - fillLeft - hintRoom), finalSize.Height));
        // A hint with no room goes past the right edge, where the panel's clip hides it (and nothing there takes clicks).
        double hintLeft = _hintFits ? finalSize.Width - hintWidth : finalSize.Width + Spacing;
        hint?.Arrange(new Rect(hintLeft, 0, hintWidth, finalSize.Height));
        return finalSize;
    }

    private (Control? Lead, Control? Fill, Control? Hint) Parts()
    {
        return (Children.Count > 0 ? Children[0] : null, Children.Count > 1 ? Children[1] : null, Children.Count > 2 ? Children[2] : null);
    }

    private static double WidthOf(Control? control) => control is { IsVisible: true } ? control.DesiredSize.Width : 0;

    private static double HeightOf(Control? control) => control is { IsVisible: true } ? control.DesiredSize.Height : 0;
}
