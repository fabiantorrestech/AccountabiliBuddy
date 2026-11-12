using System.Collections.Specialized;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// One section of a task panel. Code-behind: the header's right-click menu, dragging a row by its handle within this section or into
/// another one on the page, Alt+↑/↓ (the keyboard stays on the moved row), Ctrl+A / Esc for the selection, and the rows' roving focus
/// (13k): the rows are one Tab stop that remembers the row last used, ↑/↓ move between them and Shift+↑/↓ extend the selection.
/// </summary>
public partial class TaskSectionView : UserControl
{
    // How far the pointer moves on a drag handle before it's a drag, not a click.
    private const double DragThreshold = 4;

    private TaskListRowViewModel? _dragRow;
    private Point _dragStart;
    private bool _isDragging;
    private DropPlace? _drop;
    private TaskSectionViewModel? _subscribed;
    private TaskRowKey? _refocus;

    // The place of the row that had the keyboard last: when that row leaves (deleted, ticked away) while the keyboard was being used on
    // it, the keyboard goes to the row now there.
    private int _activeIndex = -1;
    private bool _activeByKeyboard;

    public TaskSectionView()
    {
        InitializeComponent();
        // Tab comes back to the row that had the keyboard last (a click on a row counts too).
        RowsList.AddHandler(GotFocusEvent, OnRowsGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    // ---- Roving focus (13k) ----

    private void OnRowsGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (RowButtonOf(e.Source as Visual) is { } button)
        {
            KeyboardNavigation.SetTabOnceActiveElement(RowsList, button);
            _activeIndex = RowButtons().ToList().IndexOf(button);
            _activeByKeyboard = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
        }
    }

    /// <summary>↑/↓: the keyboard goes to the row above or below in this section (it stops at the ends). False when there is none.</summary>
    internal bool FocusNeighbour(TaskListRowViewModel row, int step)
    {
        if (Neighbour(row, step) is not { } button)
        {
            return false;
        }

        button.Focus(NavigationMethod.Directional);
        button.BringIntoView();
        return true;
    }

    /// <summary>
    /// Shift+↑/↓: the selection grows (or shrinks) to the row above or below, from the row it started at, as Shift+click does; the keyboard
    /// moves with it. A focused step's row is never selected: the keyboard only moves.
    /// </summary>
    internal void ExtendByKey(TaskListRowViewModel row, int step)
    {
        if (Neighbour(row, step) is not { DataContext: TaskListRowViewModel next } button || DataContext is not TaskSectionViewModel section)
        {
            return;
        }

        if (row.OwnerKey is null && next.OwnerKey is null)
        {
            if (!row.IsPicked)
            {
                section.Click(row, TaskRowClick.Toggle);
            }

            section.Click(next, TaskRowClick.Range);
        }

        button.Focus(NavigationMethod.Directional);
        button.BringIntoView();
    }

    // The row button <paramref name="step"/> places away from the row's, skipping rows that are folding away.
    private Button? Neighbour(TaskListRowViewModel row, int step)
    {
        List<Button> buttons = RowButtons().Where(button => button.DataContext is TaskListRowViewModel { IsCollapsing: false } || button.DataContext == row).ToList();
        int index = buttons.FindIndex(button => ReferenceEquals(button.DataContext, row));
        int to = index + step;
        return index < 0 || to < 0 || to >= buttons.Count ? null : buttons[to];
    }

    private IEnumerable<Button> RowButtons()
    {
        return RowsList.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("listRow") && button.IsEffectivelyVisible);
    }

    private Button? RowButtonOf(Visual? visual)
    {
        for (Visual? current = visual; current is not null && !ReferenceEquals(current, RowsList); current = current.GetVisualParent())
        {
            if (current is Button button && button.Classes.Contains("listRow"))
            {
                return button;
            }
        }

        return null;
    }

