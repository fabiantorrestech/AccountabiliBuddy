using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Labels screen. Code-behind: clicking away from a label's or category's name keeps it, like Enter; the header's + and "+ Category" give
/// their name boxes the keyboard; and the moves (Tasks redesign 10.18, ADR 93). A press on a grip becomes a drag once the pointer has moved
/// <see cref="DragStart"/> DIP: the row (or the category) rests faint, an accent line shows where it lands, and the list scrolls near its
/// edges. A label lands in the category under the pointer (its heading or its labels; "Other labels" takes it out of any), a category among
/// the categories ("Other labels" stays last). The drop asks the view model for the move, a stamped edit that syncs. Alt+Up/Down on a grip
/// moves it one place from the keyboard (a label past its category's edge goes into the next one). Esc or losing the pointer cancels.
/// </summary>
public partial class LabelsView : UserControl
{
    private const double DragStart = 4;
    private const double EdgeScroll = 36;
    private const double ScrollStep = 14;

    private LabelsViewModel? _shown;
    private Button? _pressedGrip;
    private Point _pressedAt;
    private Drag? _drag;

    public LabelsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch(DataContext as LabelsViewModel);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnGripKeyDown, RoutingStrategies.Tunnel);
    }

    private void Watch(LabelsViewModel? labels)
    {
        if (_shown is not null)
        {
            _shown.FocusNewRequested -= OnFocusNewRequested;
            _shown.FocusNewCategoryRequested -= OnFocusNewCategoryRequested;
        }

        _shown = labels;
        if (labels is not null)
        {
            labels.FocusNewRequested += OnFocusNewRequested;
            labels.FocusNewCategoryRequested += OnFocusNewCategoryRequested;
        }
    }

    private void OnFocusNewRequested(object? sender, EventArgs e)
    {
        NewNameBox.Focus();
    }

    private void OnFocusNewCategoryRequested(object? sender, EventArgs e)
    {
        // The row shows first (its IsVisible follows the view model), then takes the keyboard.
        Dispatcher.UIThread.Post(() =>
        {
            NewCategoryBox.Focus();
            NewCategoryBox.BringIntoView();
        }, DispatcherPriority.Input);
    }

    private void OnNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: LabelRowViewModel row })
        {
            row.SaveNameCommand.Execute(null);
        }
    }

    private void OnCategoryNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: LabelCategorySectionViewModel section })
        {
            section.SaveNameCommand.Execute(null);
        }
    }

    // ---- Drag ----

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CancelDrag();
        _pressedGrip = null;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is not Visual source)
        {
            return;
        }

        _pressedGrip = source.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(button => button.Classes.Contains("dragGrip"));
        _pressedAt = e.GetPosition(DropLayer);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        Point at = e.GetPosition(DropLayer);
        if (_drag is null)
        {
            if (_pressedGrip is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                || (Math.Abs(at.Y - _pressedAt.Y) < DragStart && Math.Abs(at.X - _pressedAt.X) < DragStart))
            {
                return;
            }

            _drag = Drag.Begin(_pressedGrip);
            _pressedGrip = null;
            if (_drag is null)
            {
                return;
            }

            // The screen takes the pointer: the grip loses it, so letting go doesn't also click it.
            e.Pointer.Capture(this);
        }

        ScrollNearEdges(at);
        ShowTarget(at.Y);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressedGrip = null;
        if (_drag is not Drag drag)
        {
            return;
        }

        e.Pointer.Capture(null);
        e.Handled = true;
        (LabelCategorySectionViewModel Section, int Index)? target = drag.Target;
        EndDrag();
        if (_shown is not { } labels || target is not { } landing)
        {
            return;
        }

        _ = drag.Row is { } row
            ? labels.MoveLabelAsync(row, landing.Section, landing.Index)
            : labels.MoveCategoryAsync(drag.Section, landing.Index);
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // The grip giving the pointer up as the drag begins is not a cancel; the screen losing it (the window lost focus) is.
        if (_drag is not null && !ReferenceEquals(e.Source, this))
        {
            return;
        }

        CancelDrag();
    }

    private void CancelDrag()
    {
        if (_drag is not null)
        {
            EndDrag();
        }
    }

    private void EndDrag()
    {
        _drag?.Source.Classes.Set("dragging", false);
        _drag = null;
        DropLine.IsVisible = false;
    }

    // Near the list's top or bottom edge the list scrolls, so a label can travel to a category off screen.
    private void ScrollNearEdges(Point at)
    {
        double height = ListScroll.Bounds.Height;
        Point top = ListScroll.TranslatePoint(default, DropLayer) ?? default;
        double y = at.Y - top.Y;
        double step = y < EdgeScroll ? -ScrollStep : y > height - EdgeScroll ? ScrollStep : 0;
        if (step != 0)
        {
            double max = Math.Max(0, ListScroll.Extent.Height - ListScroll.Viewport.Height);
            ListScroll.Offset = ListScroll.Offset.WithY(Math.Clamp(ListScroll.Offset.Y + step, 0, max));
        }
    }

    // Where the pointer would drop the dragged label or category, with the accent line there.
    private void ShowTarget(double y)
    {
        if (_drag is not Drag drag)
        {
            return;
        }

        List<(LabelCategorySectionViewModel Section, Rect Block, Rect Head, List<(LabelRowViewModel Row, Rect Bounds)> Rows)> sections = Blocks();
        if (sections.Count == 0)
        {
            return;
        }

        double lineY;
        if (drag.Row is { } dragged)
        {
            // The section under the pointer (above the first: the first; below the last: the last).
            var section = sections.FirstOrDefault(entry => y < entry.Block.Bottom);
            if (section.Section is null)
            {
                section = sections[^1];
            }

            List<(LabelRowViewModel Row, Rect Bounds)> rows = section.Rows.Where(entry => !ReferenceEquals(entry.Row, dragged)).ToList();
            int index = rows.Count(entry => entry.Bounds.Center.Y < y);
            if (section.Head.Height > 0 && y < section.Head.Bottom)
            {
                index = 0;
            }

            lineY = index < rows.Count ? rows[index].Bounds.Top
                : rows.Count > 0 ? rows[^1].Bounds.Bottom
                : section.Head.Height > 0 ? section.Head.Bottom : section.Block.Top;
            drag.Target = (section.Section, index);
            Place(section.Block, lineY);
        }
        else
        {
            var categories = sections.Where(entry => entry.Section.IsCategory && !ReferenceEquals(entry.Section, drag.Section)).ToList();
            int index = categories.Count(entry => entry.Block.Center.Y < y);
            Rect anyBlock = sections[0].Block;
            lineY = index < categories.Count ? categories[index].Block.Top
                : categories.Count > 0 ? categories[^1].Block.Bottom
                : sections[0].Block.Top;
            drag.Target = (drag.Section, index);
            Place(anyBlock, lineY);
        }
    }

    private void Place(Rect block, double y)
    {
        Canvas.SetLeft(DropLine, block.Left + 6);
        Canvas.SetTop(DropLine, y - 1);
        DropLine.Width = Math.Max(0, block.Width - 12);
        DropLine.IsVisible = true;
    }

    // Each section's block, heading and label rows, in the drop layer's coordinates, in the order shown.
    private List<(LabelCategorySectionViewModel Section, Rect Block, Rect Head, List<(LabelRowViewModel Row, Rect Bounds)> Rows)> Blocks()
    {
        var result = new List<(LabelCategorySectionViewModel, Rect, Rect, List<(LabelRowViewModel, Rect)>)>();
        foreach (Border block in SectionList.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("sectionBlock")))
        {
            if (block.DataContext is not LabelCategorySectionViewModel section || !block.IsEffectivelyVisible)
            {
                continue;
            }

            Rect head = default;
            var rows = new List<(LabelRowViewModel, Rect)>();
            foreach (Border border in block.GetVisualDescendants().OfType<Border>())
            {
                if (border.Classes.Contains("sectionHead") && border.IsEffectivelyVisible)
                {
                    head = BoundsOf(border);
                }
                else if (border.Classes.Contains("labelRow") && border.DataContext is LabelRowViewModel row)
                {
                    rows.Add((row, BoundsOf(border)));
                }
            }

            result.Add((section, BoundsOf(block), head, rows));
        }

        return result;
    }

    private Rect BoundsOf(Visual visual)
    {
        Point? at = visual.TranslatePoint(default, DropLayer);
        return at is Point point ? new Rect(point, visual.Bounds.Size) : default;
    }

    // ---- Keys: Alt+Up/Down on a grip ----

    private async void OnGripKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _drag is not null)
        {
            CancelDrag();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Up or Key.Down) || _shown is not { } labels
            || e.Source is not Visual source || source.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(IsGrip) is not { } grip)
        {
            return;
        }

        e.Handled = true;
        int step = e.Key == Key.Up ? -1 : 1;
        object? moved = grip.DataContext;
        if (moved is LabelRowViewModel row)
        {
            await labels.MoveLabelByAsync(row, step);
        }
        else if (moved is LabelCategorySectionViewModel section)
        {
            await labels.MoveCategoryByAsync(section, step);
        }

        // The row may have a new container now (another category): the keyboard follows it.
        Dispatcher.UIThread.Post(() =>
        {
            Button? again = SectionList.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => IsGrip(button) && ReferenceEquals(button.DataContext, moved));
            again?.Focus(NavigationMethod.Directional);
            again?.BringIntoView();
        }, DispatcherPriority.Background);
    }

    private static bool IsGrip(Button button) => button.Classes.Contains("dragGrip");

    /// <summary>One drag: a label row (<see cref="Row"/>) or a category (<see cref="Section"/>), its faint source, and where it would land.</summary>
    private sealed class Drag
    {
        private Drag(Border source, LabelRowViewModel? row, LabelCategorySectionViewModel section)
        {
            Source = source;
            Row = row;
            Section = section;
        }

        public Border Source { get; }

        public LabelRowViewModel? Row { get; }

        public LabelCategorySectionViewModel Section { get; }

        public (LabelCategorySectionViewModel Section, int Index)? Target { get; set; }

        public static Drag? Begin(Button grip)
        {
            Border? block = grip.GetVisualAncestors().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("sectionBlock"));
            if (block?.DataContext is not LabelCategorySectionViewModel section)
            {
                return null;
            }

            Drag drag;
            if (grip.DataContext is LabelRowViewModel row)
            {
                Border? rowBorder = grip.GetVisualAncestors().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("labelRow"));
                if (rowBorder is null)
                {
                    return null;
                }

                drag = new Drag(rowBorder, row, section);
            }
            else if (section.IsCategory)
            {
                drag = new Drag(block, null, section);
            }
            else
            {
                return null;
            }

            drag.Source.Classes.Set("dragging", true);
            return drag;
        }
    }
}
