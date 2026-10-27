using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>The Setup DP Assistant: a guided day plan with sensible defaults (opened from the Day plan tab).</summary>
public partial class DpAssistantWindow : Window
{
    public DpAssistantWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is DpAssistantViewModel viewModel)
            {
                viewModel.CloseRequested += (_, _) => Close();
            }
        };
    }
}
