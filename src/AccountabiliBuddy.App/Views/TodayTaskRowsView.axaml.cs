using System.ComponentModel;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// Today's numbered task rows. Code-behind: the keyboard for the add row, the row flyout's rename box, dragging a row by its
/// handle, and the flyout coming back after a step was added (the checklist rebuilds its rows on every change, which closes it).
/// </summary>
public partial class TodayTaskRowsView : UserControl
{
    // How far the pointer moves on a drag handle before it's a drag, not a click.
    private const double DragThreshold = 4;

    // A step added in a row's flyout rebuilds the rows; the flyout opens again on the same task within this time.
    private static readonly TimeSpan ReopenWithin = TimeSpan.FromSeconds(1.5);

    private ChecklistViewModel? _subscribed;

    private TaskRowViewModel? _dragRow;
    private Point _dragStart;
    private bool _isDragging;
    private TaskRowViewModel? _dropTarget;
    private bool _dropAfter;

    private Guid? _reopenId;
    private DateTime _reopenAt;

    public TodayTaskRowsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.FocusEntryRequested -= OnFocusEntryRequested;
            _subscribed.PropertyChanged -= OnChecklistPropertyChanged;
        }

        _subscribed = DataContext as ChecklistViewModel;
        if (_subscribed is not null)
        {
            _subscribed.FocusEntryRequested += OnFocusEntryRequested;
            _subscribed.PropertyChanged += OnChecklistPropertyChanged;
        }
    }

    // The start nudge: the keyboard goes to the add row once the window shows.
    private void OnFocusEntryRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => NewTaskBox.Focus(), DispatcherPriority.Background);
    }

    // The new-task category picker: fill the menu from the current choices every time it opens.
    private void OnNewTaskPickerOpening(object? sender, EventArgs e)
    {
        if (sender is MenuFlyout flyout && DataContext is ChecklistViewModel viewModel)
        {
            flyout.ItemsSource = viewModel.NewTaskCategoryChoices.ToList();
        }
    }

    // The row's flyout: its name box starts with the task's own name, and what is done inside it (a step added, a move, a
    // category) is noticed, so it can come back once the rows are rebuilt.
    private void OnEditFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Target.DataContext: TaskRowViewModel row } flyout)
        {
            return;
        }

        row.BeginEditCommand.Execute(null);
        if (flyout.Content is Control content)
        {
            content.RemoveHandler(PointerReleasedEvent, OnFlyoutPointerReleased);
            content.AddHandler(PointerReleasedEvent, OnFlyoutPointerReleased, RoutingStrategies.Tunnel);
        }
    }

    // Closing the flyout keeps a changed name, like Enter (Escape closes it too, and keeps what the box held).
    private void OnEditFlyoutClosed(object? sender, EventArgs e)
    {
        if (sender is Flyout { Target.DataContext: TaskRowViewModel { IsEditing: true } row })
        {
            row.SaveEditCommand.Execute(null);
        }
    }

    // Enter in a row's "Add a step" box: adds the step, and the flyout comes back once the rows are rebuilt.
    private void OnNewStepKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox { DataContext: TaskRowViewModel row, Text: { } text } && text.Trim().Length > 0)
        {
            WatchForRebuild(row);
            row.AddSubtaskCommand.Execute(null);
            e.Handled = true;
        }
    }

    // A button pressed inside the flyout (move, a step's ✕ or ↑↓, a category, "Show on bar"): the same.
    private void OnFlyoutPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Visual source
            && source.GetSelfAndVisualAncestors().OfType<Button>().Any() && sender is Control { DataContext: TaskRowViewModel row })
        {
            WatchForRebuild(row);
        }
    }

    private void WatchForRebuild(TaskRowViewModel row)
    {
        _reopenId = row.Id;
        _reopenAt = DateTime.UtcNow;
    }

    private void OnChecklistPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChecklistViewModel.OpenTasks) || _reopenId is not Guid id)
        {
            return;
        }

        _reopenId = null;
        if (DateTime.UtcNow - _reopenAt > ReopenWithin)
        {
            return;
        }

        // After layout: the new rows have buttons by then.
        Dispatcher.UIThread.Post(() =>
        {
            Button? button = this.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(candidate => candidate.Classes.Contains("taskRow") && candidate.DataContext is TaskRowViewModel row && row.Id == id);
            button?.Flyout?.ShowAt(button);
        }, DispatcherPriority.Background);
    }

    // A step's rename box takes the keyboard as it opens, with the old name selected.
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

    // Clicking away keeps the new name, like Enter; Escape has already closed the box, so nothing is saved then.
    private void OnRenameBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ChecklistRowViewModel { IsEditing: true } row })
        {
            row.SaveEditCommand.Execute(null);
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

    // Drag to reorder: pressing a row's handle takes the pointer; the drag starts once it has moved a few pixels.
    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: TaskRowViewModel row } handle || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragRow = row;
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

        // Between two rows (the small gap) the last place found stays; outside the list there is none.
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

    // The row under the pointer that the dragged one can go next to: above or below it by the pointer's half.
    private (TaskRowViewModel Row, bool After)? FindDropTarget(Point at)
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

            if (visual is not Control { DataContext: TaskRowViewModel row } item || !item.Classes.Contains("taskItem"))
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

    private void ShowDropTarget(TaskRowViewModel? row, bool after)
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
        _isDragging = false;
    }
}
