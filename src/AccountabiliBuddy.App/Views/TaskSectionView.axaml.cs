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
/// One section of a task panel. Code-behind: the header's right-click menu, the "+" box taking the keyboard as it opens (and closing
/// when left empty), dragging a row by its handle within this section or into another one on the page, Alt+↑/↓ (the keyboard stays on
/// the moved row), and Ctrl+A / Esc for the selection.
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

    public TaskSectionView()
    {
        InitializeComponent();
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

    private void OnAddBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && sender is TextBox { IsVisible: true } box)
        {
            Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Loaded);
        }
    }

    // Leaving the box with nothing typed closes it; typed text stays until Enter or Esc.
    private void OnAddBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TaskSectionViewModel { IsAdding: true } section && section.AddText.Trim().Length == 0)
        {
            section.CancelAddCommand.Execute(null);
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
