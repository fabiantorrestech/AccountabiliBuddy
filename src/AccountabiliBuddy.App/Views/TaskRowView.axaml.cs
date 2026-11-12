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
/// to the section it sits in, right-click and ⋯ open the row's menu, the label picker opens from its chip, Alt+↑/↓ move the row, the
/// drag handle hands its pointer to the section (<see cref="TaskSectionView"/>), and a ticked row folds away (height and opacity).
/// </summary>
public partial class TaskRowView : UserControl
{
    private static readonly TimeSpan FoldTime = TimeSpan.FromMilliseconds(300);

    private KeyModifiers _pressModifiers;
    private bool _pointerClick;
    private TaskListRowViewModel? _row;

    public TaskRowView()
    {
        InitializeComponent();
        // The row button handles its own presses; the modifiers are read on the way down, before it does.
        RowButton.AddHandler(PointerPressedEvent, OnRowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
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

    // Right-click (or the menu key): the row's own menu. On a row outside the selection the selection is cleared first; the bulk
    // menu for a selection comes with the menus (3.5), so a selected row shows its own menu too.
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

        TaskMenus.ShowEntries(RowButton, row.BuildMenu(), PlacementMode.Pointer);
        e.Handled = true;
    }

    // Alt+↑/↓ moves the row one place in its section (reorder and renumber); Ctrl+A and Esc are the section's.
    private void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Up or Key.Down && DataContext is TaskListRowViewModel { CanDrag: true } row && Section is { } section)
        {
            section.MoveByKey(row, e.Key == Key.Up ? -1 : 1);
            e.Handled = true;
        }
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
