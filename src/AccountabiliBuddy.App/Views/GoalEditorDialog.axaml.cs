using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// Edit goal (about 940 × 740) and New goal (about 940 × 700), modal over the Goals page (round 2 R5.4). Closes when the view model saves,
/// creates, deletes or cancels; Delete goal… asks in <see cref="GoalConfirmDialog"/>; "+ New area" opens Edit area over it. From the goal
/// menu's Pause goal… the Active switch takes the keyboard.
/// </summary>
public partial class GoalEditorDialog : TaskDialogWindow
{
    private readonly GoalsService? _goals;

    public GoalEditorDialog()
    {
        InitializeComponent();
    }

    public GoalEditorDialog(GoalEditorViewModel viewModel, GoalsService goals)
        : this()
    {
        _goals = goals;
        DataContext = viewModel;
        SizeToContent = Avalonia.Controls.SizeToContent.Manual;
        Height = viewModel.IsNew ? 700 : 740;
        viewModel.CloseRequested += (_, _) => Close();
        viewModel.ConfirmRequested += async (_, request) => await GoalConfirmDialog.ShowAsync(this, request);
        viewModel.NewAreaRequested += async (_, _) => await NewAreaAsync(viewModel);
        if (viewModel.FocusActive)
        {
            Opened += (_, _) => Dispatcher.UIThread.Post(() => ActiveSwitch.Focus(), DispatcherPriority.Input);
        }
    }

    /// <summary>Shows the dialog; returns the goal New goal made (null for Edit or when nothing was made).</summary>
    public static async Task<Guid?> ShowAsync(Visual? opener, GoalEditorViewModel viewModel, GoalsService goals)
    {
        await new GoalEditorDialog(viewModel, goals).ShowOverAsync(opener);
        return viewModel.CreatedId;
    }

    private async Task NewAreaAsync(GoalEditorViewModel viewModel)
    {
        if (_goals is null)
        {
            return;
        }

        if (await GoalAreaDialog.ShowAsync(this, new GoalAreaEditorViewModel(_goals, null)) is Guid area)
        {
            viewModel.AreaAdded(area);
        }
    }
}
