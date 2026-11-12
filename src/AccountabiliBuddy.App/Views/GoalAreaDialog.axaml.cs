using AccountabiliBuddy.App.ViewModels;
using Avalonia;

namespace AccountabiliBuddy.App.Views;

/// <summary>Edit area / New area (round 2 R5.4): closes when the view model saves, deletes or cancels.</summary>
public partial class GoalAreaDialog : TaskDialogWindow
{
    public GoalAreaDialog()
    {
        InitializeComponent();
    }

    public GoalAreaDialog(GoalAreaEditorViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
    }

    /// <summary>Shows the dialog; returns the area saved (new or edited), or null.</summary>
    public static async Task<Guid?> ShowAsync(Visual? opener, GoalAreaEditorViewModel viewModel)
    {
        await new GoalAreaDialog(viewModel).ShowOverAsync(opener);
        return viewModel.SavedId;
    }
}
