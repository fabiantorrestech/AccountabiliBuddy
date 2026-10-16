using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

public partial class NudgeToast : Window
{
    private readonly DispatcherTimer _autoClose = new() { Interval = NudgeToastViewModel.DisplayTime };

    public NudgeToast()
    {
        InitializeComponent();

        _autoClose.Tick += (_, _) => Close();
        Opened += (_, _) => _autoClose.Start();
        Closed += (_, _) => _autoClose.Stop();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is NudgeToastViewModel viewModel)
            {
                viewModel.CloseRequested += (_, _) => Close();
            }
        };
    }
}
