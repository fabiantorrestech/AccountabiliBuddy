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
/// Dragging the selection as a stack on the Tasks page (Tasks redesign 8b, SPEC "Dragging", "Turning the page while dragging"; boards 14-18).
/// The pointer side of it: a left press on a Shelf row arms a drag that starts once the pointer has moved 6 px (|dx| + |dy|, 8b.01); from
/// then on the page holds the pointer, the stack card follows it 14 px right and 10 px below in the window's overlay layer (so it shows over
/// the +N list too), and what is under the pointer is found by a hit test: the drag bar's zones, a page arrow, the +N chip, the Today pill, a
/// label chip or +N list row, or a Shelf column (the view model says which columns take the stack). Holding the stack on a page arrow turns
/// the page after 2 s and every 2 s while it stays there (8b.41); holding it on +N opens the full label list (8b.27). The window can't be
/// resized during the drag (<see cref="WindowSizePin"/>, 8b.45). Letting go drops it where it is (<see cref="TasksViewModel.DropStackAsync"/>);
/// Esc, a lost pointer capture or the window losing focus cancel it and keep the selection (8b.17, 8b.46). One drag at a time.
/// </summary>
internal sealed class TaskStackDrag
{
    /// <summary>How far the pointer moves (|dx| + |dy|) before a press on a row is a drag, not a click.</summary>
    public const double StartDistance = 6;

    /// <summary>How long the stack is held on a page arrow before the page turns, and between turns.</summary>
    public static readonly TimeSpan HoldTime = TimeSpan.FromSeconds(2);

    // The stack card's offset from the pointer.
    private static readonly Vector StackOffset = new(14, 10);

    private readonly TasksView _page;
    private readonly Popup _labelsMenu;
    private readonly Control _moreChip;

    private TaskListRowViewModel? _pressedRow;
    private Point _pressedAt;
    private bool _dragging;
    private bool _ending;
    private IPointer? _pointer;
    private Window? _window;
    private WindowSizePin? _sizePin;
    private OverlayLayer? _layer;
    private TaskDragStackView? _stack;
    private StackDropTarget? _target;
    private Button? _arrowUnder;
    private Button? _holdArrow;
    private DispatcherTimer? _holdTimer;
    private bool _openedLabelsMenu;
    private bool _menuLightDismiss;

    public TaskStackDrag(TasksView page, Popup labelsMenu, Control moreChip)
    {
        _page = page;
        _labelsMenu = labelsMenu;
        _moreChip = moreChip;
    }

    /// <summary>A stack is being dragged.</summary>
    public bool IsDragging => _dragging;

    private TasksViewModel? Page => _page.DataContext as TasksViewModel;

