using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>The Unlinked dialog (ADR 54): the three choices; closes itself once one of them took effect. Closing it by hand resumes nothing.</summary>
public partial class UnlinkedDialog : Window
{
    public UnlinkedDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is UnlinkedViewModel viewModel)
            {
                viewModel.Resolved += (_, _) => Close();
            }
        };
    }
}
