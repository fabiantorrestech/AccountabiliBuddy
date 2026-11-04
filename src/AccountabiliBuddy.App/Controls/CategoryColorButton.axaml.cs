using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A task category's colour swatch and its editor flyout. Code-behind: the draft starts when the flyout opens and is kept when it closes;
/// Esc is seen on its way down, before the flyout closes itself, so that closing keeps nothing.
/// </summary>
public partial class CategoryColorButton : UserControl
{
    private Flyout? _flyout;

    public CategoryColorButton()
    {
        InitializeComponent();
    }

    private void OnEditorOpening(object? sender, EventArgs e)
    {
        _flyout = sender as Flyout;
        if (DataContext is TaskCategoryRowViewModel row)
        {
            row.ColorEditor.Begin(row.Color);
        }
    }

    private void OnEditorClosed(object? sender, EventArgs e)
    {
        if (DataContext is TaskCategoryRowViewModel row)
        {
            row.ColorEditor.Finish();
        }
    }

    private void OnEditorLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Control editor)
        {
            editor.RemoveHandler(KeyDownEvent, OnEditorKeyDown);
            editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is TaskCategoryRowViewModel row)
        {
            row.ColorEditor.Cancel();
            _flyout?.Hide();
            e.Handled = true;
        }
    }
}
