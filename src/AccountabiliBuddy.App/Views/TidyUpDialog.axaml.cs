using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The weekly tidy-up flow (spec 05 §9). Code-behind: the keys (K N A D or 1 to 4 decide, ↑/↓ move the highlight, Enter takes it, ←/→
/// step; read before the focused button sees them), the pointer over a decision highlights it, and closing keeps what was decided.
/// </summary>
public partial class TidyUpDialog : TaskDialogWindow
{
    public TidyUpDialog()
    {
        InitializeComponent();
    }

    public TidyUpDialog(TidyUpViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        Closed += (_, _) => viewModel.Closed();
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
    }

    public static Task ShowAsync(Visual? opener, TidyUpViewModel viewModel)
    {
        return new TidyUpDialog(viewModel).ShowOverAsync(opener);
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TidyUpViewModel tidy || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (tidy.IsFinished)
        {
            if (e.Key == Key.Enter)
            {
                tidy.DoneCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        TidyUpChoice? choice = e.Key switch
        {
            Key.K or Key.D1 or Key.NumPad1 => TidyUpChoice.Keep,
            Key.N or Key.D2 or Key.NumPad2 => TidyUpChoice.MoveToNext,
            Key.A or Key.D3 or Key.NumPad3 => TidyUpChoice.Archive,
            Key.D or Key.D4 or Key.NumPad4 => TidyUpChoice.Delete,
            _ => null,
        };
        if (choice is TidyUpChoice picked)
        {
            _ = tidy.ChooseAsync(picked);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                tidy.MoveHighlight(-1);
                e.Handled = true;
                break;
            case Key.Down:
                tidy.MoveHighlight(1);
                e.Handled = true;
                break;
            case Key.Left:
                Run(tidy.BackCommand);
                e.Handled = true;
                break;
            case Key.Right:
                Run(tidy.ForwardCommand);
                e.Handled = true;
                break;
            case Key.Enter when !IsOtherButton(e.Source):
                _ = tidy.ChooseLitAsync();
                e.Handled = true;
                break;
        }
    }

    // Enter on Stop for now or an arrow button is that button's own.
    private static bool IsOtherButton(object? source)
    {
        return source is Button { DataContext: not TidyUpDecisionViewModel };
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
        if (sender is Control { DataContext: TidyUpDecisionViewModel decision } && DataContext is TidyUpViewModel tidy)
        {
            tidy.Highlight(tidy.Decisions.ToList().IndexOf(decision));
        }
    }
}
