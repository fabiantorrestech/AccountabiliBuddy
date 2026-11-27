using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The title bar's two groups (round 2, brief §3.2; owner 2026-11-14, 2026-11-27): the <see cref="TitleBarSlot.Start"/> children stack
/// from the left edge (back / forward, the app name and its mode badge) and the <see cref="TitleBarSlot.End"/> children stack leftwards
/// from the right edge, last one rightmost (the session control, the noise control, the title-bar menu's button and the caption buttons).
/// <para>
/// "Cemented" slots (owner 2026-11-27): an end child with a <see cref="CementedWidthProperty"/> gets a width that depends only on the
/// window's width, never on what it shows (Start session, a timer, Ended, a long sound name), and is arranged at that whole width, so
/// its content (right-aligned inside) changing never moves anything else. When the window is too narrow for every cemented slot at its
/// cemented width with <see cref="DragMinimum"/> of drag area left, the slots give way in <see cref="GiveWayOrderProperty"/> order (lower
/// first), each down to its <see cref="CementedMinWidthProperty"/>; only then does the start group give way (its own
/// <see cref="GiveWayPanel"/> order).
/// </para>
/// <para>
/// An end child marked <see cref="CompactsFirstProperty"/> (the lunch chip, only while lunch runs) takes what is left between the start
/// group and the cemented slots: it goes compact (style class "compact") when its full width doesn't fit, and is measured with what is
/// left, so it never moves the slots or the start group. Other end children (the menu button and the caption buttons) keep their own
/// size. Children are vertically centred.
/// </para>
/// </summary>
public sealed class TitleBarLayout : Panel
{
    /// <summary>The drag area kept free between the groups before any cemented slot or the start group gives way.</summary>
    public const double DragMinimum = 40;

    public const string CompactClass = "compact";

    public static readonly AttachedProperty<TitleBarSlot> SlotProperty =
        AvaloniaProperty.RegisterAttached<TitleBarLayout, Control, TitleBarSlot>("Slot");

    /// <summary>
    /// An end child that fills what is left once the cemented slots and the start group have their width; it gains the style class
    /// "compact" when its full width doesn't fit there: the lunch chip drops its words and keeps its icon, short time and ✕.
    /// </summary>
    public static readonly AttachedProperty<bool> CompactsFirstProperty =
        AvaloniaProperty.RegisterAttached<TitleBarLayout, Control, bool>("CompactsFirst");

    /// <summary>An end child's fixed slot width (margins included) when the window has room; NaN: not cemented (its own size).</summary>
    public static readonly AttachedProperty<double> CementedWidthProperty =
        AvaloniaProperty.RegisterAttached<TitleBarLayout, Control, double>("CementedWidth", double.NaN);

    /// <summary>The narrowest a cemented slot gets in a narrow window; NaN: it never gives way.</summary>
    public static readonly AttachedProperty<double> CementedMinWidthProperty =
        AvaloniaProperty.RegisterAttached<TitleBarLayout, Control, double>("CementedMinWidth", double.NaN);

    /// <summary>Which cemented slot gives way first in a narrow window: the lowest order first.</summary>
    public static readonly AttachedProperty<int> GiveWayOrderProperty =
        AvaloniaProperty.RegisterAttached<TitleBarLayout, Control, int>("GiveWayOrder");

    // The width each compacting child has when it is not compact; kept while it is compact, so the decision is stable (no flip-flop between passes).
    private readonly Dictionary<Control, double> _fullWidth = [];

    // The width each cemented slot was given in the last measure; arranged at exactly that width.
    private readonly Dictionary<Control, double> _slotWidth = [];

    static TitleBarLayout()
    {
        AffectsParentMeasure<TitleBarLayout>(SlotProperty, CompactsFirstProperty, CementedWidthProperty, CementedMinWidthProperty, GiveWayOrderProperty);
    }

    public static bool GetCompactsFirst(Control control) => control.GetValue(CompactsFirstProperty);

    public static void SetCompactsFirst(Control control, bool value) => control.SetValue(CompactsFirstProperty, value);

    public static TitleBarSlot GetSlot(Control control) => control.GetValue(SlotProperty);

    public static void SetSlot(Control control, TitleBarSlot slot) => control.SetValue(SlotProperty, slot);

    public static double GetCementedWidth(Control control) => control.GetValue(CementedWidthProperty);

    public static void SetCementedWidth(Control control, double width) => control.SetValue(CementedWidthProperty, width);

    public static double GetCementedMinWidth(Control control) => control.GetValue(CementedMinWidthProperty);

    public static void SetCementedMinWidth(Control control, double width) => control.SetValue(CementedMinWidthProperty, width);

    public static int GetGiveWayOrder(Control control) => control.GetValue(GiveWayOrderProperty);

    public static void SetGiveWayOrder(Control control, int order) => control.SetValue(GiveWayOrderProperty, order);

    private static bool IsCemented(Control child) => GetSlot(child) == TitleBarSlot.End && !double.IsNaN(GetCementedWidth(child));

    private static bool IsFiller(Control child) => GetSlot(child) == TitleBarSlot.End && !IsCemented(child) && GetCompactsFirst(child);

