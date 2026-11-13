using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// One line of text that can have an outline around each letter (the overlay clock's "Black text outline", Settings › Overlay), so it
/// reads over any background. The outline is drawn outside the letters and never changes the control's size: turning it on moves nothing.
/// Font, size, weight and colour are inherited like a TextBlock's.
/// </summary>
public sealed class OutlinedText : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<OutlinedText, string?>(nameof(Text));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<OutlinedText>();

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<OutlinedText>();

    public static readonly StyledProperty<double> FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner<OutlinedText>();

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner<OutlinedText>();

    public static readonly StyledProperty<IBrush?> OutlineBrushProperty =
        AvaloniaProperty.Register<OutlinedText, IBrush?>(nameof(OutlineBrush));

    /// <summary>How far the outline reaches outside each letter, in DIPs; 0 = none.</summary>
    public static readonly StyledProperty<double> OutlineThicknessProperty =
        AvaloniaProperty.Register<OutlinedText, double>(nameof(OutlineThickness));

    static OutlinedText()
    {
        AffectsMeasure<OutlinedText>(TextProperty, FontFamilyProperty, FontSizeProperty, FontWeightProperty);
        AffectsRender<OutlinedText>(ForegroundProperty, OutlineBrushProperty, OutlineThicknessProperty);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public IBrush? OutlineBrush
    {
        get => GetValue(OutlineBrushProperty);
        set => SetValue(OutlineBrushProperty, value);
    }

    public double OutlineThickness
    {
        get => GetValue(OutlineThicknessProperty);
        set => SetValue(OutlineThicknessProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        FormattedText text = Format();
        // The outline's width is left out on purpose: it is drawn past the edges, so turning it on doesn't move the tile's other parts.
        return new Size(text.WidthIncludingTrailingWhitespace, text.Height);
    }

    public override void Render(DrawingContext context)
    {
        FormattedText text = Format();
        double reach = OutlineThickness;
        if (reach > 0 && OutlineBrush is IBrush outline && !string.IsNullOrEmpty(Text))
        {
            // A stroke is centred on the letter's edge, so twice the reach puts that much outside; the fill drawn next covers the inner half.
            Geometry? letters = text.BuildGeometry(default);
            if (letters is not null)
            {
                context.DrawGeometry(null, new Pen(outline, reach * 2, lineJoin: PenLineJoin.Round), letters);
            }
        }

        context.DrawText(text, default);
    }

    private FormattedText Format()
    {
        return new FormattedText(
            Text ?? string.Empty,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle.Normal, FontWeight),
            FontSize,
            Foreground ?? Brushes.Black);
    }
}
