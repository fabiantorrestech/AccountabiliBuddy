using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

/// <summary>The split minutes button of the timer's and the alarms' ringing windows (DataContext = a MinutesChoiceViewModel).</summary>
public partial class MinutesSplitButton : UserControl
{
    public MinutesSplitButton()
    {
        InitializeComponent();
    }

    private MinutesChoiceViewModel? ViewModel => DataContext as MinutesChoiceViewModel;

    /// <summary>A button of the split button or its menu: show the time its minutes would give (Tag = minutes; none = the default).</summary>
    private void OnChoiceEntered(object? sender, PointerEventArgs e)
    {
        int? minutes = sender is Control { Tag: int tagged } ? tagged : ViewModel?.DefaultMinutes;
        ViewModel?.Hover(minutes);
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
