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
            view?.HeaderButton.Focus(NavigationMethod.Directional);
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
