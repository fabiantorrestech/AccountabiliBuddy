using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The title bar's two groups (round 2, brief §3.2; owner 2026-11-14): the <see cref="TitleBarSlot.Start"/> children stack from the
/// left edge (back / forward, the app name, the noise control) and the <see cref="TitleBarSlot.End"/> children stack leftwards from
/// the right edge, last one rightmost (the session control, then synced devices │ light/dark │ Search │ the caption buttons). The end
/// children take their full width first, so in a narrow window the start group is the part that gives way (its own
/// <see cref="GiveWayPanel"/> order: the sound name, then the app name). Only when the start group is down to its smallest width
/// does an end child that keeps room on its left (<see cref="ITitleBarLeftReserve"/>: the session control's extras) get less width
/// than it asked for; it takes the cut out of that room, so it never overlaps the start group. Each child is laid out at its own
/// desired size, vertically centred.
/// </summary>
public sealed class TitleBarLayout : Panel
{
    public static readonly AttachedProperty<TitleBarSlot> SlotProperty =
        AvaloniaProperty.RegisterAttached<TitleBarLayout, Control, TitleBarSlot>("Slot");

    /// <summary>
    /// An end child that gets compact (it gains the style class "compact") before an end child that keeps room on its left (the session
    /// control) would be cut into its always-shown part: the lunch chip drops its words and keeps its icon, short time and ✕.
    /// </summary>
    public static readonly AttachedProperty<bool> CompactsFirstProperty =
        AvaloniaProperty.RegisterAttached<TitleBarLayout, Control, bool>("CompactsFirst");

    public const string CompactClass = "compact";

    // The width each compacting child has when it is not compact; kept while it is compact, so the decision is stable (no flip-flop between passes).
    private readonly Dictionary<Control, double> _fullWidth = [];

    static TitleBarLayout()
    {
        AffectsParentMeasure<TitleBarLayout>(SlotProperty, CompactsFirstProperty);
    }

    public static bool GetCompactsFirst(Control control) => control.GetValue(CompactsFirstProperty);

    public static void SetCompactsFirst(Control control, bool value) => control.SetValue(CompactsFirstProperty, value);

    public static TitleBarSlot GetSlot(Control control) => control.GetValue(SlotProperty);

    public static void SetSlot(Control control, TitleBarSlot slot) => control.SetValue(SlotProperty, slot);

    protected override Size MeasureOverride(Size availableSize)
    {
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        bool bounded = !double.IsInfinity(availableSize.Width);
        double fixedEnd = 0;
        double fixedEndFull = 0;
        double height = 0;
        var reserving = new List<Control>();
        var compacting = new List<Control>();
        foreach (Control child in Children)
        {
            if (GetSlot(child) != TitleBarSlot.End)
            {
                continue;
            }

            child.Measure(unbounded);
            height = Math.Max(height, child.DesiredSize.Height);
            if (child is ITitleBarLeftReserve)
            {
                reserving.Add(child);
            }
            else
            {
                fixedEnd += child.DesiredSize.Width;
                if (GetCompactsFirst(child))
                {
                    compacting.Add(child);
                    if (!child.Classes.Contains(CompactClass))
                    {
                        _fullWidth[child] = child.DesiredSize.Width;
                    }

                    fixedEndFull += _fullWidth.GetValueOrDefault(child, child.DesiredSize.Width);
                }
                else
                {
                    fixedEndFull += child.DesiredSize.Width;
                }
            }
        }

        // The start group at its smallest: a give-way row with every give-way child down to its floor, anything else as it asks.
        // (A measure with no room can't tell: a control's desired size never exceeds the room it was given.)
        double startMinimum = 0;
        foreach (Control child in Children)
        {
            if (GetSlot(child) == TitleBarSlot.Start)
            {
                child.Measure(unbounded);
                startMinimum += child is GiveWayPanel row
                    ? row.SmallestWidth + row.Margin.Left + row.Margin.Right
                    : child.DesiredSize.Width;
            }
        }

        // Compact first: when, with the compacting children at full width, a reserving child could not keep its always-shown part
        // (its width without the room kept on its left), they go compact and take that room back for it.
        if (bounded && compacting.Count > 0)
        {
            double needed = reserving.Sum(child => child.DesiredSize.Width - (child as ITitleBarLeftReserve)!.LeftReserve);
            bool tight = needed > availableSize.Width - fixedEndFull - startMinimum;
            foreach (Control child in compacting)
            {
                if (child.Classes.Contains(CompactClass) == tight)
                {
                    continue;
                }

                if (tight)
                {
                    child.Classes.Add(CompactClass);
                }
                else
                {
                    child.Classes.Remove(CompactClass);
                }

                fixedEnd -= child.DesiredSize.Width;
                child.Measure(unbounded);
                fixedEnd += child.DesiredSize.Width;
                if (!tight)
                {
                    _fullWidth[child] = child.DesiredSize.Width;
                }
            }
        }

        // What the reserving end children may take without pushing the start group below its smallest.
        double reserveRoom = bounded ? Math.Max(0, availableSize.Width - fixedEnd - startMinimum) : double.PositiveInfinity;
        double end = fixedEnd;
        foreach (Control child in reserving)
        {
            if (child.DesiredSize.Width > reserveRoom)
            {
                child.Measure(new Size(reserveRoom, availableSize.Height));
            }

            reserveRoom = Math.Max(0, reserveRoom - child.DesiredSize.Width);
            end += child.DesiredSize.Width;
        }

        double startRoom = bounded ? Math.Max(0, availableSize.Width - end) : double.PositiveInfinity;
        double width = end;
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

        double desiredWidth = bounded ? availableSize.Width : width;
        double desiredHeight = double.IsInfinity(availableSize.Height) ? height : availableSize.Height;
        return new Size(desiredWidth, desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double startRight = 0;
        foreach (Control child in Children)
        {
            if (GetSlot(child) == TitleBarSlot.Start)
            {
                child.Arrange(Centred(startRight, child.DesiredSize, finalSize.Height));
                startRight += child.DesiredSize.Width;
            }
        }

        // End children stack leftwards from the right edge, last one rightmost.
        double endLeft = finalSize.Width;
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            Control child = Children[i];
            if (GetSlot(child) == TitleBarSlot.End)
            {
                endLeft -= child.DesiredSize.Width;
                child.Arrange(Centred(endLeft, child.DesiredSize, finalSize.Height));
            }
        }

        return finalSize;
    }

    private static Rect Centred(double left, Size desired, double height)
    {
        double childHeight = Math.Min(desired.Height, height);
        return new Rect(left, (height - childHeight) / 2, desired.Width, childHeight);
    }
}
