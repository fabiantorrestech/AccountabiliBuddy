using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>The Unlinked dialog (ADR 54): the three choices; closes itself once one of them took effect. Closing it by hand resumes nothing.</summary>
public partial class UnlinkedDialog : Window
{
    private UnlinkedViewModel? _viewModel;

    public UnlinkedDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.Resolved -= OnResolved;
            }

            _viewModel = DataContext as UnlinkedViewModel;
            if (_viewModel is not null)
            {
                _viewModel.Resolved += OnResolved;
            }
        };

        // The view model is shared with the Devices page and outlives this window: a closed window must not be closed again.
        Closed += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.Resolved -= OnResolved;
            }
        };
    }

    private void OnResolved(object? sender, EventArgs e) => Close();
}
