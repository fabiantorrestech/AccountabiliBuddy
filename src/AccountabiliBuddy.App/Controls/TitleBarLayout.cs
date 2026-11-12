using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The title bar's three groups (round 2, brief §3.2): the <see cref="TitleBarSlot.Start"/> group at the left edge, the
/// <see cref="TitleBarSlot.End"/> group at the right edge, and the <see cref="TitleBarSlot.Center"/> child (the session control)
/// centred on the whole width, so neither side group moves it and it moves neither of them. A centre child that keeps room on its
/// left for something that opens there (<see cref="ITitleBarLeftReserve"/>: the session control's extras) is centred by the rest
/// of its width, and that room never overlaps the left group. In a narrow window the centre child slides toward whichever side has
/// room, and never overlaps the side groups' laid-out widths. Each child is laid out at its own desired size, vertically centred.
/// </summary>
public sealed class TitleBarLayout : Panel
{
    public static readonly AttachedProperty<TitleBarSlot> SlotProperty =
        AvaloniaProperty.RegisterAttached<TitleBarLayout, Control, TitleBarSlot>("Slot");

    static TitleBarLayout()
    {
        AffectsParentMeasure<TitleBarLayout>(SlotProperty);
    }

    public static TitleBarSlot GetSlot(Control control) => control.GetValue(SlotProperty);

    public static void SetSlot(Control control, TitleBarSlot slot) => control.SetValue(SlotProperty, slot);

    // The centre and end children take their full width; the start children get what is left, so in a narrow window the start
    // group (the noise control's sound name) is the part that gives way.
    protected override Size MeasureOverride(Size availableSize)
    {
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        double width = 0;
        double height = 0;
        foreach (Control child in Children)
        {
            if (GetSlot(child) != TitleBarSlot.Start)
            {
                child.Measure(unbounded);
                width += child.DesiredSize.Width;
                height = Math.Max(height, child.DesiredSize.Height);
            }
        }

        double startRoom = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - width);
        foreach (Control child in Children)
        {
            if (GetSlot(child) == TitleBarSlot.Start)
            {
                child.Measure(new Size(startRoom, availableSize.Height));
                startRoom = Math.Max(0, startRoom - child.DesiredSize.Width);
                width += child.DesiredSize.Width;
                height = Math.Max(height, child.DesiredSize.Height);
            }
        }

        double desiredWidth = double.IsInfinity(availableSize.Width) ? width : availableSize.Width;
        double desiredHeight = double.IsInfinity(availableSize.Height) ? height : availableSize.Height;
        return new Size(desiredWidth, desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double startRight = 0;
        double endLeft = finalSize.Width;

        foreach (Control child in Children)
        {
            TitleBarSlot slot = GetSlot(child);
            if (slot == TitleBarSlot.Start)
            {
                child.Arrange(Centred(startRight, child.DesiredSize, finalSize.Height));
                startRight += child.DesiredSize.Width;
            }
        }

        // End children stack leftwards from the right edge, last one rightmost.
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            Control child = Children[i];
            if (GetSlot(child) == TitleBarSlot.End)
            {
                endLeft -= child.DesiredSize.Width;
                child.Arrange(Centred(endLeft, child.DesiredSize, finalSize.Height));
            }
        }

        foreach (Control child in Children)
        {
            if (GetSlot(child) != TitleBarSlot.Center)
            {
                continue;
            }

            double width = child.DesiredSize.Width;
            double reserve = child is ITitleBarLeftReserve reserving ? Math.Min(reserving.LeftReserve, width) : 0;
            // The resting part is centred; the reserve sits to its left.
            double left = (finalSize.Width - (width - reserve)) / 2 - reserve;
            // Too little room in the middle: keep it clear of the right group first, then (reserve included) of the left group.
            left = Math.Min(left, endLeft - width);
            left = Math.Max(left, startRight);
            child.Arrange(Centred(left, child.DesiredSize, finalSize.Height));
        }

        return finalSize;
    }

    private static Rect Centred(double left, Size desired, double height)
    {
        double childHeight = Math.Min(desired.Height, height);
        return new Rect(left, (height - childHeight) / 2, desired.Width, childHeight);
    }
}
