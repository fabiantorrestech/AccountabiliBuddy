using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>Asks which day plan to keep after both devices changed it; closing it uses the latest edit.</summary>
public partial class DayPlanClashDialog : Window
{
    public DayPlanClashDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is DayPlanClashViewModel viewModel)
            {
                viewModel.CloseRequested += (_, _) => Close();
            }
        };
        Closed += (_, _) => (DataContext as DayPlanClashViewModel)?.OnClosed();
    }
}