    /// <summary>A pointer press anywhere on the page (tunnel, handled ones too): a left press on a Shelf row arms a drag.</summary>
    public void OnPressed(PointerPressedEventArgs e)
    {
        _pressedRow = null;
        if (_dragging || Page is null || !e.GetCurrentPoint(_page).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (ShelfRowAt(e.Source as Visual) is { } row)
        {
            _pressedRow = row;
            _pressedAt = e.GetPosition(_page);
        }
    }

    /// <summary>The pointer moved (tunnel, handled ones too): an armed press becomes a drag after 6 px; a drag follows the pointer.</summary>
    public void OnMoved(PointerEventArgs e)
    {
        if (_dragging)
        {
            Track(e);
            e.Handled = true;
            return;
        }

        if (_pressedRow is not { } row)
        {
            return;
        }

        if (!e.GetCurrentPoint(_page).Properties.IsLeftButtonPressed)
        {
            _pressedRow = null;
            return;
        }

        Vector moved = e.GetPosition(_page) - _pressedAt;
        if (Math.Abs(moved.X) + Math.Abs(moved.Y) < StartDistance)
        {
            return;
        }

        _pressedRow = null;
        Begin(row, e);
    }

    /// <summary>The button went up: a drag drops where it is; an armed press that never moved is left to be a click.</summary>
    public void OnReleased(PointerReleasedEventArgs e)
    {
        _pressedRow = null;
        if (!_dragging)
        {
            return;
        }

        Track(e);
        StackDropTarget? target = _target;
        e.Handled = true;
        Finish();
        _ = Page?.DropStackAsync(target);
    }

    /// <summary>The page lost the pointer it held (another control took it, or the system did): the drag is cancelled.</summary>
    public void OnCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_dragging && !_ending && ReferenceEquals(e.Source, _page))
        {
            Cancel();
        }
    }

    /// <summary>Esc, the window losing focus, the page hidden: the drag stops, nothing changes, the selection stays.</summary>
    public void Cancel()
    {
        _pressedRow = null;
        if (!_dragging)
        {
            return;
        }

        Finish();
        Page?.EndStackDrag();
    }

    // ---- Starting and ending ----

    private void Begin(TaskListRowViewModel row, PointerEventArgs e)
    {
        if (Page is not { } page)
        {
            return;
        }

        // The page holds the pointer from now on: the row's own press lets go, so letting go is never also a click. Taken before the drag
        // lights anything, so whatever the row's press undoes when it loses the pointer can't put out what the drag lit.
        _ending = true;
        e.Pointer.Capture(_page);
        _ending = false;
        if (!page.BeginStackDrag(row))
        {
            e.Pointer.Capture(null);
            return;
        }

        _dragging = true;
        _pointer = e.Pointer;

        // No tooltips pop up under the stack (a row's "142 · title", a chip's name) while it moves over the page.
        _page.CloseToolTipsUnder();
        ToolTip.SetServiceEnabled(_page, false);
        _window = TopLevel.GetTopLevel(_page) as Window;
        _sizePin = WindowSizePin.Pin(_window);
        if (_window is not null)
        {
            _window.Deactivated += OnWindowDeactivated;
            _window.AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        // The +N list must not close under the stack (it opens while the stack is held on +N and its rows take the drop).
        _menuLightDismiss = _labelsMenu.IsLightDismissEnabled;
        _labelsMenu.IsLightDismissEnabled = false;
        _layer = OverlayLayer.GetOverlayLayer(_page);
        if (_layer is not null)
        {
            _stack = new TaskDragStackView { DataContext = page, ZIndex = 10_000 };
            _layer.Children.Add(_stack);
        }

        Track(e);
    }

    private void Finish()
    {
        _ending = true;
        try
        {
            StopHold();
            _arrowUnder = null;
            _target = null;
            if (_stack is not null)
            {
                _layer?.Children.Remove(_stack);
                _stack = null;
            }

            _layer = null;
            _sizePin?.Dispose();
            _sizePin = null;
            if (_window is not null)
            {
                _window.Deactivated -= OnWindowDeactivated;
                _window.RemoveHandler(InputElement.KeyDownEvent, OnWindowKeyDown);
                _window = null;
            }

            _labelsMenu.IsLightDismissEnabled = _menuLightDismiss;
            if (_openedLabelsMenu && Page is { } page)
            {
                page.Shelf.IsLabelsMenuOpen = false;
            }

            _openedLabelsMenu = false;
            _page.ClearValue(ToolTip.ServiceEnabledProperty);
            _dragging = false;
            if (_pointer is { } pointer && ReferenceEquals(pointer.Captured, _page))
            {
                pointer.Capture(null);
            }

            _pointer = null;
        }
        finally
        {
            _ending = false;
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => Cancel();

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dragging && e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel();
        }
    }

    // ---- Following the pointer ----

    private void Track(PointerEventArgs e)
    {
        if (_layer is not null && _stack is not null)
        {
            Point at = e.GetPosition(_layer) + StackOffset - new Vector(TaskDragStackView.StackRoom, TaskDragStackView.StackRoom);
            Canvas.SetLeft(_stack, at.X);
            Canvas.SetTop(_stack, at.Y);
        }

        if (TopLevel.GetTopLevel(_page) is { } top)
        {
            ShowTarget(TargetAt(top, e.GetPosition(top)));
        }
    }

    private void ShowTarget(StackDropTarget? target)
    {
        Button? arrow = target is { Kind: StackDropKind.PageArrow } ? _arrowUnder : null;
        if (Equals(target, _target) && ReferenceEquals(arrow, _holdArrow))
        {
            return;
        }

        _target = target;
        Page?.ShowStackTarget(target);
        HoldOn(arrow);
        if (target is { Kind: StackDropKind.More } && Page is { Shelf.IsLabelsMenuOpen: false } page)
        {
            // 8b.27: held on +N the full label list opens; its rows take the drop too. It closes after the drop.
            page.Shelf.IsLabelsMenuOpen = true;
            _openedLabelsMenu = true;
        }
    }

    // What the stack is over: the topmost place that takes it, else null (empty space, the bar between its zones, a column that doesn't take it).
    private StackDropTarget? TargetAt(TopLevel top, Point at)
    {
        _arrowUnder = null;
        if (Page is not { } page || top.InputHitTest(at) is not Visual hit)
        {
            return null;
        }

        Control? rowItem = null;
        foreach (Visual visual in hit.GetSelfAndVisualAncestors())
        {
            if (visual is not Control control)
            {
                continue;
            }

            switch (control.Name)
            {
                case "DragChooseZone":
                    return new StackDropTarget(StackDropKind.Choose);
                case "DragCancelZone":
                    return new StackDropTarget(StackDropKind.Cancel);
            }

            if (control is Button arrow && PageTurnHover.GetStep(arrow) is int step and not 0)
            {
                _arrowUnder = arrow;
                return new StackDropTarget(StackDropKind.PageArrow, PageStep: step);
            }

            if (ReferenceEquals(control, _moreChip))
            {
                return new StackDropTarget(StackDropKind.More);
            }

            if (control.Classes.Contains("todayStrip"))
            {
                return new StackDropTarget(StackDropKind.Today);
            }

            if (control is ToggleButton { DataContext: LabelFilterChipViewModel chip } && (control.Classes.Contains("labelChip") || control.Classes.Contains("lmLabel")))
            {
                return new StackDropTarget(StackDropKind.Label, LabelId: chip.Id, LabelName: chip.Name);
            }

            if (control is ToggleButton && control.Classes.Contains("lmNone"))
            {
                return new StackDropTarget(StackDropKind.NoLabel);
            }

            if (control is LabelsMenuView)
            {
                return null;
            }

            if (rowItem is null && control is Panel { DataContext: TaskListRowViewModel } && control.Classes.Contains("listItem"))
            {
                rowItem = control;
            }

            if (control is TaskColumnView { DataContext: TaskSectionViewModel column })
            {
                if (!column.IsStackLit)
                {
                    return null;
                }

                // On its own column (a reorder) the line shows above or below the row under the pointer, by its half.
                if (rowItem is { DataContext: TaskListRowViewModel row } && column.Rows.Contains(row) && page.WouldReorderStack(column)
                    && top.TranslatePoint(at, rowItem) is Point local)
                {
                    return new StackDropTarget(StackDropKind.Column, column, row, local.Y > rowItem.Bounds.Height / 2);
                }

                return new StackDropTarget(StackDropKind.Column, column);
            }

            if (control is TasksView)
            {
                return null;
            }
        }

        return null;
    }

    // A Shelf column's task row under a press (its Panel.listItem inside a TaskColumnView of this page), else null.
    private TaskListRowViewModel? ShelfRowAt(Visual? source)
    {
        TaskListRowViewModel? row = null;
        for (Visual? visual = source; visual is not null && !ReferenceEquals(visual, _page); visual = visual.GetVisualParent())
        {
            if (row is null && visual is Panel { DataContext: TaskListRowViewModel candidate } panel && panel.Classes.Contains("listItem"))
            {
                row = candidate;
            }

            if (visual is TaskColumnView)
            {
                return row;
            }
        }

        return null;
    }

    // ---- Holding the stack on a page arrow (8b.39-8b.42) ----

    private void HoldOn(Button? arrow)
    {
        if (ReferenceEquals(arrow, _holdArrow))
        {
            return;
        }

        StopHold();
        if (arrow is null)
        {
            return;
        }

        _holdArrow = arrow;
        arrow.Classes.Add("stackHot");
        if (CanTurn(arrow))
        {
            // The fill rises from the bottom over the 2 s (a style animation that repeats every 2 s, in step with the timer).
            arrow.Classes.Add("stackHold");
            _holdTimer = new DispatcherTimer { Interval = HoldTime };
            _holdTimer.Tick += OnHoldTick;
            _holdTimer.Start();
        }
    }

    private void OnHoldTick(object? sender, EventArgs e)
    {
        if (_holdArrow is not { } arrow || !CanTurn(arrow))
        {
            StopTimer();
            return;
        }

        arrow.Command!.Execute(null);
        if (!CanTurn(arrow))
        {
            // The first or last page: no fill, nothing more to turn to (8b.42).
            arrow.Classes.Remove("stackHold");
            StopTimer();
        }
    }

    private static bool CanTurn(Button arrow) => arrow.Command is { } command && command.CanExecute(null);

    private void StopHold()
    {
        StopTimer();
        if (_holdArrow is not null)
        {
            _holdArrow.Classes.Remove("stackHot");
            _holdArrow.Classes.Remove("stackHold");
            _holdArrow = null;
        }
    }

    private void StopTimer()
    {
        if (_holdTimer is not null)
        {
            _holdTimer.Stop();
            _holdTimer.Tick -= OnHoldTick;
            _holdTimer = null;
        }
    }
}
