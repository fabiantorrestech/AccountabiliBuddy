using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>The "Start over" dialog (ADR 54, 10).</summary>
public partial class StartOverDialog : Window
{
    public StartOverDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is StartOverViewModel viewModel)
            {
                viewModel.CloseRequested += (_, _) => Close();
            }
        };
        Closed += (_, _) => (DataContext as StartOverViewModel)?.Dispose();
    }
}
