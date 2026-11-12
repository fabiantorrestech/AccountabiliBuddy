using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Tasks page. Code-behind: the two columns' widths (the Today / Shelf split, a hidden column giving its room to the other), the
/// resize handle between them, keeping a title or note typed in the detail card when the box loses the keyboard, the ⋯ menus of the
/// detail card's steps, putting the keyboard in the detail card's title or step box when a menu asks for it, and the dialogs the
/// rows and menus ask for (Rename one by one, Delete N, Changed on both devices).
/// </summary>
public partial class TasksView : UserControl
{
    /// <summary>True → 1, false → 0: the reset button keeps its slot while hidden.</summary>
    public static readonly Avalonia.Data.Converters.IValueConverter ShownOpacity =
        new Avalonia.Data.Converters.FuncValueConverter<bool, double>(shown => shown ? 1 : 0);

    /// <summary>The toast's place: 18 in from the card's left and bottom, lifted above Jot it down while the Today column shows it.</summary>
    public static readonly Avalonia.Data.Converters.IValueConverter ToastMargin =
        new Avalonia.Data.Converters.FuncValueConverter<bool, Thickness>(todayShows => new Thickness(18, 0, 0, todayShows ? JotClearance : 18));

    // Jot it down's band at the bottom of the Today column (its pill and margins) plus the toast's own 18.
    private const double JotClearance = 64;

    // The narrowest a shown column gets.
    private const double ColumnMinWidth = 280;

    private TasksViewModel? _subscribed;
    private bool _isSplitting;
    private int _splitWhileDragging;

