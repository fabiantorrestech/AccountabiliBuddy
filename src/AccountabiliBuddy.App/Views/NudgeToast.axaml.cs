using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

public partial class NudgeToast : Window
{
    private readonly DispatcherTimer _autoClose = new();

    public NudgeToast()
    {
        InitializeComponent();

        _autoClose.Tick += (_, _) =>
        {
            _autoClose.Stop();
            (DataContext as NudgeToastViewModel)?.RaiseExpired();
            Close();
        };
        Opened += (_, _) =>
        {
            if (DataContext is NudgeToastViewModel { StaysOpen: false } shown)
            {
                _autoClose.Interval = shown.LifeTime;
                _autoClose.Start();
            }
        };
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
