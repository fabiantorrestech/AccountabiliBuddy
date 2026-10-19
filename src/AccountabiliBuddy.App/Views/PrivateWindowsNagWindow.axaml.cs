using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

public partial class PrivateWindowsNagWindow : Window
{
    public PrivateWindowsNagWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is PrivateWindowsNagViewModel viewModel)
            {
                viewModel.CloseRequested += (_, _) => Close();
            }
        };
    }
}