    public TasksView()
    {
        InitializeComponent();
        // The reset button's own press and release (a Button handles them itself, so listen to handled ones too).
        ResetButton.AddHandler(PointerPressedEvent, OnResetPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        ResetButton.AddHandler(PointerReleasedEvent, OnResetReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        ResetButton.AddHandler(PointerCaptureLostEvent, OnResetCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        ResetButton.AddHandler(KeyDownEvent, OnResetKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        ResetButton.AddHandler(KeyUpEvent, OnResetKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    // The page stays alive while another tab shows: leaving it clears the selection, so it is not found half-done on return.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && !IsVisible && DataContext is TasksViewModel page)
        {
            page.ClearSelection();
        }

        if (change.Property == IsVisibleProperty && DataContext is TasksViewModel shown)
        {
            shown.IsOnScreen = IsEffectivelyVisible;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is TasksViewModel page)
        {
            page.IsOnScreen = IsEffectivelyVisible;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (DataContext is TasksViewModel page)
        {
            page.IsOnScreen = false;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnPagePropertyChanged;
            _subscribed.FocusRequested -= OnFocusRequested;
            _subscribed.RenameEachRequested -= OnRenameEachRequested;
            _subscribed.DeleteManyRequested -= OnDeleteManyRequested;
            _subscribed.ConflictRequested -= OnConflictRequested;
            _subscribed.SessionFullRequested -= OnSessionFullRequested;
            _subscribed.FocusRowRequested -= OnFocusRowRequested;
        }

        _subscribed = DataContext as TasksViewModel;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnPagePropertyChanged;
            _subscribed.FocusRequested += OnFocusRequested;
            _subscribed.RenameEachRequested += OnRenameEachRequested;
            _subscribed.DeleteManyRequested += OnDeleteManyRequested;
            _subscribed.ConflictRequested += OnConflictRequested;
            _subscribed.SessionFullRequested += OnSessionFullRequested;
            _subscribed.FocusRowRequested += OnFocusRowRequested;
            _subscribed.IsOnScreen = IsEffectivelyVisible && VisualRoot is not null;
        }

        ApplyColumns();
    }

    // Ctrl+G opened a task: once the page shows it (the tab switch, the Shelf, an unfolded section), its row takes the keyboard.
    private void OnFocusRowRequested(object? sender, TaskRowKey key)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Button? row = this.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.Classes.Contains("listRow") && button.IsEffectivelyVisible && button.DataContext is TaskListRowViewModel candidate && candidate.Key == key);
            if (row is not null)
            {
                row.Focus(NavigationMethod.Tab);
                row.BringIntoView();
            }
        }, DispatcherPriority.Background);
    }

    // ---- The dialogs (spec 03 §C): over the window, the keyboard back where it was once they close ----

    private void OnRenameEachRequested(object? sender, IReadOnlyList<Guid> ids)
    {
        if (_subscribed?.RenameEachDialog(ids) is { } rename)
        {
            _ = RenameOneByOneDialog.ShowAsync(this, rename);
        }
    }

    private void OnDeleteManyRequested(object? sender, IReadOnlyList<Guid> ids)
    {
        if (_subscribed?.DeleteManyDialog(ids) is { } delete)
        {
            _ = DeleteTasksDialog.ShowAsync(this, delete);
        }
    }

    private void OnConflictRequested(object? sender, Guid id)
    {
        if (_subscribed?.ConflictDialogFor(id) is { } conflict)
        {
            _ = ConflictDialog.ShowAsync(this, conflict);
        }
    }

    private void OnSessionFullRequested(object? sender, SessionFullViewModel dialog)
    {
        // A reminder's "Start session" opens it over the reminder's own topmost window; everything else over this page.
        _ = SessionFullDialog.ShowAsync(dialog.Over ?? this, dialog);
    }

    // ---- "Hold to reset" (1.5 s): the fill grows while held; let go early (or a click) shows the hint for a moment ----

    private static readonly TimeSpan ResetHold = TimeSpan.FromSeconds(1.5);
    private DispatcherTimer? _holdTimer;
    private DateTime _holdStarted;
    private DispatcherTimer? _hintTimer;

    private void OnResetPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(ResetButton).Properties.IsLeftButtonPressed)
        {
            StartHold();
        }
    }

    private void OnResetReleased(object? sender, PointerReleasedEventArgs e)
    {
        StopHold(showHint: true);
    }

    private void OnResetCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        StopHold(showHint: false);
    }

    private void OnResetKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter)
        {
            if (_holdTimer is null)
            {
                StartHold();
            }

            e.Handled = true;
        }
    }

    private void OnResetKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter)
        {
            StopHold(showHint: true);
            e.Handled = true;
        }
    }

    private void StartHold()
    {
        StopHold(showHint: false);
        _holdStarted = DateTime.UtcNow;
        _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _holdTimer.Tick += (_, _) =>
        {
            double share = Math.Clamp((DateTime.UtcNow - _holdStarted) / ResetHold, 0, 1);
            ResetFill.Width = ResetButton.Bounds.Width * share;
            if (share >= 1)
            {
                StopHold(showHint: false);
                _subscribed?.ResetSessionView();
            }
        };
        _holdTimer.Start();
    }

    // The hold ended: the fill goes; a hold let go early shows the hint for a second.
    private void StopHold(bool showHint)
    {
        bool wasHolding = _holdTimer is not null;
        _holdTimer?.Stop();
        _holdTimer = null;
        ResetFill.Width = 0;
        if (showHint && wasHolding && _subscribed is not null)
        {
            _subscribed.ShowsResetHint = true;
            _hintTimer?.Stop();
            _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _hintTimer.Tick += (_, _) =>
            {
                _hintTimer?.Stop();
                _hintTimer = null;
                if (_subscribed is not null)
                {
                    _subscribed.ShowsResetHint = false;
                }
            };
            _hintTimer.Start();
        }
    }

    private void OnPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TasksViewModel.ShowsToday) or nameof(TasksViewModel.IsShelfShown) or nameof(TasksViewModel.TodaySplit))
        {
            ApplyColumns();
        }
    }

    // Both columns: Today takes its share of the split and the Shelf the rest; one column alone takes all the room.
    private void ApplyColumns(int? split = null)
    {
        if (_subscribed is not { } page)
        {
            return;
        }

        bool both = page.ShowsToday && page.IsShelfShown;
        int today = split ?? page.TodaySplit;
        ColumnDefinition todayColumn = Page.ColumnDefinitions[1];
        ColumnDefinition shelfColumn = Page.ColumnDefinitions[3];
        todayColumn.Width = page.ShowsToday ? new GridLength(both ? today : 1, GridUnitType.Star) : GridLength.Auto;
        todayColumn.MinWidth = page.ShowsToday ? ColumnMinWidth : 0;
        shelfColumn.Width = page.IsShelfShown ? new GridLength(both ? 100 - today : 1, GridUnitType.Star) : GridLength.Auto;
        shelfColumn.MinWidth = page.IsShelfShown ? ColumnMinWidth : 0;
    }

    // ---- The resize handle: Today's share follows the pointer (30 to 70 %), kept when let go; a double-click puts it back. ----

    private void OnSplitPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control handle || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed || _subscribed is null)
        {
            return;
        }

        _isSplitting = true;
        _splitWhileDragging = _subscribed.TodaySplit;
        SplitGrip.Classes.Set("active", true);
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void OnSplitMoved(object? sender, PointerEventArgs e)
    {
        if (!_isSplitting)
        {
            return;
        }

        double left = TodayColumn.Bounds.Left;
        double width = ShelfColumn.Bounds.Right - left;
        if (width <= 0)
        {
            return;
        }

        double percent = (e.GetPosition(Page).X - left) / width * 100;
        _splitWhileDragging = (int)Math.Round(Math.Clamp(percent, Services.Tasks.TaskLayoutPreferences.MinSplit, Services.Tasks.TaskLayoutPreferences.MaxSplit));
        ApplyColumns(_splitWhileDragging);
        e.Handled = true;
    }

    private void OnSplitReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isSplitting && _subscribed is not null)
        {
            _ = _subscribed.SetSplitAsync(_splitWhileDragging);
        }

        EndSplit();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnSplitCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndSplit();
    }

    private void OnSplitDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_subscribed is not null)
        {
            _ = _subscribed.SetSplitAsync(Services.Tasks.TaskLayoutPreferences.DefaultSplit);
        }

        e.Handled = true;
    }

    private void EndSplit()
    {
        if (!_isSplitting)
        {
            return;
        }

        _isSplitting = false;
        SplitGrip.Classes.Set("active", false);
        ApplyColumns();
    }

    // A row's menu said Rename or Add subtask: the detail card shows that task, and its title or step box takes the keyboard. A picker's "New category…": its name box does.
    private void OnFocusRequested(object? sender, string target)
    {
        Dispatcher.UIThread.Post(() =>
        {
            string name = target switch
            {
                "step" => "DetailStepBox",
                "newCategory" => "NewCategoryBox",
                _ => "DetailTitleBox",
            };
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

    // ---- The open task's chips and footer ----

    private void OnLabelChipClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TaskDetailViewModel detail } button)
        {
            TaskMenus.ShowEntries(button, detail.LabelMenu(), PlacementMode.BottomEdgeAlignedLeft);
            e.Handled = true;
        }
    }

    private void OnTagChipClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TaskDetailViewModel detail } button)
        {
            TaskMenus.ShowEntries(button, detail.TagMenu(), PlacementMode.BottomEdgeAlignedLeft);
            e.Handled = true;
        }
    }

    private void OnTimeLimitChipClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TaskDetailViewModel detail } button)
        {
            TaskMenus.ShowEntries(button, detail.TimeLimitMenu(), PlacementMode.BottomEdgeAlignedLeft);
            e.Handled = true;
        }
    }

    // The ID chip: "142 · title" on the clipboard (the title is the user's own text, copied only where they asked); the toast says so.
    private async void OnCopyIdClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TaskDetailViewModel detail })
        {
            return;
        }

        e.Handled = true;
        await TaskMenus.CopyAsync(this, detail.CopyText);
    }

    // ---- Dragging a step by its handle among its task's steps (the letters follow on the rebuild); Alt+↑/↓ on a step too ----

    private TaskStepViewModel? _dragStep;
    private TaskStepViewModel? _stepTarget;
    private bool _stepAfter;
    private Point _stepStart;
    private bool _isDraggingStep;

    private void OnStepHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: TaskStepViewModel step } handle && step.CanDrag && e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            _dragStep = step;
            _stepStart = e.GetPosition(this);
            _isDraggingStep = false;
            e.Pointer.Capture(handle);
            e.Handled = true;
        }
    }

    private void OnStepHandleMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStep is null)
        {
            return;
        }

        Point at = e.GetPosition(this);
        if (!_isDraggingStep)
        {
            Point moved = at - _stepStart;
            if (Math.Abs(moved.X) < 4 && Math.Abs(moved.Y) < 4)
            {
                return;
            }

            _isDraggingStep = true;
        }

        ShowStepTarget(null, false);
        if (this.InputHitTest(at) is Visual hit)
        {
            foreach (Visual visual in hit.GetSelfAndVisualAncestors())
            {
                if (visual is Control { DataContext: TaskStepViewModel step } item && item.Classes.Contains("stepItem"))
                {
                    if (step != _dragStep && this.TranslatePoint(at, item) is Point local)
                    {
                        ShowStepTarget(step, local.Y > item.Bounds.Height / 2);
                    }

                    break;
                }
            }
        }

        e.Handled = true;
    }

    private void OnStepHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragStep is not null && _isDraggingStep && _stepTarget is not null && DataContext is TasksViewModel { Detail: { } detail })
        {
            _ = detail.MoveStepAsync(_dragStep.Id, _stepTarget.Id, _stepAfter);
        }

        EndStepDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnStepHandleCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndStepDrag();
    }

    private void EndStepDrag()
    {
        ShowStepTarget(null, false);
        _dragStep = null;
        _isDraggingStep = false;
    }

    private void ShowStepTarget(TaskStepViewModel? step, bool after)
    {
        if (_stepTarget is not null)
        {
            _stepTarget.DropMark = 0;
        }

        _stepTarget = step;
        _stepAfter = after;
        if (step is not null)
        {
            step.DropMark = after ? 1 : -1;
        }
    }

    // Alt+↑/↓ on a step (its check box or a button of its row): one place up or down.
    private void OnStepKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Up or Key.Down) || sender is not Control { DataContext: TaskStepViewModel step }
            || !step.CanDrag || DataContext is not TasksViewModel { Detail: { } detail })
        {
            return;
        }

        int index = detail.Steps.IndexOf(step);
        int to = index + (e.Key == Key.Up ? -1 : 1);
        if (index >= 0 && to >= 0 && to < detail.Steps.Count)
        {
            _ = detail.MoveStepAsync(step.Id, detail.Steps[to].Id, e.Key == Key.Down);
        }

        e.Handled = true;
    }

    // The detail card's ⋯ and a step's ⋯: the menu is built from what it belongs to each time it opens. The open task's menu can ask
    // for Copy ID and Reminders & due date, which only the view can do.
    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            TaskMenus.Show(button, entry => OnDetailMenuViewAction(button, entry));
            e.Handled = true;
        }
    }

    private void OnDetailMenuViewAction(Button button, TaskMenuEntry entry)
    {
        if (button.DataContext is not TaskDetailViewModel detail)
        {
            return;
        }

        switch (entry.ViewAction)
        {
            case "copyId":
                _ = TaskMenus.CopyAsync(this, detail.CopyText);
                break;
            case "reminders" when detail.ReminderMenu() is { } menu:
                ReminderMenu.ShowAt(button, menu, PlacementMode.BottomEdgeAlignedRight);
                break;
        }
    }

    // The open task's reminders line: its Reminders & due date menu.
    private void OnRemindersClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TaskDetailViewModel detail } button && detail.ReminderMenu() is { } menu)
        {
            ReminderMenu.ShowAt(button, menu, PlacementMode.BottomEdgeAlignedLeft);
            e.Handled = true;
        }
    }

    // Esc clears the selection; otherwise it closes the open task, unless a text box has the focus: Esc there is left to the edit.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || e.Handled || DataContext is not TasksViewModel page)
        {
            return;
        }

        if (page.ClearSelection())
        {
            e.Handled = true;
            return;
        }

        bool editing = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;
        if (!editing && page.IsDetailOpen)
        {
            page.CloseDetailCommand.Execute(null);
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
}
