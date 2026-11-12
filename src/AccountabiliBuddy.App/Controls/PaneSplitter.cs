using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The one splitter between two panes (round 2, brief §4.4, change note §4a "Pane split"): drag to resize the pane before it; its grip
/// shows only on hover, keyboard focus or while dragging; a double-click puts the width back to <see cref="DefaultValue"/>; with
/// keyboard focus ← and → move it <see cref="KeyStep"/> px. <see cref="Value"/> stays within <see cref="Minimum"/> to
/// <see cref="Maximum"/>. A drag or a key starts from <see cref="ShownValue"/> (the width the pane really has, which a small window
/// may hold under <see cref="Value"/>). <see cref="ValueCommitted"/> fires once per change that should be remembered (a drag's end, a
/// key, a double-click), so the width is stored once, not on every pixel. Reusable for any two-pane page.
/// </summary>
public sealed class PaneSplitter : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<PaneSplitter, double>(nameof(Value), 470, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay, coerce: CoerceValue);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<PaneSplitter, double>(nameof(Minimum), 360);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<PaneSplitter, double>(nameof(Maximum), 560);

    public static readonly StyledProperty<double> DefaultValueProperty =
        AvaloniaProperty.Register<PaneSplitter, double>(nameof(DefaultValue), 470);

    public static readonly StyledProperty<double> KeyStepProperty =
        AvaloniaProperty.Register<PaneSplitter, double>(nameof(KeyStep), 10);

    /// <summary>The width the pane is shown at (set by its panel); a drag or a key starts from it. NaN = <see cref="Value"/>.</summary>
    public static readonly StyledProperty<double> ShownValueProperty =
        AvaloniaProperty.Register<PaneSplitter, double>(nameof(ShownValue), double.NaN);

    public static readonly StyledProperty<IBrush?> GripBrushProperty =
        AvaloniaProperty.Register<PaneSplitter, IBrush?>(nameof(GripBrush));

    private const double GripWidth = 4;
    private const double GripHeight = 36;

    private bool _dragging;
    private double _dragStartX;
    private double _dragStartValue;

    static PaneSplitter()
    {
        FocusableProperty.OverrideDefaultValue<PaneSplitter>(true);
        AffectsRender<PaneSplitter>(GripBrushProperty);
        MinimumProperty.Changed.AddClassHandler<PaneSplitter>((splitter, _) => splitter.CoerceValue(ValueProperty));
        MaximumProperty.Changed.AddClassHandler<PaneSplitter>((splitter, _) => splitter.CoerceValue(ValueProperty));
    }

    public PaneSplitter()
    {
        Background = Brushes.Transparent;
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double DefaultValue
    {
        get => GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    public double KeyStep
    {
        get => GetValue(KeyStepProperty);
        set => SetValue(KeyStepProperty, value);
    }

    public double ShownValue
    {
        get => GetValue(ShownValueProperty);
        set => SetValue(ShownValueProperty, value);
    }

    public IBrush? GripBrush
    {
        get => GetValue(GripBrushProperty);
        set => SetValue(GripBrushProperty, value);
    }

    /// <summary>A hit-testable fill (transparent by default) so the whole strip takes the pointer.</summary>
    public IBrush? Background { get; set; }

    /// <summary>A change to remember (a drag's end, a key, a double-click), with the new <see cref="Value"/>.</summary>
    public event EventHandler<double>? ValueCommitted;

    /// <summary>The width a key press or a drag moves from: what is shown, or <see cref="Value"/> when nothing says otherwise.</summary>
    private double StartValue => double.IsNaN(ShownValue) ? Value : ShownValue;

    public override void Render(DrawingContext context)
    {
        if (Background is not null)
        {
            context.DrawRectangle(Background, null, new Rect(Bounds.Size));
        }

        // The grip: only on hover, keyboard focus or while dragging (hover-only controls also show on keyboard focus, brief §3.12).
        if ((IsPointerOver || IsKeyboardFocusWithin || _dragging) && GripBrush is not null)
        {
            var grip = new Rect((Bounds.Width - GripWidth) / 2, (Bounds.Height - GripHeight) / 2, GripWidth, GripHeight);
            context.DrawRectangle(GripBrush, null, new RoundedRect(grip, GripWidth / 2));
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Cursor ??= new Cursor(StandardCursorType.SizeWestEast);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        InvalidateVisual();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            Reset();
            e.Handled = true;
            return;
        }

        _dragging = true;
        _dragStartX = e.GetPosition(Parent as Visual ?? this).X;
        _dragStartValue = StartValue;
        e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
        {
            return;
        }

        double moved = e.GetPosition(Parent as Visual ?? this).X - _dragStartX;
        Value = Math.Round(_dragStartValue + moved);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
        ValueCommitted?.Invoke(this, Value);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_dragging)
        {
            _dragging = false;
            InvalidateVisual();
            ValueCommitted?.Invoke(this, Value);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = KeyDelta(e.Key, KeyStep);
        if (step == 0)
        {
            return;
        }

        Value = StartValue + step;
        ValueCommitted?.Invoke(this, Value);
        e.Handled = true;
    }

    /// <summary>Back to <see cref="DefaultValue"/> (a double-click).</summary>
    public void Reset()
    {
        Value = DefaultValue;
        ValueCommitted?.Invoke(this, Value);
    }

    /// <summary>The rule on its own (also for the checks): a width kept within its limits; NaN is the default.</summary>
    public static double Clamp(double value, double minimum, double maximum, double fallback)
    {
        return double.IsNaN(value) ? fallback : Math.Clamp(value, minimum, Math.Max(minimum, maximum));
    }

    /// <summary>How far a key moves the splitter: ← and → by <paramref name="step"/>, anything else not at all.</summary>
    public static double KeyDelta(Key key, double step)
    {
        return key switch
        {
            Key.Left => -step,
            Key.Right => step,
            _ => 0,
        };
    }

    private static double CoerceValue(AvaloniaObject target, double value)
    {
        var splitter = (PaneSplitter)target;
        return Clamp(value, splitter.Minimum, splitter.Maximum, splitter.DefaultValue);
    }
}
