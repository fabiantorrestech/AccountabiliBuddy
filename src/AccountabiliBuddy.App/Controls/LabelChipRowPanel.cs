using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The Tasks page's label row (Tasks redesign 4b.01, 4b.06): its chips in one line, <see cref="Spacing"/> apart, never wrapping or scrolling
/// sideways. Only the chips that fit whole are shown, measured at their real width; the rest are laid out past the panel's edge, where its
/// clip hides them (they keep their size, so a wider window brings them back at once). <see cref="FitCount"/> says how many fit, so the page
/// can count the rest behind +N and take them out of the Tab order.
/// </summary>
public sealed class LabelChipRowPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<LabelChipRowPanel, double>(nameof(Spacing), 6);

    public static readonly StyledProperty<int> FitCountProperty =
        AvaloniaProperty.Register<LabelChipRowPanel, int>(nameof(FitCount), defaultBindingMode: BindingMode.OneWayToSource);

    private int _fit;

    static LabelChipRowPanel()
    {
        AffectsMeasure<LabelChipRowPanel>(SpacingProperty);
        ClipToBoundsProperty.OverrideDefaultValue<LabelChipRowPanel>(true);
    }

    /// <summary>The gap between two chips.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>How many chips (from the first) fit whole; written by the panel as it lays them out.</summary>
    public int FitCount
    {
        get => GetValue(FitCountProperty);
        set => SetValue(FitCountProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double used = 0;
        double height = 0;
        int fit = 0;
        bool full = false;
        foreach (Control child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
            if (full)
            {
                continue;
            }

            double next = used + (fit > 0 ? Spacing : 0) + child.DesiredSize.Width;
            if (next > availableSize.Width + 0.5)
            {
                // Whole chips only, in order: the first that doesn't fit and every one after it go behind +N.
                full = true;
                continue;
            }

            used = next;
            fit++;
        }

        _fit = fit;
        return new Size(used, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        for (int index = 0; index < Children.Count; index++)
        {
            Control child = Children[index];
            double width = child.DesiredSize.Width;
            double y = child.VerticalAlignment == VerticalAlignment.Top ? 0 : Math.Max(0, (finalSize.Height - child.DesiredSize.Height) / 2);
            if (index < _fit)
            {
                child.Arrange(new Rect(x, y, width, child.DesiredSize.Height));
                x += width + Spacing;
            }
            else
            {
                // Past the edge: clipped, so neither drawn nor hit.
                child.Arrange(new Rect(finalSize.Width + 10_000, y, width, child.DesiredSize.Height));
            }
        }

        if (FitCount != _fit)
        {
            FitCount = _fit;
        }

        return finalSize;
    }
}
