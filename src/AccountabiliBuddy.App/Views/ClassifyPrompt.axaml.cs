using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;

namespace AccountabiliBuddy.App.Views;

public partial class ClassifyPrompt : Window
{
    public ClassifyPrompt()
    {
        InitializeComponent();
        ReasonBox.KeyDown += OnReasonKeyDown;
    }

    /// <summary>Enter in the reason box is Done.</summary>
    private void OnReasonKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ClassifyPromptViewModel viewModel && viewModel.DoneCommand.CanExecute(null))
        {
            viewModel.DoneCommand.Execute(null);
            e.Handled = true;
        }
    }
}
