using AccountabiliBuddy.App.Controls;
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
/// The task editor popup (Tasks redesign item 5), hosted by the Tasks page and by Today (each shows it while the popup is theirs).
/// Code-behind: a click on the dim closes it; Esc closes it (a menu or an inline box inside takes Esc first); Ctrl+Enter adds a new task;
/// the keyboard goes into the popup as it opens and back to where it was as it closes; the box a menu asks for (Rename, Add subtask, a
/// new task's title) takes the keyboard; the pills' menus and the label picker; the ⋯ menus, Copy ID, Reminders; dragging a step by its
/// handle and Alt+↑/↓; keeping a title or note typed when the box loses the keyboard.
/// </summary>
public partial class TaskEditorView : UserControl
{
    // The label pill's picker: tall enough for the rail and the labels, each scrolling on its own.
    private const double LabelPickerWidth = 440;
    private const double LabelPickerHeight = 320;

    private TasksViewModel? _subscribed;
    private IInputElement? _focusBefore;
    private bool _focusTargetPending;

    public TaskEditorView()
    {
        InitializeComponent();
        // Tunnel: Ctrl+Enter adds a new task before a notes box takes it as a new line.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private TasksViewModel? Page => DataContext as TasksViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.FocusRequested -= OnFocusRequested;
        }

