using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Goals;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The weekly goals review (round 2 R5.4). Code-behind: the keys (1 to 4 answer, ↑/↓ move the highlight, Enter takes it, ←/→ step; read
/// before the focused button sees them; Esc closes as "Stop for now"), the pointer over a decision highlights it, and closing keeps the
/// answers given.
/// </summary>
public partial class GoalReviewDialog : TaskDialogWindow
{
    public GoalReviewDialog()
    {
        InitializeComponent();
    }

    public GoalReviewDialog(GoalReviewViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        Closed += (_, _) => viewModel.Closed();
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not GoalReviewViewModel review || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (review.IsFinished)
        {
            if (e.Key == Key.Enter)
            {
                review.DoneCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        GoalReviewChoice? choice = e.Key switch
        {
            Key.D1 or Key.NumPad1 => GoalReviewChoice.KeepGoing,
            Key.D2 or Key.NumPad2 => GoalReviewChoice.Adjust,
            Key.D3 or Key.NumPad3 => GoalReviewChoice.Pause,
            Key.D4 or Key.NumPad4 => GoalReviewChoice.DecideNextWeek,
            _ => null,
        };
        if (choice is GoalReviewChoice picked)
        {
            _ = review.ChooseAsync(picked);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                review.MoveHighlight(-1);
                e.Handled = true;
                break;
            case Key.Down:
                review.MoveHighlight(1);
                e.Handled = true;
                break;
            case Key.Left:
                Run(review.BackCommand);
                e.Handled = true;
                break;
            case Key.Right:
                Run(review.ForwardCommand);
                e.Handled = true;
                break;
            case Key.Enter when !IsOtherButton(e.Source):
                _ = review.ChooseLitAsync();
                e.Handled = true;
                break;
        }
    }

    // Enter on Stop for now, × or an arrow button is that button's own.
    private static bool IsOtherButton(object? source)
    {
        return source is Button { DataContext: not GoalReviewDecisionViewModel };
    }

    private static void Run(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private void OnDecisionPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: GoalReviewDecisionViewModel decision } && DataContext is GoalReviewViewModel review && decision.IsAvailable)
        {
            review.Highlight(review.Decisions.ToList().IndexOf(decision));
        }
    }
}
