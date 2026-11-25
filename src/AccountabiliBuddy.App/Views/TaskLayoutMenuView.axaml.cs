using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// Options › Layout's code-behind (0.1.154): dragging a row up or down, and the keys. As in the +N menu (<see cref="LabelsMenuView"/>), a
/// press becomes a drag only once the pointer has moved <see cref="DragStart"/> DIP (so the fold toggle still clicks); the row follows the
/// pointer, the rows it passes slide aside, and the drop asks for the move (<see cref="TaskLayoutMenuViewModel.MoveOntoAsync"/>, this
/// PC's order; the page follows at once). When a drag ends the panel takes the keyboard back from the row it was pressed on (0.1.153: a
/// drag is not a click). Keys: ↑/↓ walk the rows, Alt+↑/↓ move the focused row, Enter/Space fold or open it, Esc closes.
/// </summary>
public partial class TaskLayoutMenuView : UserControl
{
    private const double DragStart = 4;

    private Border? _pressedRow;
    private Point _pressedAt;
    private DragState? _drag;

    public TaskLayoutMenuView()
    {
        InitializeComponent();
        Card.Focusable = true;
        Card.FocusAdorner = null;
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnMenuKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Esc closed the panel from the keyboard: the page puts the keyboard back on its Options button.</summary>
    public event EventHandler? Dismissed;

    /// <summary>The header's ‹: the page closes this panel and opens Options again.</summary>
    public event EventHandler? BackRequested;

    private TaskLayoutMenuViewModel? Menu => DataContext as TaskLayoutMenuViewModel;

    /// <summary>
    /// The panel takes the keyboard as it opens, so ↓ and Esc work at once: onto the first row when opened from the keyboard, else onto the
    /// panel itself (no row looks picked until a key is pressed; ↓ then goes to the first row).
    /// </summary>
    public void FocusFirst(bool fromKeyboard)
    {
        Control? first = fromKeyboard ? Rows().FirstOrDefault() : null;
        if (first is not null)
        {
            first.Focus(NavigationMethod.Directional);
        }
        else
        {
            Card.Focus(NavigationMethod.Unspecified);
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        CancelDrag();
        BackRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    // ---- Drag ----

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CancelDrag();
        _pressedRow = null;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is not Visual source)
        {
            return;
        }

        // The fold toggle is a click, never a drag.
        if (source.GetSelfAndVisualAncestors().OfType<Button>().Any(button => button.Classes.Contains("lyFold")))
        {
            return;
        }

        _pressedRow = source.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("lyRow"));
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
        ReleasePressFocus(drag);
        if (drag.To != drag.From && Menu is { } menu && drag.To < menu.Rows.Count)
        {
            _ = menu.MoveOntoAsync(drag.Key, menu.Rows[drag.To].Key);
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_drag is not null && !ReferenceEquals(e.Source, this))
        {
            return;
        }

        CancelDrag();
    }

    private void CancelDrag()
    {
        if (_drag is { } drag)
        {
            drag.Reset();
            ReleasePressFocus(drag);
        }

        _drag = null;
    }

