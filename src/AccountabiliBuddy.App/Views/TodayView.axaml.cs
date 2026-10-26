using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

public partial class TodayView : UserControl
{
    public TodayView()
    {
        InitializeComponent();
    }

    // Editing a come-back: the box takes the keyboard as it opens, with the old text selected.
    private void OnComebackBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
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

    // Clicking away keeps the new text, like Enter; Escape has already closed the box, so nothing is saved then.
    private void OnComebackBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ComebackRowViewModel { IsEditing: true } row })
        {
            row.SaveEditCommand.Execute(null);
        }
    }
}
