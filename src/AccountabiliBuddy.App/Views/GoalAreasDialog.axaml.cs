using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Goals;
using Avalonia;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

/// <summary>Goals Options › Areas (round 2 R5.4): ✎ and + open Edit area over this dialog; the list follows the store.</summary>
public partial class GoalAreasDialog : TaskDialogWindow
{
    private readonly GoalsService? _goals;

    public GoalAreasDialog()
    {
        InitializeComponent();
    }

    public GoalAreasDialog(GoalsService goals, GoalAreasViewModel viewModel)
        : this()
    {
        _goals = goals;
        DataContext = viewModel;
        viewModel.EditAreaRequested += async (_, id) => await EditAreaAsync(id);
        Closed += (_, _) => viewModel.Dispose();
    }

    public static Task ShowAsync(Visual? opener, GoalsService goals)
    {
        return new GoalAreasDialog(goals, new GoalAreasViewModel(goals)).ShowOverAsync(opener);
    }

    private async Task EditAreaAsync(Guid? id)
    {
        if (_goals is null)
        {
            return;
        }

        GoalArea? area = id is Guid areaId ? _goals.Lists.Areas.FirstOrDefault(item => item.Id == areaId && item.IsLive) : null;
        if (id is not null && area is null)
        {
            return;
        }

        await GoalAreaDialog.ShowAsync(this, new GoalAreaEditorViewModel(_goals, area));
    }

    private void OnDone(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
