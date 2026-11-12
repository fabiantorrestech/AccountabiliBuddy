using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A main part with a fixed side slot (round 2, change note §4a: the goal heatmap and its day panel), by the panel's own width: the
/// slot sits to the right of the main part, <see cref="SideMinWidth"/> to <see cref="SideMaxWidth"/> wide, while the panel is at
/// least <see cref="StackBelow"/> wide; under that the slot moves under the main part at full width. Either way it is always there
/// (it never appears, disappears or moves while the width stays). Children, in order: the main part, the side slot.
/// </summary>
public sealed class SideSlotPanel : Panel
{
    public static readonly StyledProperty<double> StackBelowProperty =
        AvaloniaProperty.Register<SideSlotPanel, double>(nameof(StackBelow), 700);

    public static readonly StyledProperty<double> SideMinWidthProperty =
        AvaloniaProperty.Register<SideSlotPanel, double>(nameof(SideMinWidth), 200);

    public static readonly StyledProperty<double> SideMaxWidthProperty =
        AvaloniaProperty.Register<SideSlotPanel, double>(nameof(SideMaxWidth), 280);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<SideSlotPanel, double>(nameof(Spacing), 16);

    public static readonly DirectProperty<SideSlotPanel, bool> IsStackedProperty =
        AvaloniaProperty.RegisterDirect<SideSlotPanel, bool>(nameof(IsStacked), panel => panel.IsStacked);

    private bool _isStacked;

    static SideSlotPanel()
    {
        AffectsMeasure<SideSlotPanel>(StackBelowProperty, SideMinWidthProperty, SideMaxWidthProperty, SpacingProperty);
    }

    public double StackBelow
    {
        get => GetValue(StackBelowProperty);
        set => SetValue(StackBelowProperty, value);
    }

    public double SideMinWidth
    {
        get => GetValue(SideMinWidthProperty);
        set => SetValue(SideMinWidthProperty, value);
    }

    public double SideMaxWidth
    {
        get => GetValue(SideMaxWidthProperty);
        set => SetValue(SideMaxWidthProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>True while the side slot sits under the main part.</summary>
    public bool IsStacked
    {
        get => _isStacked;
        private set => SetAndRaise(IsStackedProperty, ref _isStacked, value);
    }

    /// <summary>The rule on its own (also for the checks): whether the slot stacks, and its width when beside.</summary>
    public static (bool Stacked, double SideWidth) Layout(double width, double stackBelow, double sideMin, double sideMax)
    {
        if (width < stackBelow)
        {
            return (true, width);
        }

        return (false, Math.Clamp(width * 0.32, sideMin, sideMax));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        (Control? main, Control? side) = Parts();
        double width = double.IsInfinity(availableSize.Width) ? StackBelow : availableSize.Width;
        (bool stacked, double sideWidth) = Layout(width, StackBelow, SideMinWidth, SideMaxWidth);
        IsStacked = stacked;
        if (stacked)
        {
            main?.Measure(new Size(width, double.PositiveInfinity));
            side?.Measure(new Size(width, double.PositiveInfinity));
            double height = (main?.DesiredSize.Height ?? 0) + Spacing + (side?.DesiredSize.Height ?? 0);
            return new Size(width, height);
        }

        double mainWidth = Math.Max(0, width - sideWidth - Spacing);
        main?.Measure(new Size(mainWidth, double.PositiveInfinity));
        side?.Measure(new Size(sideWidth, double.PositiveInfinity));
        return new Size(width, Math.Max(main?.DesiredSize.Height ?? 0, side?.DesiredSize.Height ?? 0));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        (Control? main, Control? side) = Parts();
        (bool stacked, double sideWidth) = Layout(finalSize.Width, StackBelow, SideMinWidth, SideMaxWidth);
        if (stacked)
        {
            double mainHeight = main?.DesiredSize.Height ?? 0;
            main?.Arrange(new Rect(0, 0, finalSize.Width, mainHeight));
            side?.Arrange(new Rect(0, mainHeight + Spacing, finalSize.Width, side.DesiredSize.Height));
            return finalSize;
        }

        double mainWidth = Math.Max(0, finalSize.Width - sideWidth - Spacing);
        main?.Arrange(new Rect(0, 0, mainWidth, finalSize.Height));
        side?.Arrange(new Rect(mainWidth + Spacing, 0, sideWidth, side.DesiredSize.Height));
        return finalSize;
    }

    private (Control? Main, Control? Side) Parts()
    {
        return (Children.Count > 0 ? Children[0] : null, Children.Count > 1 ? Children[1] : null);
    }
}
