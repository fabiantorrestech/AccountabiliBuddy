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
        // It can't be skipped: only answering (Keep) closes it.
        if (DataContext is PickTasksViewModel { IsDone: false })
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
