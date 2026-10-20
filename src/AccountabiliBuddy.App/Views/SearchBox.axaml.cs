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
    public SearchBox()
    {
        InitializeComponent();
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
            bool insideDropdown = focused is Visual visual && Dropdown.Child is Visual content
                                  && (ReferenceEquals(visual, content) || visual.IsVisualAncestorOf(content) || content.IsVisualAncestorOf(visual));
            if (!insideDropdown)
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
