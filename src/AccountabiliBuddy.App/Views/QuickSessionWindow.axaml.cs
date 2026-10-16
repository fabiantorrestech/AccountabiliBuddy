using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The quick-session card. Keyboard first: Enter starts, Esc closes, 1–5 pick a preset, ←/→ change the length
/// by 5 minutes, B toggles the break. Clicking anywhere else closes it.
/// </summary>
public partial class QuickSessionWindow : Window
{
    public QuickSessionWindow()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            Card.Opacity = 1;
            Card.RenderTransform = TransformOperations.Parse("scale(1)");
            Activate();
            Focus();
        };

        // Tunnel so the shortcuts work wherever focus is inside the card (sliders would otherwise eat the arrows).
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Deactivated += OnDeactivated;
    }

    /// <summary>Raised when the popup loses focus to another window (a click elsewhere).</summary>
    public event EventHandler? FocusLost;

    private QuickSessionViewModel? ViewModel => DataContext as QuickSessionViewModel;

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // The profile drop-down lives in its own popup; opening it must not count as clicking away.
        if (ProfileBox?.IsDropDownOpen != true)
        {
            FocusLost?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        QuickSessionViewModel? viewModel = ViewModel;
        if (viewModel is null || ProfileBox?.IsDropDownOpen == true)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                viewModel.CloseCommand.Execute(null);
                break;
            case Key.Enter:
                if (viewModel.StartCommand.CanExecute(null))
                {
                    viewModel.StartCommand.Execute(null);
                }

                break;
            case >= Key.D1 and <= Key.D5:
                viewModel.SelectPresetByNumber(e.Key - Key.D1 + 1);
                break;
            case >= Key.NumPad1 and <= Key.NumPad5:
                viewModel.SelectPresetByNumber(e.Key - Key.NumPad1 + 1);
                break;
            case Key.Left:
                viewModel.NudgeFocus(-5);
                break;
            case Key.Right:
                viewModel.NudgeFocus(5);
                break;
            case Key.B:
                viewModel.BreakEnabled = !viewModel.BreakEnabled;
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
