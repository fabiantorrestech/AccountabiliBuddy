using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>One column of the Archive sheet. Code-behind: filling a row's ⋯ menu when it opens, and an inline box taking the keyboard as it opens.</summary>
public partial class ArchiveColumnView : UserControl
{
    public ArchiveColumnView()
    {
        InitializeComponent();
    }

    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            TaskMenus.Show(button);
            e.Handled = true;
        }
    }

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
}