        _subscribed = DataContext as TasksViewModel;
        if (_subscribed is not null)
        {
            _subscribed.FocusRequested += OnFocusRequested;
        }
    }

    // Shown: the keyboard goes into the popup (unless a box asked for it); hidden: back to what had it before.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty)
        {
            return;
        }

        if (IsVisible)
        {
            _focusBefore = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            Dispatcher.UIThread.Post(() =>
            {
                if (!_focusTargetPending && IsEffectivelyVisible && !IsKeyboardInside())
                {
                    Frame.Focus(NavigationMethod.Directional);
                }
            }, DispatcherPriority.Loaded);
            return;
        }

        IInputElement? before = _focusBefore;
        _focusBefore = null;
        if (before is InputElement element && element.IsEffectivelyVisible && element.Focusable && TopLevel.GetTopLevel(element) is not null
            && !this.IsVisualAncestorOf(element))
        {
            Dispatcher.UIThread.Post(() => element.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
        }
    }

    private bool IsKeyboardInside()
    {
        return TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused && Frame.IsVisualAncestorOf(focused);
    }

    // ---- Closing and adding ----

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        Page?.CloseEditorCommand.Execute(null);
        e.Handled = true;
    }

    // Esc that nothing inside took (a step's inline box, the minutes box): the popup closes; a new task goes back to the add box.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && Page is { ShowsEditor: true } page)
        {
            page.CloseEditorCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control && Page?.Editor is TaskDraftViewModel draft)
        {
            draft.AddCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ---- The box a menu or the add box asks for ----

    private void OnFocusRequested(object? sender, string target)
    {
        string? name = target switch
        {
            "title" => "DetailTitleBox",
            "step" => "DetailStepBox",
            "draftTitle" => "DraftTitleBox",
            _ => null,
        };
        if (name is null)
        {
            return;
        }

        _focusTargetPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _focusTargetPending = false;
            if (!IsEffectivelyVisible)
            {
                return;
            }

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
            else
            {
                box.CaretIndex = box.Text?.Length ?? 0;
            }
        }, DispatcherPriority.Background);
    }

    // ---- The open task's pills ----

    // The label pill: the shared label picker (categories, search, No label); before the task list, the older label menu.
    private void OnLabelPillClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TaskDetailViewModel detail } button)
        {
            return;
        }

        if (detail.LabelPicker() is { } picker)
        {
            ShowLabelPicker(button, picker, pick => _ = detail.ApplyLabelPickAsync(pick));
        }
        else
        {
            TaskMenus.ShowEntries(button, detail.LabelMenu(), PlacementMode.BottomEdgeAlignedLeft);
        }

        e.Handled = true;
    }

    private void OnTagPillClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TaskDetailViewModel detail } button)
        {
            TaskMenus.ShowEntries(button, detail.TagMenu(), PlacementMode.BottomEdgeAlignedLeft, entry => BeginNewTag(button, entry));
            e.Handled = true;
        }
    }

    // "New tag…": the tag pill is swapped for a text box in its place (Enter adds the tag to this task, Esc or leaving it cancels).
    private static void BeginNewTag(Button pill, TaskMenuEntry entry)
    {
        if (entry.Submit is not { } submit || pill.Parent is not Panel row)
        {
            return;
        }

        var box = new TextBox { MinWidth = 160, Height = 28, PlaceholderText = "New tag · Enter adds it" };
        Avalonia.Automation.AutomationProperties.SetName(box, "New tag for this task");
        bool finished = false;

        void Finish(bool add)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            string typed = box.Text ?? string.Empty;
            row.Children.Remove(box);
            pill.IsVisible = true;
            if (add && !string.IsNullOrWhiteSpace(typed))
            {
                _ = submit(typed);
            }
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                Finish(add: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Finish(add: false);
                e.Handled = true;
            }
        };
        box.LostFocus += (_, _) => Finish(add: false);
        row.Children.Insert(row.Children.IndexOf(pill) + 1, box);
        pill.IsVisible = false;
        Dispatcher.UIThread.Post(() => box.Focus(NavigationMethod.Pointer), DispatcherPriority.Background);
    }

    private void OnTimeLimitPillClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TaskDetailViewModel detail } button)
        {
            TaskMenus.ShowEntries(button, detail.TimeLimitMenu(), PlacementMode.BottomEdgeAlignedLeft);
            e.Handled = true;
        }
    }

    // ---- A new task's pills ----

    private void OnDraftLabelPillClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TaskDraftViewModel draft } button && draft.LabelPicker() is { } picker)
        {
            ShowLabelPicker(button, picker, draft.ApplyLabelPick);
            e.Handled = true;
        }
    }

    private void OnDraftTagPillClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TaskDraftViewModel draft } button)
        {
            TaskMenus.ShowEntries(button, draft.TagMenu(), PlacementMode.BottomEdgeAlignedLeft, entry => BeginNewTag(button, entry));
            e.Handled = true;
        }
    }

    private void OnDraftTimeLimitPillClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TaskDraftViewModel draft } button)
        {
            TaskMenus.ShowEntries(button, draft.TimeLimitMenu(), PlacementMode.BottomEdgeAlignedLeft);
            e.Handled = true;
        }
    }

    // The label picker in a popover under the pill: a pick applies and closes it (a second click on the picked label only takes it back).
    private static void ShowLabelPicker(Control anchor, LabelPickerViewModel picker, Action<LabelPick> apply)
    {
        var view = new LabelPicker { DataContext = picker, Height = LabelPickerHeight };
        var flyout = new TaskPopoverFlyout(LabelPickerWidth)
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = new Border { Padding = new Thickness(12), Child = view },
        };
        picker.PickChanged += (_, _) =>
        {
            if (!picker.Pick.IsNothing)
            {
                apply(picker.Pick);
                flyout.Hide();
            }
        };
        flyout.Opened += (_, _) => Dispatcher.UIThread.Post(view.FocusSearch, DispatcherPriority.Input);
        flyout.ShowAt(anchor);
    }

    // ---- The footer ----

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

    // The open task's ⋯ and a step's ⋯: the menu is built from what it belongs to each time it opens. The open task's menu can ask for
    // Copy ID and Reminders & due date, which only the view can do.
    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            TaskMenus.Show(button, entry => OnMenuViewAction(button, entry));
            e.Handled = true;
        }
    }

    private void OnMenuViewAction(Button button, TaskMenuEntry entry)
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
                ReminderMenu.ShowAt(button, menu, PlacementMode.TopEdgeAlignedRight);
                break;
        }
    }

    // Reminders: the Reminders & due date menu.
    private void OnRemindersClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TaskDetailViewModel detail } button && detail.ReminderMenu() is { } menu)
        {
            ReminderMenu.ShowAt(button, menu, PlacementMode.BottomEdgeAlignedRight);
            e.Handled = true;
        }
    }

    // ---- Keeping what is typed ----

    // A step's inline box (rename, a step after it) and the minutes box take the keyboard as they open.
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
        if (_dragStep is not null && _isDraggingStep && _stepTarget is not null && Page is { Detail: { } detail })
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
            || !step.CanDrag || Page is not { Detail: { } detail })
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
}
