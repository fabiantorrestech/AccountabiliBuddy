using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>The search box: Enter goes to the next match (or all results), Esc clears it.</summary>
public partial class SearchBox : UserControl
{
    private TopLevel? _topLevel;

    public SearchBox()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _topLevel?.RemoveHandler(PointerPressedEvent, OnWindowPointerPressed);
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    // A click anywhere else in the window takes the focus out of the box, so it shrinks (and its dropdown closes) even
    // when the click lands on something that can't take the focus itself, like a page's background. Clicks in the
    // dropdown route through the window too (its popup's events go through the box), and those must reach the result.
    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!Box.IsKeyboardFocusWithin || e.Source is not Visual clicked || ReferenceEquals(clicked, this)
            || this.IsVisualAncestorOf(clicked) || IsInDropdown(clicked))
        {
            return;
        }

        _topLevel?.FocusManager?.Focus(null);
    }

    private bool IsInDropdown(Visual visual)
    {
        return Dropdown.Child is Visual content
               && (ReferenceEquals(visual, content) || visual.IsVisualAncestorOf(content) || content.IsVisualAncestorOf(visual));
    }

    /// <summary>Puts the cursor in the box with its text selected (Ctrl+F).</summary>
    public void FocusBox()
    {
        Box.Focus();
        Box.SelectAll();
    }

    // Focus went somewhere else (the page, a tab): close the dropdown, unless it went into the dropdown itself.
    private void OnBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is not SearchViewModel search || Box.IsFocused)
            {
                return;
            }

            object? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (focused is not Visual visual || !IsInDropdown(visual))
            {
                search.IsOpen = false;
            }
        });
    }

    private void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SearchViewModel search)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            search.Submit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            search.ClearCommand.Execute(null);
            e.Handled = true;
        }
    }
}