    // Right-click (or the menu key) on a header: Expand all, Collapse all and the Pinned open switches.
    private void OnHeaderContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control header && DataContext is TaskSectionViewModel section)
        {
            TaskMenus.ShowEntries(header, section.HeaderMenu(), PlacementMode.BottomEdgeAlignedLeft);
            e.Handled = true;
        }
    }

    // Ctrl+A selects every row the panel shows; Esc clears the selection (when there is one; otherwise Esc is left to the page).
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || DataContext is not TaskSectionViewModel section || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control)
        {
            section.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && section.ClearSelection())
        {
            e.Handled = true;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.Rows.CollectionChanged -= OnRowsChanged;
        }

        _subscribed = DataContext as TaskSectionViewModel;
        if (_subscribed is not null)
        {
            _subscribed.Rows.CollectionChanged += OnRowsChanged;
        }
    }

    // ---- Alt+↑/↓: the row moves one place in this section; the keyboard stays on it after the page is rebuilt. ----

    internal void MoveByKey(TaskListRowViewModel row, int step)
    {
        if (DataContext is not TaskSectionViewModel section)
        {
            return;
        }

        int index = section.Rows.IndexOf(row);
        int to = index + step;
        if (index < 0 || to < 0 || to >= section.Rows.Count)
        {
            return;
        }

        TaskListRowViewModel target = section.Rows[to];
        bool after = step > 0;
        Guid? nextId = after && to + 1 < section.Rows.Count ? section.Rows[to + 1].Id : null;
        _refocus = row.Key;
        _ = section.DropAsync(new TaskRowDrop(row, section, section, target, after, nextId));
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // The row Tab returns to left the section: Tab lands on the first row again. When it had the keyboard (nothing has it now),
        // the row now in its place takes it, so Del, Space or ↑/↓ can go on from there.
        Dispatcher.UIThread.Post(() =>
        {
            if (KeyboardNavigation.GetTabOnceActiveElement(RowsList) is not Visual active || RowsList.IsVisualAncestorOf(active))
            {
                return;
            }

            KeyboardNavigation.SetTabOnceActiveElement(RowsList, null);
            bool keyboardFell = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is null or TopLevel;
            List<Button> buttons = RowButtons().ToList();
            if (keyboardFell && _activeByKeyboard && _activeIndex >= 0 && buttons.Count > 0)
            {
                Button next = buttons[Math.Min(_activeIndex, buttons.Count - 1)];
                next.Focus(NavigationMethod.Directional);
            }
        }, DispatcherPriority.Loaded);

        if (_refocus is not TaskRowKey key)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Button? button = this.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(candidate => candidate.Classes.Contains("listRow") && candidate.DataContext is TaskListRowViewModel row && row.Key == key);
            if (button is not null)
            {
                _refocus = null;
                button.Focus(NavigationMethod.Tab);
            }
        }, DispatcherPriority.Loaded);
    }

    // ---- Drag by the handle: pressing it takes the pointer; the drag starts once it has moved a few pixels. The row can land next
    // to a row of this section or of any other section on the page, or at the end of a section; the page decides what that means. ----

    internal void HandlePressed(Control handle, PointerPressedEventArgs e)
    {
        if (handle.DataContext is not TaskListRowViewModel row || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed
            || DataContext is not TaskSectionViewModel)
        {
            return;
        }

        _dragRow = row;
        _dragStart = e.GetPosition(this);
        _isDragging = false;
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    internal void HandleMoved(PointerEventArgs e)
    {
        if (_dragRow is null)
        {
            return;
        }

        if (!_isDragging)
        {
            Point moved = e.GetPosition(this) - _dragStart;
            if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold)
            {
                return;
            }

            _isDragging = true;
        }

        ShowDropTarget(FindDropTarget(e));
        e.Handled = true;
    }

    internal void HandleReleased(PointerReleasedEventArgs e)
    {
        if (_dragRow is not null && _isDragging && _drop is { } drop && DataContext is TaskSectionViewModel from)
        {
            TaskListRowViewModel? target = drop.Row;
            Guid? nextId = null;
            if (target is not null && drop.After)
            {
                int at = drop.Section.Rows.IndexOf(target);
                nextId = at >= 0 && at + 1 < drop.Section.Rows.Count ? drop.Section.Rows[at + 1].Id : null;
            }

            _ = from.DropAsync(new TaskRowDrop(_dragRow, from, drop.Section, target, drop.After, nextId));
        }

        EndDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    internal void EndDrag()
    {
        ShowDropTarget(null);
        _dragRow = null;
        _isDragging = false;
    }

    // Where the pointer is: next to a row (above or below it by its half), or the end of a section (its header or empty space).
    private DropPlace? FindDropTarget(PointerEventArgs e)
    {
        if (_dragRow is null || TopLevel.GetTopLevel(this) is not TopLevel top)
        {
            return null;
        }

        Point at = e.GetPosition(top);
        if (top.InputHitTest(at) is not Visual hit)
        {
            return null;
        }

        Control? item = null;
        foreach (Visual visual in hit.GetSelfAndVisualAncestors())
        {
            if (item is null && visual is Control { DataContext: TaskListRowViewModel } candidate && candidate.Classes.Contains("listItem"))
            {
                item = candidate;
            }

            if (visual is not TaskSectionView { DataContext: TaskSectionViewModel section })
            {
                continue;
            }

            if (item is { DataContext: TaskListRowViewModel row })
            {
                if (row == _dragRow)
                {
                    return null;
                }

                Point? local = top.TranslatePoint(at, item);
                return local is Point point ? new DropPlace(section, row, point.Y > item.Bounds.Height / 2) : null;
            }

            return new DropPlace(section, null, true);
        }

        return null;
    }

    private void ShowDropTarget(DropPlace? place)
    {
        if (_drop is { } old)
        {
            if (old.Row is not null)
            {
                old.Row.DropMark = 0;
            }

            old.Section.IsDropTarget = false;
        }

        _drop = place;
        if (place is null)
        {
            return;
        }

        if (place.Row is not null)
        {
            place.Row.DropMark = place.After ? 1 : -1;
        }
        else
        {
            place.Section.IsDropTarget = true;
        }
    }

    private sealed record DropPlace(TaskSectionViewModel Section, TaskListRowViewModel? Row, bool After);
}
