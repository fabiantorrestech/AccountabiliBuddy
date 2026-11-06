using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>Switching to ProductaBuddy during a focus session (ADR 68), see <c>GuidanceDuringSessionViewModel</c>.</summary>
public partial class GuidanceDuringSessionDialog : Window
{
    public GuidanceDuringSessionDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is GuidanceDuringSessionViewModel viewModel)
            {
                viewModel.CloseRequested += OnCloseRequested;
            }
        };
    }

    /// <summary>True if the user proceeded; false if they cancelled or closed the window.</summary>
    public bool Confirmed { get; private set; }

    private void OnCloseRequested(object? sender, bool confirmed)
    {
        Confirmed = confirmed;
        Close(confirmed);
    }
}