    // 0.1.153: the press that became the drag may have left the keyboard on its row; the panel takes it back (it stays in the panel, so
    // ↑/↓ and Esc still work).
    private void ReleasePressFocus(DragState drag)
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused && drag.Holds(focused))
        {
            Card.Focus(NavigationMethod.Unspecified);
        }
    }

    // ---- Keys ----

    private void OnMenuKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelDrag();
            if (Menu is { } menu)
            {
                menu.IsOpen = false;
            }

            Dismissed?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        Border? row = FocusedRow();
        if (e.Key is Key.Enter or Key.Space && e.KeyModifiers == KeyModifiers.None && row?.DataContext is TaskLayoutRowViewModel folding
            && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Border)
        {
            folding.ToggleFoldCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Up or Key.Down))
        {
            return;
        }

        int step = e.Key == Key.Up ? -1 : 1;
        if (e.KeyModifiers == KeyModifiers.Alt)
        {
            if (row?.DataContext is TaskLayoutRowViewModel moving && Menu is { } menu)
            {
                string key = moving.Key;
                _ = menu.MoveByAsync(key, step);
                RefocusSoon(key);
            }

            e.Handled = true;
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        List<Border> rows = Rows();
        int at = row is null ? -1 : rows.IndexOf(row);
        int next = at < 0 ? (step > 0 ? 0 : rows.Count - 1) : Math.Clamp(at + step, 0, rows.Count - 1);
        if (next >= 0 && next < rows.Count)
        {
            rows[next].Focus(NavigationMethod.Directional);
            rows[next].BringIntoView();
        }

        e.Handled = true;
    }

    // The row the keyboard is on (on the row itself or its fold toggle).
    private Border? FocusedRow()
    {
        return TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused
            ? focused.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("lyRow"))
            : null;
    }

    // After a move the rows are laid out again: the keyboard goes back to the moved one.
    private void RefocusSoon(string key)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Border? target = Rows().FirstOrDefault(row => row.DataContext is TaskLayoutRowViewModel item && item.Key == key);
            target?.Focus(NavigationMethod.Directional);
            target?.BringIntoView();
        }, DispatcherPriority.Background);
    }

    private List<Border> Rows()
    {
        return RowList.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("lyRow") && border.IsEffectivelyVisible).ToList();
    }

    /// <summary>
    /// One drag: the dragged row's container follows the pointer up and down; the rows it passes slide over by its height to make room.
    /// <see cref="To"/> is where it would land.
    /// </summary>
    private sealed class DragState
    {
        private readonly Control _item;
        private readonly Border _row;
        private readonly IReadOnlyList<Control> _siblings;
        private readonly double _height;

        private DragState(Control item, Border row, IReadOnlyList<Control> siblings, int from, string key)
        {
            _item = item;
            _row = row;
            _siblings = siblings;
            _height = item.Bounds.Height;
            From = To = from;
            Key = key;
        }

        public int From { get; }

        public int To { get; private set; }

        public string Key { get; }

        public static DragState? Begin(Border row)
        {
            ContentPresenter? container = row.GetVisualAncestors().OfType<ContentPresenter>()
                .FirstOrDefault(presenter => presenter.GetVisualParent() is Panel { IsItemsHost: true });
            if (container?.GetVisualParent() is not Panel panel || row.DataContext is not TaskLayoutRowViewModel item)
            {
                return null;
            }

            List<Control> siblings = panel.Children.ToList();
            int from = siblings.IndexOf(container);
            if (from < 0 || siblings.Count < 2)
            {
                return null;
            }

            row.Classes.Add("dragging");
            return new DragState(container, row, siblings, from, item.Key);
        }

        /// <summary>The keyboard is on the dragged row.</summary>
        public bool Holds(Visual focused) => focused.GetSelfAndVisualAncestors().Any(visual => ReferenceEquals(visual, _item));

        public void Follow(double dy)
        {
            int last = _siblings.Count - 1;
            double top = _siblings[0].Bounds.Top - _item.Bounds.Top;
            double bottom = _siblings[last].Bounds.Bottom - _item.Bounds.Bottom;
            dy = Math.Clamp(dy, top, Math.Max(top, bottom));
            _item.RenderTransform = new TranslateTransform(0, dy);
            _item.ZIndex = 1;

            double center = _item.Bounds.Center.Y + dy;
            int to = From;
            while (to < last && _siblings[to + 1].Bounds.Center.Y <= center)
            {
                to++;
            }

            while (to > 0 && _siblings[to - 1].Bounds.Center.Y >= center)
            {
                to--;
            }

            To = to;
            for (int index = 0; index <= last; index++)
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
            _row.Classes.Remove("dragging");
            _item.ZIndex = 0;
            foreach (Control sibling in _siblings)
            {
                sibling.RenderTransform = null;
            }
        }
    }
}
