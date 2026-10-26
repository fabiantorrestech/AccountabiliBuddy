using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

public partial class ChecklistView : UserControl
{
    // How far the pointer moves on a drag handle before it's a drag, not a click.
    private const double DragThreshold = 4;

    private ChecklistViewModel? _subscribed;

    // A drag by a row's handle (⠿): the row, the task a dragged subtask belongs to, where it started, and where it would go.
    private ChecklistRowViewModel? _dragRow;
    private TaskRowViewModel? _dragOwner;
    private Point _dragStart;
    private bool _isDragging;
    private ChecklistRowViewModel? _dropTarget;
    private bool _dropAfter;

    public ChecklistView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.FocusEntryRequested -= OnFocusEntryRequested;
        }

        _subscribed = DataContext as ChecklistViewModel;
        if (_subscribed is not null)
        {
            _subscribed.FocusEntryRequested += OnFocusEntryRequested;
        }
    }

    // The start nudge: the keyboard goes to "Add a task" once the window shows.
    private void OnFocusEntryRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => NewTaskBox.Focus(), DispatcherPriority.Background);
    }

    // Rename: the box takes the keyboard as it opens, with the old name selected.
    private void OnRenameBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && sender is TextBox { IsVisible: true } box)
        {
            Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    // A category's name: clicking away keeps it, like Enter.
    private void OnCategoryNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: TaskCategoryRowViewModel row })
        {
            row.SaveNameCommand.Execute(null);
        }
    }

    // Clicking away keeps the new name, like Enter; Escape has already closed the box, so nothing is saved then.
    private void OnRenameBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ChecklistRowViewModel { IsEditing: true } row })
        {
            row.SaveEditCommand.Execute(null);
        }
    }

    // Drag to reorder: pressing a row's handle takes the pointer; the drag starts once it has moved a few pixels.
    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ChecklistRowViewModel row } handle || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragRow = row;
        _dragOwner = row is SubtaskRowViewModel ? OwnerTask(handle) : null;
        _dragStart = e.GetPosition(this);
        _isDragging = false;
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void OnHandleMoved(object? sender, PointerEventArgs e)
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

        // Between two rows (the lists' small gaps) the last place found stays; outside the checklist there is none.
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

    private void OnHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragRow is not null && _isDragging && _dropTarget is not null && DataContext is ChecklistViewModel checklist)
        {
            _ = checklist.MoveToAsync(_dragRow.Id, _dropTarget.Id, _dropAfter);
        }

        EndDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnHandleCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndDrag();
    }

    // The row under the pointer that the dragged one can go next to: another open task for a task (its whole block,
    // subtasks included), another subtask of the same task for a subtask; above or below by the pointer's half.
    private (ChecklistRowViewModel Row, bool After)? FindDropTarget(Point at)
    {
        if (_dragRow is null || this.InputHitTest(at) is not Visual hit)
        {
            return null;
        }

        string itemClass = _dragRow is TaskRowViewModel ? "taskItem" : "subtaskItem";
        foreach (Visual visual in hit.GetSelfAndVisualAncestors())
        {
            if (visual == this)
            {
                return null;
            }

            if (visual is not Control { DataContext: ChecklistRowViewModel row } item || !item.Classes.Contains(itemClass))
            {
                continue;
            }

            if (row == _dragRow || (_dragOwner is not null && OwnerTask(item) != _dragOwner))
            {
                return null;
            }

            Point? local = this.TranslatePoint(at, item);
            return local is Point point ? (row, point.Y > item.Bounds.Height / 2) : null;
        }

        return null;
    }

    private void ShowDropTarget(ChecklistRowViewModel? row, bool after)
    {
        if (_dropTarget is not null)
        {
            _dropTarget.IsDropAbove = _dropTarget.IsDropBelow = false;
        }

        _dropTarget = row;
        _dropAfter = after;
        if (row is not null)
        {
            row.IsDropAbove = !after;
            row.IsDropBelow = after;
        }
    }

    private void EndDrag()
    {
        ShowDropTarget(null, false);
        _dragRow = null;
        _dragOwner = null;
        _isDragging = false;
    }

    // The task a subtask row sits under.
    private static TaskRowViewModel? OwnerTask(Visual visual)
    {
        return visual.GetVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(control => control.Classes.Contains("taskItem"))?.DataContext as TaskRowViewModel;
    }
}
