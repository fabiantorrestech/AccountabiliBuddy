using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The +N menu's code-behind (Tasks redesign 4b.33-4b.35, keys): dragging a label row within its category, or a category by its heading,
/// and the keys. A press only becomes a drag once the pointer has moved <see cref="DragStart"/> DIP, so a click still picks the label. While
/// dragging, the row follows the pointer and the rows it passes make room; the drop asks the Shelf for the move (a stamped position edit
/// that syncs, and the label row follows). A label never leaves its own category here (Settings › Tasks › Labels does that).
/// Keys: ↑/↓ move between the rows and buttons, Enter/Space pick (the buttons' own), Alt+↑/↓ move the focused label or category one place,
/// Esc closes the menu (<see cref="Dismissed"/>, so the page can put the keyboard back on +N).
/// </summary>
public partial class LabelsMenuView : UserControl
{
    private const double DragStart = 4;

    private Control? _pressedRow;
    private Point _pressedAt;
    private DragState? _drag;

    public LabelsMenuView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnMenuKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Esc closed the menu from the keyboard.</summary>
    public event EventHandler? Dismissed;

    /// <summary>
    /// Puts the keyboard on the first label (or the first button) as the menu opens, so ↑/↓ work at once; its focus ring shows only when
    /// the menu was opened from the keyboard.
    /// </summary>
    public void FocusFirst(bool fromKeyboard)
    {
        Button? first = Rows().FirstOrDefault(row => row is ToggleButton) ?? Rows().FirstOrDefault();
        first?.Focus(fromKeyboard ? NavigationMethod.Directional : NavigationMethod.Pointer);
    }

    private ShelfViewModel? Shelf => DataContext as ShelfViewModel;

