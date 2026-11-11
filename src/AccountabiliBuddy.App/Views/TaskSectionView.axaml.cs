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
/// when left empty), and dragging a row by its handle within the section (This session's order).
/// </summary>
public partial class TaskSectionView : UserControl
{
    // How far the pointer moves on a drag handle before it's a drag, not a click.
    private const double DragThreshold = 4;

    private TaskListRowViewModel? _dragRow;
    private Point _dragStart;
    private bool _isDragging;
    private TaskListRowViewModel? _dropTarget;
    private bool _dropAfter;

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

    // ---- Drag to reorder: pressing a row's handle takes the pointer; the drag starts once it has moved a few pixels. ----

    internal void HandlePressed(Control handle, PointerPressedEventArgs e)
    {
        if (handle.DataContext is not TaskListRowViewModel row || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed
            || DataContext is not TaskSectionViewModel { MoveRow: not null })
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

        Point at = e.GetPosition(this);
        if (!_isDragging)
        {
            Point moved = at - _dragStart;
            if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold)
            {
                return;
            }

            _isDragging = true;
        }

        // Between two rows the last place found stays; outside the section there is none.
        if (FindDropTarget(at) is { } found)
        {
            ShowDropTarget(found.Row, found.After);
        }
        else if (!new Rect(Bounds.Size).Contains(at))
        {
            ShowDropTarget(null, false);
        }

        e.Handled = true;
    }

    internal void HandleReleased(PointerReleasedEventArgs e)
    {
        if (_dragRow is not null && _isDragging && _dropTarget is not null && DataContext is TaskSectionViewModel { MoveRow: { } move })
        {
            _ = move(_dragRow.Id, _dropTarget.Id, _dropAfter);
        }

        EndDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    internal void EndDrag()
    {
        ShowDropTarget(null, false);
        _dragRow = null;
        _isDragging = false;
    }

    // The row under the pointer that the dragged one can go next to: above or below it by the pointer's half.
    private (TaskListRowViewModel Row, bool After)? FindDropTarget(Point at)
    {
        if (_dragRow is null || this.InputHitTest(at) is not Visual hit)
        {
            return null;
        }

        foreach (Visual visual in hit.GetSelfAndVisualAncestors())
        {
            if (visual == this)
            {
                return null;
            }

            if (visual is not Control { DataContext: TaskListRowViewModel row } item || !item.Classes.Contains("listItem") || !row.CanDrag)
            {
                continue;
            }

            if (row == _dragRow)
            {
                return null;
            }

            Point? local = this.TranslatePoint(at, item);
            return local is Point point ? (row, point.Y > item.Bounds.Height / 2) : null;
        }

        return null;
    }

    private void ShowDropTarget(TaskListRowViewModel? row, bool after)
    {
        if (_dropTarget is not null)
        {
            _dropTarget.DropMark = 0;
        }

        _dropTarget = row;
        _dropAfter = after;
        if (row is not null)
        {
            row.DropMark = after ? 1 : -1;
        }
    }
}
