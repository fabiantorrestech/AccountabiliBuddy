using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The page header's one line (Tasks redesign 4a.02-4a.08, 4a.21): the title, the date, flexible space, then at the right the page's
/// search (if it has one), its own controls and Options, <see cref="Spacing"/> apart and vertically centred. It never wraps. When the
/// line runs out of room the date shortens first, with an ellipsis, down to <see cref="DateMinText"/> ("Tuesday · …"); only then does
/// the search narrow, from its MaxWidth to its MinWidth; after that the date gives up the rest. The parts are set by
/// <see cref="PageHeader"/>; any of them may be hidden.
/// </summary>
public sealed class PageHeaderRow : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<PageHeaderRow, double>(nameof(Spacing), 10);

    private double _dateWidth;
    private double _searchWidth;

    static PageHeaderRow()
    {
        AffectsMeasure<PageHeaderRow>(SpacingProperty);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public Control? TitlePart { get; set; }

    public TextBlock? DatePart { get; set; }

    /// <summary>The shortest the date gets before the search starts to narrow ("Tuesday · …").</summary>
    public string DateMinText { get; set; } = string.Empty;

    /// <summary>The page's search; its MinWidth and MaxWidth are how far it may narrow and widen. Hidden while it has nothing to show.</summary>
    public Control? SearchPart { get; set; }

    public Control? ActionsPart { get; set; }

    public Control? OptionsPart { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        double title = MeasureNatural(TitlePart, infinite);
        double actions = MeasureNatural(ActionsPart, infinite);
        double options = MeasureNatural(OptionsPart, infinite);
        double dateFull = MeasureNatural(DatePart, infinite);
        bool hasDate = Shows(DatePart);
        bool hasSearch = Shows(SearchPart);
        double searchMax = hasSearch ? Finite(SearchPart!.MaxWidth, 300) : 0;
        double searchMin = hasSearch ? Math.Min(SearchPart!.MinWidth, searchMax) : 0;

        int parts = new[] { Shows(TitlePart), hasDate, hasSearch, Shows(ActionsPart), Shows(OptionsPart) }.Count(shown => shown);
        double gaps = Math.Max(0, parts - 1) * Spacing;
        double fixedWidth = title + actions + options + gaps;
        double rest = double.IsInfinity(availableSize.Width) ? dateFull + searchMax : Math.Max(0, availableSize.Width - fixedWidth);

        if (rest >= dateFull + searchMax)
        {
            _dateWidth = dateFull;
            _searchWidth = searchMax;
        }
        else
        {
            // The date gives way first, down to "Tuesday · …"; then the search, down to its minimum; then the date again.
            double dateMin = Math.Min(DateMinWidth(), dateFull);
            _dateWidth = Math.Clamp(rest - searchMax, dateMin, dateFull);
            _searchWidth = Math.Clamp(rest - _dateWidth, searchMin, searchMax);
            _dateWidth = Math.Max(0, Math.Min(_dateWidth, rest - _searchWidth));
        }

        if (hasDate)
        {
            DatePart!.Measure(new Size(_dateWidth, availableSize.Height));
        }

        if (hasSearch)
        {
            SearchPart!.Measure(new Size(_searchWidth, availableSize.Height));
        }

        double height = 0;
        foreach (Control child in Children)
        {
            height = Math.Max(height, child.DesiredSize.Height);
        }

        double width = fixedWidth + (hasDate ? _dateWidth : 0) + (hasSearch ? _searchWidth : 0);
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double height = finalSize.Height;
        double left = 0;
        left = ArrangeFromLeft(TitlePart, left, TitlePart?.DesiredSize.Width ?? 0, height);
        ArrangeFromLeft(DatePart, left, _dateWidth, height);

        double right = finalSize.Width;
        right = ArrangeFromRight(OptionsPart, right, OptionsPart?.DesiredSize.Width ?? 0, height);
        right = ArrangeFromRight(ActionsPart, right, ActionsPart?.DesiredSize.Width ?? 0, height);
        ArrangeFromRight(SearchPart, right, _searchWidth, height);
        return finalSize;
    }

    private static bool Shows(Control? part) => part is { IsVisible: true };

    private static double Finite(double value, double fallback) => double.IsInfinity(value) || double.IsNaN(value) ? fallback : value;

    private static double MeasureNatural(Control? part, Size infinite)
    {
        if (part is null)
        {
            return 0;
        }

        part.Measure(infinite);
        return Shows(part) ? part.DesiredSize.Width : 0;
    }

    private double ArrangeFromLeft(Control? part, double left, double width, double height)
    {
        if (!Shows(part))
        {
            return left;
        }

        part!.Arrange(new Rect(left, 0, width, height));
        return left + width + Spacing;
    }

    private double ArrangeFromRight(Control? part, double right, double width, double height)
    {
        if (!Shows(part))
        {
            return right;
        }

        part!.Arrange(new Rect(Math.Max(0, right - width), 0, width, height));
        return right - width - Spacing;
    }

    private (string Text, Typeface Typeface, double Size, double Width)? _dateTextWidth;

    // The width "Tuesday · …" takes in the date's own font, plus its margin.
    private double DateMinWidth()
    {
        if (DatePart is not TextBlock date || DateMinText.Length == 0)
        {
            return 0;
        }

        var typeface = new Typeface(date.FontFamily, date.FontStyle, date.FontWeight, date.FontStretch);
        // The text's width is measured once per (text, font, size), not on every measure pass.
        if (_dateTextWidth is not { } cached || cached.Text != DateMinText || cached.Typeface != typeface || cached.Size != date.FontSize)
        {
            using var layout = new TextLayout(DateMinText, typeface, date.FontSize, null);
            cached = (DateMinText, typeface, date.FontSize, Math.Ceiling(layout.Width));
            _dateTextWidth = cached;
        }

        return cached.Width + date.Margin.Left + date.Margin.Right + date.Padding.Left + date.Padding.Right;
    }
}
