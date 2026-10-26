using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

public partial class ClassifyStackPrompt : Window
{
    public ClassifyStackPrompt()
    {
        InitializeComponent();
        // Tunnel, so the TextBox can't take Enter before this sees it.
        ReasonBox.AddHandler(KeyDownEvent, OnReasonKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Enter in the reason box is Done.</summary>
    private void OnReasonKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ClassifyStackViewModel viewModel && viewModel.DoneCommand.CanExecute(null))
        {
            viewModel.DoneCommand.Execute(null);
            e.Handled = true;
        }
    }
}
