using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

public partial class EndSessionDialog : Window
{
    public EndSessionDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is EndSessionDialogViewModel viewModel)
            {
                viewModel.CloseRequested += OnCloseRequested;
            }
        };
    }

    /// <summary>True if the user confirmed; false if they cancelled or closed the window.</summary>
    public bool Confirmed { get; private set; }

    private void OnCloseRequested(object? sender, bool confirmed)
    {
        Confirmed = confirmed;
        Close(confirmed);
    }
}
