using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Shelf's columns (round 2 R3). Code-behind: the Shelf's dialogs open over the window: Create / Edit view (from the Views and
/// Options menus) and the weekly tidy-up.
/// </summary>
public partial class ShelfView : UserControl
{
    private ShelfViewModel? _subscribed;

    public ShelfView()
    {
        InitializeComponent();
        // Tab walking out of the columns' end goes into the neighbouring page (before the focus manager moves it).
        AddHandler(KeyDownEvent, OnTabKeyDown, RoutingStrategies.Tunnel);
    }

    // ---- Paging (round 2 PG1) ----

    /// <summary>
    /// Turns the page by <paramref name="delta"/> (Ctrl+PageUp / Ctrl+PageDown, the buttons' twin). When the keyboard is in a column
    /// (<paramref name="keepKeyboard"/>), it goes to the header of the new page's first column, so the page you leave does not take it away.
    /// False at an end.
    /// </summary>
    public bool TurnPage(int delta, bool keepKeyboard)
    {
        if (_subscribed is null || !_subscribed.Pager.Step(delta))
        {
            return false;
        }

        if (keepKeyboard)
        {
            string? key = (delta > 0 ? _subscribed.Pager.KeysOnPage.FirstOrDefault() : _subscribed.Pager.KeysOnPage.LastOrDefault());
            if (key is not null)
            {
                FocusColumnLater(key);
            }
        }

        return true;
    }

    /// <summary>The keyboard is inside one of the columns (not on the page bar).</summary>
    public bool KeyboardIsInColumns()
    {
        return TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused && ColumnsOf(focused) is not null;
    }

    private TaskColumnView? ColumnsOf(Visual visual) => visual.GetSelfAndVisualAncestors().OfType<TaskColumnView>().FirstOrDefault();

    private void OnTabKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift) || _subscribed is not { Pager.HasPages: true } shelf
            || TopLevel.GetTopLevel(this)?.FocusManager is not { } focus || focus.GetFocusedElement() is not Visual focused || ColumnsOf(focused) is null)
        {
            return;
        }

        bool forward = e.KeyModifiers == KeyModifiers.None;
        if (focus.FindNextElement(forward ? NavigationDirection.Next : NavigationDirection.Previous) is Visual next && ColumnsOf(next) is not null)
        {
            return;
        }

        if (TurnPage(forward ? 1 : -1, keepKeyboard: true))
        {
            e.Handled = true;
        }
    }

    private void FocusColumnLater(string key)
    {
        // The columns lay out again with the new page first; then the keyboard goes to that column's header.
        Dispatcher.UIThread.Post(() =>
        {
            TaskColumnView? view = this.GetVisualDescendants().OfType<TaskColumnView>()
                .FirstOrDefault(candidate => candidate.DataContext is TaskSectionViewModel section && section.Key == key && candidate.IsEffectivelyVisible);
            view?.FocusHeader();
        }, DispatcherPriority.Loaded);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.ViewEditorRequested -= OnViewEditorRequested;
            _subscribed.TidyUpRequested -= OnTidyUpRequested;
        }

        _subscribed = DataContext as ShelfViewModel;
        if (_subscribed is not null)
        {
            _subscribed.ViewEditorRequested += OnViewEditorRequested;
            _subscribed.TidyUpRequested += OnTidyUpRequested;
        }
    }

    // ---- Tasks redesign 4c: the handle between Next and To sort (14 px over the gap; drag or ← / → to resize, 30–75%, this PC only) ----

    // How far ← / → move the split.
    private const double SplitKeyStep = 0.02;

    private TaskColumnsPanel? _columnsPanel;
    private TaskColumnSplitPair? _dragPair;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        HookColumnsPanel();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arranged = base.ArrangeOverride(finalSize);
        HookColumnsPanel();
        return arranged;
    }

    // The columns' panel is made by the ItemsControl: once it exists, the handle follows its layout.
    private void HookColumnsPanel()
    {
        if (Columns.ItemsPanelRoot is not TaskColumnsPanel panel || ReferenceEquals(panel, _columnsPanel))
        {
            return;
        }

        if (_columnsPanel is not null)
        {
            _columnsPanel.SplitHandleChanged -= OnSplitHandleChanged;
            _columnsPanel.SizeChanged -= OnColumnsSizeChanged;
        }

        _columnsPanel = panel;
        panel.SplitHandleChanged += OnSplitHandleChanged;
        panel.SizeChanged += OnColumnsSizeChanged;
        PlaceHandle();
    }

    private void OnSplitHandleChanged(object? sender, EventArgs e) => PlaceHandle();

    private void OnColumnsSizeChanged(object? sender, SizeChangedEventArgs e) => PlaceHandle();

    private void PlaceHandle()
    {
        if (_columnsPanel?.SplitHandle is not { } pair)
        {
            SplitHandle.IsVisible = false;
            return;
        }

        SplitHandle.IsVisible = true;
        SplitHandle.Height = _columnsPanel.Bounds.Height;
        Canvas.SetLeft(SplitHandle, pair.CenterX - SplitHandle.Width / 2);
        Canvas.SetTop(SplitHandle, 0);
    }

    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_columnsPanel?.SplitHandle is not { } pair || !e.GetCurrentPoint(SplitHandle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragPair = pair;
        e.Pointer.Capture(SplitHandle);
        e.Handled = true;
    }

    private void OnHandleMoved(object? sender, PointerEventArgs e)
    {
        if (_dragPair is not { } pair || _subscribed is null || _columnsPanel is null || pair.PairWidth <= 0)
        {
            return;
        }

        // The left column ends half a gap before the pointer; Next's share follows from which of the two is on the left.
        double leftWidth = e.GetPosition(_columnsPanel).X - pair.PairLeft - _columnsPanel.Spacing / 2;
        double leftShare = leftWidth / pair.PairWidth;
        _subscribed.ShelfSplit = ClampSplit(pair.LeadOnLeft ? leftShare : 1 - leftShare);
        e.Handled = true;
    }

    private void OnHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragPair is not null && _subscribed is not null)
        {
            _ = _subscribed.SaveShelfSplitAsync(_subscribed.ShelfSplit);
        }

        _dragPair = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnHandleCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_dragPair is not null && _subscribed is not null)
        {
            _ = _subscribed.SaveShelfSplitAsync(_subscribed.ShelfSplit);
        }

        _dragPair = null;
    }

    // ← / → on the focused handle: the line between the two columns moves left or right.
    private void OnHandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Left or Key.Right) || _subscribed is null || _columnsPanel?.SplitHandle is not { } pair)
        {
            return;
        }

        double towardRight = e.Key == Key.Right ? SplitKeyStep : -SplitKeyStep;
        _ = _subscribed.SaveShelfSplitAsync(ClampSplit(_subscribed.ShelfSplit + (pair.LeadOnLeft ? towardRight : -towardRight)));
        e.Handled = true;
    }

    private static double ClampSplit(double share)
    {
        return Math.Clamp(share, Services.Tasks.TaskLayoutPreferences.MinShelfSplit / 100.0, Services.Tasks.TaskLayoutPreferences.MaxShelfSplit / 100.0);
    }

    // "Create a view…" (null) or "Edit view…" from the Views or Options menu.
    private void OnViewEditorRequested(object? sender, Core.Domain.TaskView? view)
    {
        if (_subscribed?.ViewEditor(view) is { } editor)
        {
            _ = ViewEditorDialog.ShowAsync(this, editor);
        }
    }

    private void OnTidyUpRequested(object? sender, TidyUpViewModel tidy)
    {
        _ = TidyUpDialog.ShowAsync(this, tidy);
    }
}
