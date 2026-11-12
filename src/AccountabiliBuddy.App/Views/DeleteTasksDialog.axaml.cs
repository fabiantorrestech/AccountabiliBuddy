using AccountabiliBuddy.App.ViewModels;
using Avalonia;

namespace AccountabiliBuddy.App.Views;

/// <summary>"Delete N tasks?" for a selection (the bulk menu's Delete N).</summary>
public partial class DeleteTasksDialog : TaskDialogWindow
{
    public DeleteTasksDialog()
    {
        InitializeComponent();
    }

    public DeleteTasksDialog(DeleteTasksViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
    }

    public static Task ShowAsync(Visual? opener, DeleteTasksViewModel viewModel)
    {
        return new DeleteTasksDialog(viewModel).ShowOverAsync(opener);
    }
}