    // ---- Drag ----

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CancelDrag();
        _pressedRow = null;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is not Visual source)
        {
            return;
        }

        _pressedRow = source.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(IsDraggable);
        _pressedAt = e.GetPosition(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        Point at = e.GetPosition(this);
        if (_drag is null)
        {
            if (_pressedRow is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (Math.Abs(at.Y - _pressedAt.Y) < DragStart && Math.Abs(at.X - _pressedAt.X) < DragStart)
            {
                return;
            }

            _drag = DragState.Begin(_pressedRow);
            _pressedRow = null;
            if (_drag is null)
            {
                return;
            }

            // The menu takes the pointer: the row's button loses it, so letting go doesn't also pick the label.
            e.Pointer.Capture(this);
        }

        _drag.Follow(at.Y - _pressedAt.Y);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressedRow = null;
        if (_drag is not DragState drag)
        {
            return;
        }

        _drag = null;
        drag.Reset();
        e.Pointer.Capture(null);
        e.Handled = true;
        if (drag.To != drag.From)
        {
            _ = drag.IsGroup ? Shelf?.MoveGroupAsync(drag.Id, drag.To) : Shelf?.MoveLabelAsync(drag.Id, drag.To);
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // The row's button giving the pointer up as the drag begins is not a cancel; the menu losing it (the window lost focus) is.
        if (_drag is not null && !ReferenceEquals(e.Source, this))
        {
            return;
        }

        CancelDrag();
    }

    private void CancelDrag()
    {
        _drag?.Reset();
        _drag = null;
    }

    // A label row, or the heading of a real category ("Other labels" stays last).
    private static bool IsDraggable(Button button)
    {
        return button.DataContext switch
        {
            LabelFilterChipViewModel => button.Classes.Contains("lmLabel"),
            LabelFilterGroupViewModel group => group.CanMove && button.Classes.Contains("lmHead"),
            _ => false,
        };
    }

    // ---- Keys ----

    private void OnMenuKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelDrag();
            if (Shelf is { } shelf)
            {
                shelf.IsLabelsMenuOpen = false;
            }

            Dismissed?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Up or Key.Down))
        {
            return;
        }

        int step = e.Key == Key.Up ? -1 : 1;
        Button? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Button;
        if (e.KeyModifiers == KeyModifiers.Alt)
        {
            MoveFocused(focused, step);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        List<Button> rows = Rows();
        int at = focused is null ? -1 : rows.IndexOf(focused);
        int next = at < 0 ? (step > 0 ? 0 : rows.Count - 1) : Math.Clamp(at + step, 0, rows.Count - 1);
        if (next >= 0 && next < rows.Count)
        {
            rows[next].Focus(NavigationMethod.Directional);
            rows[next].BringIntoView();
        }

        e.Handled = true;
    }

    // Alt+↑/↓: the focused label (within its category) or category one place; the keyboard stays on it once the menu is rebuilt.
    private void MoveFocused(Button? focused, int step)
    {
        if (Shelf is not { } shelf || focused is null || !IsDraggable(focused))
        {
            return;
        }

        if (focused.DataContext is LabelFilterChipViewModel label)
        {
            _ = shelf.MoveLabelByAsync(label.Id, step);
            RefocusSoon(button => button.DataContext is LabelFilterChipViewModel item && item.Id == label.Id && button.Classes.Contains("lmLabel"));
        }
        else if (focused.DataContext is LabelFilterGroupViewModel { GroupId: Guid groupId })
        {
            _ = shelf.MoveGroupByAsync(groupId, step);
            RefocusSoon(button => button.DataContext is LabelFilterGroupViewModel item && item.GroupId == groupId && button.Classes.Contains("lmHead"));
        }
    }

    private void RefocusSoon(Func<Button, bool> match)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Button? target = Rows().FirstOrDefault(match);
            target?.Focus(NavigationMethod.Directional);
            target?.BringIntoView();
        }, DispatcherPriority.Background);
    }

    // Every button the keyboard can reach, top to bottom: the headings, the labels, No label, then the footer.
    private List<Button> Rows()
    {
        return this.GetVisualDescendants().OfType<Button>().Where(button => button.Focusable && button.IsEffectivelyVisible && button.IsEffectivelyEnabled).ToList();
    }

    /// <summary>
    /// One drag: the dragged item's container (a label row's, or a whole category's) follows the pointer up and down; the items it passes
    /// slide over by its height to make room. <see cref="To"/> is where it would land.
    /// </summary>
    private sealed class DragState
    {
        private readonly Control _item;
        private readonly IReadOnlyList<Control> _siblings;
        private readonly double _height;
        private readonly int _last;

        private DragState(Control item, IReadOnlyList<Control> siblings, int from, int last, Guid id, bool isGroup)
        {
            _item = item;
            _siblings = siblings;
            _height = item.Bounds.Height;
            _last = last;
            From = To = from;
            Id = id;
            IsGroup = isGroup;
        }

        public int From { get; }

        public int To { get; private set; }

        public Guid Id { get; }

        public bool IsGroup { get; }

        // The item container (ContentPresenter) holding the row (a label) or the heading (its whole category), among its siblings.
        public static DragState? Begin(Control row)
        {
            // The nearest item container whose visual parent is its ItemsControl's panel (a button's own presenter is below it, not above).
            ContentPresenter? container = row.GetVisualAncestors().OfType<ContentPresenter>()
                .FirstOrDefault(presenter => presenter.GetVisualParent() is Panel { IsItemsHost: true });
            if (container?.GetVisualParent() is not Panel panel)
            {
                return null;
            }

            List<Control> siblings = panel.Children.ToList();
            int from = siblings.IndexOf(container);
            int movable = row.DataContext is LabelFilterGroupViewModel
                ? siblings.Count(sibling => sibling.DataContext is LabelFilterGroupViewModel { CanMove: true })
                : siblings.Count;
            if (from < 0 || from >= movable)
            {
                return null;
            }

            return row.DataContext switch
            {
                LabelFilterChipViewModel label => new DragState(container, siblings, from, movable - 1, label.Id, isGroup: false),
                LabelFilterGroupViewModel { GroupId: Guid groupId } => new DragState(container, siblings, from, movable - 1, groupId, isGroup: true),
                _ => null,
            };
        }

        public void Follow(double dy)
        {
            // Never dragged past the first or last place it can take.
            double top = _siblings[0].Bounds.Top - _item.Bounds.Top;
            double bottom = _siblings[_last].Bounds.Bottom - _item.Bounds.Bottom;
            dy = Math.Clamp(dy, top, Math.Max(top, bottom));
            _item.RenderTransform = new TranslateTransform(0, dy);
            _item.ZIndex = 1;

            double center = _item.Bounds.Center.Y + dy;
            int to = From;
            // At or past a neighbour's middle takes its place (held at the first or last place, the pointer lands right on it).
            while (to < _last && _siblings[to + 1].Bounds.Center.Y <= center)
            {
                to++;
            }

            while (to > 0 && _siblings[to - 1].Bounds.Center.Y >= center)
            {
                to--;
            }

            To = to;
            for (int index = 0; index <= _last; index++)
            {
                if (index == From)
                {
                    continue;
                }

                double shift = index > From && index <= To ? -_height : index < From && index >= To ? _height : 0;
                _siblings[index].RenderTransform = shift == 0 ? null : new TranslateTransform(0, shift);
            }
        }

        public void Reset()
        {
            _item.ZIndex = 0;
            foreach (Control sibling in _siblings)
            {
                sibling.RenderTransform = null;
            }
        }
    }
}