    protected override Size MeasureOverride(Size availableSize)
    {
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        bool bounded = !double.IsInfinity(availableSize.Width);
        double height = 0;

        // The end children that keep their own size (the menu button and the caption buttons).
        double fixedEnd = 0;
        foreach (Control child in Children)
        {
            if (GetSlot(child) == TitleBarSlot.End && !IsCemented(child) && !IsFiller(child))
            {
                child.Measure(unbounded);
                fixedEnd += child.DesiredSize.Width;
                height = Math.Max(height, child.DesiredSize.Height);
            }
        }

        // The start group at its natural width.
        double startNatural = 0;
        foreach (Control child in Children)
        {
            if (GetSlot(child) == TitleBarSlot.Start)
            {
                child.Measure(unbounded);
                startNatural += child.DesiredSize.Width;
            }
        }

        // The cemented slots: their width comes from the window's width alone (never from their content).
        List<Control> cemented = Children.Where(child => IsCemented(child) && child.IsVisible).ToList();
        double cementedTotal = bounded
            ? SizeSlots(cemented, availableSize.Width - fixedEnd - DragMinimum - startNatural)
            : SizeSlotsUnbounded(cemented);
        foreach (Control child in cemented)
        {
            child.Measure(new Size(_slotWidth[child], availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        foreach (Control child in Children.Where(child => IsCemented(child) && !child.IsVisible))
        {
            _slotWidth.Remove(child);
            child.Measure(unbounded);
        }

        // The start group gets what is left of the window less the drag area (its natural width whenever the slots fit beside it).
        double startRoom = bounded ? Math.Max(0, availableSize.Width - fixedEnd - cementedTotal - DragMinimum) : double.PositiveInfinity;
        double startUsed = 0;
        foreach (Control child in Children)
        {
            if (GetSlot(child) == TitleBarSlot.Start)
            {
                child.Measure(new Size(Math.Max(0, startRoom - startUsed), availableSize.Height));
                startUsed += child.DesiredSize.Width;
                height = Math.Max(height, child.DesiredSize.Height);
            }
        }

        // The filler (the lunch chip) takes the drag area's room; compact when its full width doesn't fit there.
        double fillerRoom = bounded ? Math.Max(0, availableSize.Width - fixedEnd - cementedTotal - startUsed) : double.PositiveInfinity;
        double fillerUsed = 0;
        foreach (Control child in Children.Where(IsFiller))
        {
            fillerUsed += MeasureFiller(child, Math.Max(0, fillerRoom - fillerUsed), availableSize.Height);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        double width = startUsed + fillerUsed + cementedTotal + fixedEnd;
        double desiredWidth = bounded ? availableSize.Width : width;
        double desiredHeight = double.IsInfinity(availableSize.Height) ? height : availableSize.Height;
        return new Size(desiredWidth, desiredHeight);
    }

    // Each slot at its cemented width if they all fit in the room beside the start group at its natural width; otherwise they give way
    // in order, each down to its minimum. If they still don't fit, the start group gives way next (it is measured with what is left).
    private double SizeSlots(List<Control> slots, double room)
    {
        double total = 0;
        foreach (Control slot in slots)
        {
            _slotWidth[slot] = GetCementedWidth(slot);
            total += _slotWidth[slot];
        }

        double deficit = total - Math.Max(0, room);
        foreach (Control slot in slots.OrderBy(GetGiveWayOrder))
        {
            if (deficit <= 0)
            {
                break;
            }

            double minimum = GetCementedMinWidth(slot);
            if (double.IsNaN(minimum))
            {
                continue;
            }

            double cut = Math.Min(deficit, Math.Max(0, _slotWidth[slot] - minimum));
            _slotWidth[slot] -= cut;
            deficit -= cut;
            total -= cut;
        }

        return total;
    }

    private double SizeSlotsUnbounded(List<Control> slots)
    {
        double total = 0;
        foreach (Control slot in slots)
        {
            _slotWidth[slot] = GetCementedWidth(slot);
            total += _slotWidth[slot];
        }

        return total;
    }

    // The lunch chip: full when its full width fits the room, compact otherwise; measured with the room, so it never overflows.
    private double MeasureFiller(Control child, double room, double height)
    {
        var unbounded = new Size(double.PositiveInfinity, height);
        child.Measure(unbounded);
        if (!child.Classes.Contains(CompactClass))
        {
            _fullWidth[child] = child.DesiredSize.Width;
        }

        bool tight = child.IsVisible && _fullWidth.GetValueOrDefault(child, child.DesiredSize.Width) > room;
        if (child.Classes.Contains(CompactClass) != tight)
        {
            if (tight)
            {
                child.Classes.Add(CompactClass);
            }
            else
            {
                child.Classes.Remove(CompactClass);
            }

            child.Measure(unbounded);
            if (!tight)
            {
                _fullWidth[child] = child.DesiredSize.Width;
            }
        }

        if (child.DesiredSize.Width > room)
        {
            child.Measure(new Size(room, height));
        }

        return child.DesiredSize.Width;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double startRight = 0;
        foreach (Control child in Children)
        {
            if (GetSlot(child) == TitleBarSlot.Start)
            {
                child.Arrange(Centred(startRight, child.DesiredSize.Width, child.DesiredSize.Height, finalSize.Height));
                startRight += child.DesiredSize.Width;
            }
        }

        // End children stack leftwards from the right edge, last one rightmost; a cemented slot at its whole slot width.
        double endLeft = finalSize.Width;
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            Control child = Children[i];
            if (GetSlot(child) == TitleBarSlot.End)
            {
                double width = _slotWidth.TryGetValue(child, out double slot) && child.IsVisible ? slot : child.DesiredSize.Width;
                endLeft -= width;
                child.Arrange(Centred(endLeft, width, child.DesiredSize.Height, finalSize.Height));
            }
        }

        return finalSize;
    }

    private static Rect Centred(double left, double width, double desiredHeight, double height)
    {
        double childHeight = Math.Min(desiredHeight, height);
        return new Rect(left, (height - childHeight) / 2, width, childHeight);
    }
}
