using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

public partial class PickTasksWindow : Window
{
    public PickTasksWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // It can't be skipped: only answering (Keep) closes it. Quitting the app or Windows still closes it; the next start asks again.
        bool quitting = e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown;
        if (!quitting && DataContext is PickTasksViewModel { IsDone: false })
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
