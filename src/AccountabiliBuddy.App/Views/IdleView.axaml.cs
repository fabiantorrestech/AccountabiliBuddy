using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>Today's idle card: "Other ways to start" opens the Pomodoro form and the options flyout from its menu.</summary>
public partial class IdleView : UserControl
{
    public IdleView()
    {
        InitializeComponent();
    }

    // The menu closes first, then the Pomodoro form opens on the same button.
    private void OnPomodoroClick(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => FlyoutBase.ShowAttachedFlyout(OtherWaysButton), DispatcherPriority.Background);
    }

    // The options (profile, length, break, test session) open from the length numeral's flyout.
    private void OnOptionsClick(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => LengthButton.Flyout?.ShowAt(LengthButton), DispatcherPriority.Background);
    }

    // The options panel's fold is the flyout itself: it starts open.
    private void OnOptionsOpening(object? sender, EventArgs e)
    {
        if (DataContext is SessionViewModel viewModel)
        {
            viewModel.QuickStart.ShowsOptions = true;
        }
    }
}
