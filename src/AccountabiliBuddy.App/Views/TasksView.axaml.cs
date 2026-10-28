using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Tasks page. Code-behind: dragging a This-session row by its handle, Esc in the search box, the search box taking the
/// keyboard when it opens, keeping a title or note typed in the detail card when the box loses the keyboard, filling a ⋯ menu when
/// it opens, and putting the keyboard in the detail card's title or step box when a menu asks for it.
/// </summary>
public partial class TasksView : UserControl
{
    // How far the pointer moves on a drag handle before it's a drag, not a click.
    private const double DragThreshold = 4;

    private TasksViewModel? _subscribed;
    private TaskListRowViewModel? _dragRow;
    private Point _dragStart;
    private bool _isDragging;
    private TaskListRowViewModel? _dropTarget;
    private bool _dropAfter;

    public TasksView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnPagePropertyChanged;
            _subscribed.FocusRequested -= OnFocusRequested;
        }

        _subscribed = DataContext as TasksViewModel;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnPagePropertyChanged;
            _subscribed.FocusRequested += OnFocusRequested;
        }
    }

    // A row's menu said Rename or Add subtask: the detail card shows that task, and its title or step box takes the keyboard.
    private void OnFocusRequested(object? sender, string target)
    {
        Dispatcher.UIThread.Post(() =>
        {
            string name = target == "step" ? "DetailStepBox" : "DetailTitleBox";
            TextBox? box = this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(candidate => candidate.Name == name);
            if (box is null)
            {
                return;
            }

            box.Focus();
            if (target == "title")
            {
                box.SelectAll();
            }
        }, DispatcherPriority.Background);
    }

    // A ⋯ button: the menu is built from the row it belongs to each time it opens.
    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            TaskMenus.Show(button);
            e.Handled = true;
        }
    }

    // A step's inline box (rename, a step after it) takes the keyboard as it opens.
    private void OnInlineBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
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

    // Opening the search puts the keyboard in its box.
    private void OnPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TasksViewModel.IsSearchOpen) && _subscribed is { IsSearchOpen: true })
        {
            Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("SearchBox")?.Focus(), DispatcherPriority.Loaded);
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _subscribed is not null)
        {
            _subscribed.ToggleSearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnDetailTitleLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TaskDetailViewModel detail })
        {
            detail.CommitTitleCommand.Execute(null);
        }
    }

    private void OnDetailNoteLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TaskDetailViewModel detail })
        {
            detail.CommitNoteCommand.Execute(null);
        }
    }

    // Drag to reorder: pressing a row's handle takes the pointer; the drag starts once it has moved a few pixels.
    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: TaskListRowViewModel row } handle || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
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

        // Between two rows the last place found stays; outside the page there is none.
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
        if (_dragRow is not null && _isDragging && _dropTarget is not null && DataContext is TasksViewModel page)
        {
            _ = page.MoveSessionRowAsync(_dragRow.Id, _dropTarget.Id, _dropAfter);
        }

        EndDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnHandleCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndDrag();
    }

    // The This-session row under the pointer that the dragged one can go next to: above or below it by the pointer's half.
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

    private void EndDrag()
    {
        ShowDropTarget(null, false);
        _dragRow = null;
        _isDragging = false;
    }
}
