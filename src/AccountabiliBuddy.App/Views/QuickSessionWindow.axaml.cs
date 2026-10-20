using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The quick-session card. Keyboard first: Enter starts, Esc closes, 1–5 pick a preset, ←/→ change the length
/// by 5 minutes (Shift: 1 minute), B toggles the break. Holding Shift also makes the slider move in 1-minute steps.
/// While the end-time editor is open, Enter applies the time and Esc cancels the edit.
/// Clicking anywhere else closes the card. The controls themselves are <see cref="QuickStartPanel"/>.
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
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        // Shift may have been pressed before the card had focus; pointer events carry the current modifiers.
        AddHandler(PointerMovedEvent, OnPreviewPointer, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPreviewPointer, RoutingStrategies.Tunnel);
        Deactivated += OnDeactivated;
    }

    /// <summary>Raised when the popup loses focus to another window (a click elsewhere).</summary>
    public event EventHandler? FocusLost;

    private QuickSessionViewModel? ViewModel => DataContext as QuickSessionViewModel;

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Drop-downs (profile list, time picker) open in their own popups; that must not count as clicking away.
        // The time picker cannot report whether it is open, so the card stays while the end-time editor is open;
        // the blurred backdrop and Esc still close it.
        if (!StartPanel.IsProfileDropDownOpen && ViewModel?.IsEditingUntil != true)
        {
            FocusLost?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPreviewPointer(object? sender, PointerEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.FineAdjust = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        }
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (ViewModel is { } viewModel && e.Key is Key.LeftShift or Key.RightShift)
        {
            viewModel.FineAdjust = false;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        QuickSessionViewModel? viewModel = ViewModel;
        if (viewModel is null || StartPanel.IsProfileDropDownOpen)
        {
            return;
        }

        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            viewModel.FineAdjust = true;
            return;
        }

        if (viewModel.IsEditingUntil)
        {
            HandleEditorKey(viewModel, e);
            return;
        }

        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
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
                viewModel.NudgeFocus(-1, fine: shift);
                break;
            case Key.Right:
                viewModel.NudgeFocus(1, fine: shift);
                break;
            case Key.B:
                viewModel.BreakEnabled = !viewModel.BreakEnabled;
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>In the end-time editor only Enter and Esc are special; everything else is typing.</summary>
    private static void HandleEditorKey(QuickSessionViewModel viewModel, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                viewModel.ApplyUntilCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                viewModel.CancelEditUntilCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
