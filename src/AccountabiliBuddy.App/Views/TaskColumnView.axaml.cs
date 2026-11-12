using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// One Shelf column (round 2 R3). Code-behind: Alt+← / Alt+→ on the focused header move the column (the keyboard stays on its header
/// once the page has been rebuilt), the header's right-click menu (Expand all, Collapse all), and dragging the header's grip onto
/// another column, which takes that column's place. The rows inside are a <see cref="TaskSectionView"/>: their drag, keyboard and
/// selection are unchanged.
/// </summary>
public partial class TaskColumnView : UserControl
{
    // How far the pointer moves on the grip before it's a drag, not a click.
    private const double DragThreshold = 4;

    private bool _gripPressed;
    private bool _isDragging;
    private Point _gripStart;
    private TaskSectionViewModel? _target;

    public TaskColumnView()
    {
        InitializeComponent();
    }

    // ---- Alt+← / Alt+→ on the header ----

    /// <summary>The visual is a column's header (or inside it), where Alt+← / Alt+→ move the column, not back / forward.</summary>
    public static bool IsHeader(Visual visual)
    {
        TaskColumnView? column = visual as TaskColumnView ?? visual.FindAncestorOfType<TaskColumnView>();
        return column is not null && (ReferenceEquals(column.HeaderButton, visual) || column.HeaderButton.IsVisualAncestorOf(visual));
    }

    private void OnHeaderKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Left or Key.Right) || DataContext is not TaskSectionViewModel { CanMoveColumn: true } column)
        {
            return;
        }

        e.Handled = true;
        _ = MoveAndKeepFocusAsync(column, e.Key == Key.Left ? -1 : 1);
    }

    private async Task MoveAndKeepFocusAsync(TaskSectionViewModel column, int step)
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        string key = column.Key;
        await column.MoveColumnAsync(step);

        // The page rebuilds (coalesced, in the background) and the column's view may be made again: the keyboard goes back to its header.
        Dispatcher.UIThread.Post(() => FocusHeaderOf(top, key), DispatcherPriority.ContextIdle);
    }

    private static void FocusHeaderOf(TopLevel? top, string key)
    {
        TaskColumnView? view = top?.GetVisualDescendants().OfType<TaskColumnView>()
            .FirstOrDefault(candidate => candidate.DataContext is TaskSectionViewModel section && section.Key == key);
        if (view is not null)
        {
            view.HeaderButton.Focus(NavigationMethod.Directional);
            view.HeaderButton.BringIntoView();
        }
    }

    private void OnHeaderContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control header && DataContext is TaskSectionViewModel column)
        {
            TaskMenus.ShowEntries(header, column.HeaderMenu(), PlacementMode.BottomEdgeAlignedLeft, fromKeyboard: !e.TryGetPosition(header, out _));
            e.Handled = true;
        }
    }

    // ---- Dragging the grip onto another column ----

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control grip || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed || DataContext is not TaskSectionViewModel { CanMoveColumn: true })
        {
            return;
        }

        _gripPressed = true;
        _isDragging = false;
        _gripStart = e.GetPosition(this);
        e.Pointer.Capture(grip);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (!_gripPressed)
        {
            return;
        }

        if (!_isDragging)
        {
            Point moved = e.GetPosition(this) - _gripStart;
            if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold)
            {
                return;
            }

            _isDragging = true;
        }

        ShowTarget(ColumnUnder(e));
        e.Handled = true;
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging && _target is { } target && DataContext is TaskSectionViewModel column)
        {
            _ = column.MoveColumnOntoAsync(target.Key);
        }

        EndGripDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnGripCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndGripDrag();
    }

    private void EndGripDrag()
    {
        ShowTarget(null);
        _gripPressed = false;
        _isDragging = false;
    }

    // The other column under the pointer, if any.
    private TaskSectionViewModel? ColumnUnder(PointerEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not TopLevel top || top.InputHitTest(e.GetPosition(top)) is not Visual hit)
        {
            return null;
        }

        TaskColumnView? column = hit.GetSelfAndVisualAncestors().OfType<TaskColumnView>().FirstOrDefault();
        return column is not null && !ReferenceEquals(column, this) && column.DataContext is TaskSectionViewModel section ? section : null;
    }

    private void ShowTarget(TaskSectionViewModel? target)
    {
        if (ReferenceEquals(_target, target))
        {
            return;
        }

        if (_target is not null)
        {
            _target.IsColumnDropTarget = false;
        }

        _target = target;
        if (target is not null)
        {
            target.IsColumnDropTarget = true;
        }
    }
}
