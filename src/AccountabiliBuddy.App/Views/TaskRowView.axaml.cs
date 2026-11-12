using System.ComponentModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// One task row (<see cref="TaskListRowViewModel"/>). Code-behind: a click passes its modifier keys (Ctrl / Shift: Windows selection)
/// to the section it sits in, right-click, the menu key and ⋯ open the row's menu, the label picker opens from its chip, the row's keys
/// (13k: ↑/↓ move the keyboard, Shift+↑/↓ extend the selection, Space ticks, Ctrl+Space adds it to the selection, Alt+↑/↓ move the
/// row; Enter opens it, as a click), the drag handle hands its pointer to the section (<see cref="TaskSectionView"/>), and a ticked
/// row folds away (height and opacity).
/// </summary>
public partial class TaskRowView : UserControl
{
    private static readonly TimeSpan FoldTime = TimeSpan.FromMilliseconds(300);

    private KeyModifiers _pressModifiers;
    private bool _pointerClick;
    private TaskListRowViewModel? _row;

    // Space is down on the row: its key repeats don't tick and untick the row again and again.
    private bool _spaceHeld;
    private Window? _window;

    public TaskRowView()
    {
        InitializeComponent();
        // The row button handles its own presses; the modifiers are read on the way down, before it does.
        RowButton.AddHandler(PointerPressedEvent, OnRowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Space is the row's tick, not a click: taken before the button presses itself (and its release, which would click).
        RowButton.AddHandler(KeyDownEvent, OnRowSpaceDown, RoutingStrategies.Tunnel);
        RowButton.AddHandler(KeyUpEvent, OnRowSpaceUp, RoutingStrategies.Tunnel);
        RowButton.LostFocus += (_, _) => _spaceHeld = false;
        // Space let go in another window never comes up here.
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window window)
            {
                _window = window;
                window.Deactivated += OnWindowDeactivated;
            }
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_window is not null)
            {
                _window.Deactivated -= OnWindowDeactivated;
                _window = null;
            }
        };
    }

    private TaskSectionView? Section => this.FindAncestorOfType<TaskSectionView>();

    private TaskSectionViewModel? SectionModel => Section?.DataContext as TaskSectionViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_row is not null)
        {
            _row.PropertyChanged -= OnRowPropertyChanged;
        }

        _row = DataContext as TaskListRowViewModel;
        RowRoot.Transitions = null;
        RowRoot.ClearValue(MaxHeightProperty);
        RowRoot.ClearValue(OpacityProperty);
        if (_row is not null)
        {
            _row.PropertyChanged += OnRowPropertyChanged;
            if (_row.IsCollapsing)
            {
                Fold();
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_row is not null)
        {
            _row.PropertyChanged -= OnRowPropertyChanged;
            _row = null;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_row is null && DataContext is TaskListRowViewModel row)
        {
            _row = row;
            _row.PropertyChanged += OnRowPropertyChanged;
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TaskListRowViewModel.IsCollapsing) || _row is null)
        {
            return;
        }

        if (_row.IsCollapsing)
        {
            Fold();
        }
        else
        {
            RowRoot.Transitions = null;
            RowRoot.ClearValue(MaxHeightProperty);
            RowRoot.ClearValue(OpacityProperty);
        }
    }

    // The ticked row folds away: from its height to nothing and from full to no opacity (at once when Motion is off).
    private void Fold()
    {
        TimeSpan time = Motion.Current?.Scale(FoldTime) ?? FoldTime;
        if (time <= TimeSpan.Zero)
        {
            RowRoot.MaxHeight = 0;
            RowRoot.Opacity = 0;
            return;
        }

        RowRoot.Transitions = null;
        RowRoot.MaxHeight = Math.Max(1, RowRoot.Bounds.Height);
        Dispatcher.UIThread.Post(() =>
        {
            RowRoot.Transitions =
            [
                new DoubleTransition { Property = MaxHeightProperty, Duration = time, Easing = new CubicEaseOut() },
                new DoubleTransition { Property = OpacityProperty, Duration = time },
            ];
            RowRoot.MaxHeight = 0;
            RowRoot.Opacity = 0;
        }, DispatcherPriority.Render);
    }

    // The sync-problem cloud on the detail line: "Changed on both devices" (the row itself does not open).
    // ▾ on a focused step's line: the live step list of its task (P3).
    private void OnStepListClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control anchor && DataContext is TaskListRowViewModel { StepList: { } make } && make() is { } steps)
        {
            StepListPopover.ShowAt(anchor, steps);
        }

        e.Handled = true;
    }

    private void OnConflictClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TaskListRowViewModel { OpenConflict: { } open })
        {
            open();
        }

        e.Handled = true;
    }

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressModifiers = e.KeyModifiers;
        _pointerClick = e.GetCurrentPoint(RowButton).Properties.IsLeftButtonPressed;
    }

    // A click with Ctrl and/or Shift selects (Windows rules); a plain click or Enter opens or closes the task.
    private void OnRowClick(object? sender, RoutedEventArgs e)
    {
        // Clicks of the buttons inside the row (check box, quick actions) bubble up here too: only the row's own click counts.
        if (DataContext is not TaskListRowViewModel row || !ReferenceEquals(e.Source, RowButton))
        {
            return;
        }

        KeyModifiers modifiers = _pointerClick ? _pressModifiers : KeyModifiers.None;
        _pointerClick = false;
        bool ctrl = modifiers.HasFlag(KeyModifiers.Control);
        bool shift = modifiers.HasFlag(KeyModifiers.Shift);
        TaskRowClick click = shift ? ctrl ? TaskRowClick.AddRange : TaskRowClick.Range : ctrl ? TaskRowClick.Toggle : TaskRowClick.Open;
        if (SectionModel is { } section)
        {
            section.Click(row, click);
        }
        else
        {
            row.Click(click);
        }

        e.Handled = true;
    }

    // The open arrow: the same as a plain click.
    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TaskListRowViewModel row)
        {
            if (SectionModel is { } section)
            {
                section.Click(row, TaskRowClick.Open);
            }
            else
            {
                row.Click(TaskRowClick.Open);
            }
        }

        e.Handled = true;
    }

    // Right-click (or the menu key): on a row inside a selection of two or more, the bulk menu; otherwise the row's own menu (a row
    // outside the selection clears it first).
    private void OnRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not TaskListRowViewModel row)
        {
            return;
        }

        if (!row.IsPicked)
        {
            SectionModel?.ClearSelection();
        }

        // From the menu key or Shift+F10 there is no pointer: the menu opens under the row.
        bool fromPointer = e.TryGetPosition(RowButton, out _);
        PlacementMode placement = fromPointer ? PlacementMode.Pointer : PlacementMode.BottomEdgeAlignedLeft;
        TaskMenus.ShowEntries(RowButton, row.BuildContextMenu(), placement, OnMenuViewAction, fromKeyboard: !fromPointer);
        e.Handled = true;
    }

    // The entries only the view can answer: Copy ID (the clipboard) and Reminders & due date (its popover, at the row).
    private void OnMenuViewAction(TaskMenuEntry entry)
    {
        if (DataContext is not TaskListRowViewModel row)
        {
            return;
        }

        switch (entry.ViewAction)
        {
            case "copyId":
                _ = TaskMenus.CopyAsync(this, row.Tip);
                break;
            case "reminders" when row.ReminderMenu?.Invoke() is { } menu:
                ReminderMenu.ShowAt(RowButton, menu, PlacementMode.BottomEdgeAlignedLeft);
                break;
        }
    }

    // ↑/↓ move the keyboard to the row above or below; Shift+↑/↓ extend the selection; Alt+↑/↓ move the row one place in its section
    // (reorder and renumber). Ctrl+A and Esc are the section's; Del, F2, H and Ctrl+Z the window's (Services/TaskShortcuts).
    private void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down) || DataContext is not TaskListRowViewModel row || Section is not { } section)
        {
            return;
        }

        int step = e.Key == Key.Up ? -1 : 1;
        if (e.KeyModifiers == KeyModifiers.Alt && row.CanDrag)
        {
            section.MoveByKey(row, step);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.None)
        {
            section.FocusNeighbour(row, step);
            e.Handled = true; // at an end too: the list doesn't scroll away under the keyboard
        }
        else if (e.KeyModifiers == KeyModifiers.Shift)
        {
            section.ExtendByKey(row, step);
            e.Handled = true;
        }
    }

    // Space on the row: tick it (a second Space while it is struck through takes it back). Ctrl+Space: add it to the selection, or take
    // it out (Ctrl+click). Only the row itself: a button inside it that has the keyboard after a click keeps its own Space.
    private void OnRowSpaceDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || !ReferenceEquals(e.Source, RowButton) || DataContext is not TaskListRowViewModel row)
        {
            return;
        }

        e.Handled = true;
        if (_spaceHeld)
        {
            return;
        }

        _spaceHeld = true;
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            if (row.OwnerKey is null)
            {
                Click(row, TaskRowClick.Toggle);
            }
        }
        else if (e.KeyModifiers == KeyModifiers.None && row.ToggleCommand is { } tick && tick.CanExecute(null))
        {
            tick.Execute(null);
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _spaceHeld = false;
    }

    private void OnRowSpaceUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spaceHeld = false;
        }

        if (e.Key == Key.Space && ReferenceEquals(e.Source, RowButton))
        {
            e.Handled = true;
        }
    }

    private void Click(TaskListRowViewModel row, TaskRowClick click)
    {
        if (SectionModel is { } section)
        {
            section.Click(row, click);
        }
        else
        {
            row.Click(click);
        }
    }

    // A ⋯ button: the menu is built from the row it belongs to each time it opens.
    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            TaskMenus.Show(button, OnMenuViewAction);
            e.Handled = true;
        }
    }

    // The label chip or "+ label" spot: the picker of ⋯ › Label. Handled here, so the click does not also open or tick the row.
    private void OnCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            TaskMenus.ShowCategoryPicker(button);
            e.Handled = true;
        }
    }

    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control handle)
        {
            Section?.HandlePressed(handle, e);
        }
    }

    private void OnHandleMoved(object? sender, PointerEventArgs e)
    {
        Section?.HandleMoved(e);
    }

    private void OnHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        Section?.HandleReleased(e);
    }

    private void OnHandleCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        Section?.EndDrag();
    }
}
