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
/// The Tasks page. Code-behind: a click on the Today pill opens Today (round 2 R3), Esc in the header's search clears it, the naming
/// row's box taking the keyboard when a picker asks for it, and the dialogs the rows and menus ask for (Rename one by one, Delete N,
/// Changed on both devices). The open task's editor is the popup (<see cref="TaskEditorView"/>), with its own code-behind.
/// </summary>
public partial class TasksView : UserControl
{
    /// <summary>True → 1, false → 0: the reset button keeps its slot while hidden.</summary>
    public static readonly Avalonia.Data.Converters.IValueConverter ShownOpacity =
        new Avalonia.Data.Converters.FuncValueConverter<bool, double>(shown => shown ? 1 : 0);

    private TasksViewModel? _subscribed;

    public TasksView()
    {
        InitializeComponent();
        // The reset button's own press and release (a Button handles them itself, so listen to handled ones too).
        ResetButton.AddHandler(PointerPressedEvent, OnResetPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        ResetButton.AddHandler(PointerReleasedEvent, OnResetReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        ResetButton.AddHandler(PointerCaptureLostEvent, OnResetCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        ResetButton.AddHandler(KeyDownEvent, OnResetKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        ResetButton.AddHandler(KeyUpEvent, OnResetKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        LabelsMenu.Dismissed += (_, _) => MoreLabelsButton.Focus(NavigationMethod.Directional);
        // The Archive sheet went down with the keyboard in it: back to the Archive row.
        ArchiveSheet.Closed += (_, _) => ArchiveRowButton.Focus(NavigationMethod.Directional);
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
            _subscribed.FocusRequested += OnFocusRequested;
            _subscribed.RenameEachRequested += OnRenameEachRequested;
            _subscribed.DeleteManyRequested += OnDeleteManyRequested;
            _subscribed.ConflictRequested += OnConflictRequested;
            _subscribed.SessionFullRequested += OnSessionFullRequested;
            _subscribed.FocusRowRequested += OnFocusRowRequested;
            _subscribed.IsOnScreen = IsEffectivelyVisible && VisualRoot is not null;
        }
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

    // ---- The Today pill and the header's search (Tasks redesign 4a) ----

    // A click on the Today pill (not on its Open Today button, which says so itself) opens Today too (change note §2: selecting it jumps there).
    private void OnTodayStripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && DataContext is TasksViewModel page)
        {
            page.TodayStrip.OpenTodayCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Esc in the header's search clears it.
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is TasksViewModel { Shelf.IsSearching: true } page)
        {
            page.Shelf.ClearSearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    // A picker's "New category…" or "New tag…": the naming row's box takes the keyboard (the editor popup's boxes are its own).
    private void OnFocusRequested(object? sender, string target)
    {
        if (target != "newCategory")
        {
            return;
        }

        Dispatcher.UIThread.Post(() => NewCategoryBox.Focus(), DispatcherPriority.Background);
    }

    // ---- The label row's +N menu (Tasks redesign 4b) ----

    // When the menu last closed: a click on +N that closed it by light dismiss must not open it again at once.
    private long _labelsMenuClosedAt;

    // +N was pressed from the keyboard (Enter or Space): the menu's first row shows its focus ring.
    private bool _labelsMenuFromKeyboard;

    private void OnMoreLabelsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TasksViewModel page || Environment.TickCount64 - _labelsMenuClosedAt < 250)
        {
            return;
        }

        _labelsMenuFromKeyboard = !MoreLabelsButton.IsPointerOver;
        page.Shelf.IsLabelsMenuOpen = !page.Shelf.IsLabelsMenuOpen;
        e.Handled = true;
    }

    // Opened: at most as tall as the room under the row (8 clear of the window's bottom; its list scrolls), and the keyboard goes in.
    private void OnLabelsMenuOpened(object? sender, EventArgs e)
    {
        if (TopLevel.GetTopLevel(LabelRow) is { } top && LabelRow.TranslatePoint(new Point(0, LabelRow.Bounds.Height), top) is Point bottom)
        {
            LabelsMenu.MaxHeight = Math.Max(160, top.ClientSize.Height - bottom.Y - 8 - 8);
        }

        bool fromKeyboard = _labelsMenuFromKeyboard;
        _labelsMenuFromKeyboard = false;
        Dispatcher.UIThread.Post(() => LabelsMenu.FocusFirst(fromKeyboard), DispatcherPriority.Input);
    }

    private void OnLabelsMenuClosed(object? sender, EventArgs e)
    {
        _labelsMenuClosedAt = Environment.TickCount64;
    }

    // Esc clears the selection; otherwise it closes the editor popup, unless a text box has the focus: Esc there is left to the edit.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || DataContext is not TasksViewModel pager)
        {
            return;
        }

        // PG1: Ctrl+PageUp / Ctrl+PageDown turn the Shelf's column page (the keyboard stays in the columns when it was in them).
        if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.PageUp or Key.PageDown && pager.Shelf.Pager.HasPages
            && this.GetVisualDescendants().OfType<ShelfView>().FirstOrDefault() is { } shelf)
        {
            shelf.TurnPage(e.Key == Key.PageDown ? 1 : -1, shelf.KeyboardIsInColumns());
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape || DataContext is not TasksViewModel page)
        {
            return;
        }

        if (page.ClearSelection())
        {
            e.Handled = true;
            return;
        }

        bool editing = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;
        if (!editing && page.IsArchiveOpen && !page.ShowsEditorOnTasks)
        {
            // The Archive sheet is open (6.03): Esc closes it (inside the sheet, its own handler already did).
            page.IsArchiveOpen = false;
            e.Handled = true;
            return;
        }

        if (!editing && page.ShowsEditorOnTasks)
        {
            page.CloseEditorCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ---- The page header (round 2, brief §3.3): Views and Options (History went into the Archive sheet, Tasks redesign item 6) ----

    // Views (bookmark): the saved views, Create a view… and This view ›.
    private void OnViewsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control button && DataContext is TasksViewModel tasks)
        {
            TaskMenus.ShowEntries(button, tasks.Shelf.SavedViewsMenu(), PlacementMode.BottomEdgeAlignedRight);
            e.Handled = true;
        }
    }

    // Options (tune; board 09): the panel menu under the button, aligned to its right edge, floating over the page (nothing moves).
    private void OnTasksOptionsRequested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TasksViewModel tasks)
        {
            return;
        }

        Control anchor = TasksHeader.OptionsAnchor;
        TasksHeader.SetOptionsOpen(true);
        TaskMenus.ShowEntries(anchor, tasks.TasksOptionsMenu(), PlacementMode.BottomEdgeAlignedRight, null, () => TasksHeader.SetOptionsOpen(false), panelLook: true);
        e.Handled = true;
    }
}
