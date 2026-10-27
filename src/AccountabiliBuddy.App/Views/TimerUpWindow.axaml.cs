using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

public partial class TimerUpWindow : Window
{
    public TimerUpWindow()
    {
        InitializeComponent();
    }

    private TimerUpViewModel? ViewModel => DataContext as TimerUpViewModel;

    /// <summary>A button of the split button or its menu: show the time its minutes would give (Tag = minutes).</summary>
    private void OnChoiceEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { Tag: string tag } && int.TryParse(tag, out int minutes))
        {
            ViewModel?.Hover(minutes);
        }
    }

    private void OnChoiceExited(object? sender, PointerEventArgs e) => ViewModel?.Hover(null);

    /// <summary>A menu entry was picked: close the menu (the command already ran).</summary>
    private void OnChoicePicked(object? sender, RoutedEventArgs e)
    {
        ViewModel?.Hover(null);
        SplitMenu.Flyout?.Hide();
    }

    private void OnCustomKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel?.ApplyCustomCommand.Execute(null);
            e.Handled = true;
        }
    }
}
