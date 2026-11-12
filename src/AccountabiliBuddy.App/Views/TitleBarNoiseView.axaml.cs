using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The title bar's noise control. Its volume popover opens centred under the volume button with the keyboard on the slider:
/// arrows ±5, Shift+arrows ±1, Page Up/Down ±10, Home/End, a click on the track jumps there; a digit (or a click on the % chip)
/// switches the chip to a box to type 0–100 (Enter or leaving the box sets it, Esc keeps the old value).
/// </summary>
public partial class TitleBarNoiseView : UserControl
{
    private const string OpenClass = "open";

    private bool _typing;

    public TitleBarNoiseView()
    {
        InitializeComponent();
        VolumeSlider.AddHandler(KeyDownEvent, OnSliderKeyDown, RoutingStrategies.Tunnel);
        VolumeChip.Click += (_, _) => BeginTyping(firstDigit: null);
        VolumeBox.AddHandler(KeyDownEvent, OnBoxKeyDown, RoutingStrategies.Tunnel);
        VolumeBox.AddHandler(TextInputEvent, OnBoxTextInput, RoutingStrategies.Tunnel);
        VolumeBox.LostFocus += (_, _) => CommitTyping();
        if (VolumeFlyout is { } popover)
        {
            popover.Opened += (_, _) =>
            {
                VolumeButton.Classes.Set(OpenClass, true);
                Dispatcher.UIThread.Post(() => VolumeSlider.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
            };
            popover.Closed += (_, _) =>
            {
                VolumeButton.Classes.Set(OpenClass, false);
                CommitTyping();
            };
        }
    }

    /// <summary>The volume popover, for the title bar's one-pop-up-at-a-time rule.</summary>
    public FlyoutBase? VolumeFlyout => VolumeButton.Flyout;

    private NoisePlayerViewModel? ViewModel => DataContext as NoisePlayerViewModel;

    private void OnSliderKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (NoiseVolumeKeys.Apply((int)Math.Round(viewModel.Volume), e.Key, e.KeyModifiers) is int volume)
        {
            viewModel.Volume = volume;
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.None && NoiseVolumeKeys.DigitOf(e.Key) is int digit)
        {
            BeginTyping(digit);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            VolumeFlyout?.Hide();
            e.Handled = true;
        }
    }

    private void BeginTyping(int? firstDigit)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        _typing = true;
        VolumeBox.Text = firstDigit?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                         ?? ((int)Math.Round(viewModel.Volume)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        VolumeChip.IsVisible = false;
        VolumeBox.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            VolumeBox.Focus(NavigationMethod.Tab);
            // A digit replaces the value and keeps typing after it; a click on the chip selects the value to type over.
            if (firstDigit is null)
            {
                VolumeBox.SelectAll();
            }
            else
            {
                VolumeBox.CaretIndex = VolumeBox.Text?.Length ?? 0;
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTyping();
            VolumeSlider.Focus(NavigationMethod.Tab);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Esc leaves the typing only; the popover stays open on the old value.
            EndTyping();
            VolumeSlider.Focus(NavigationMethod.Tab);
            e.Handled = true;
        }
    }

    // Digits only (the box holds at most three).
    private static void OnBoxTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Text is { Length: > 0 } text && !text.All(char.IsAsciiDigit))
        {
            e.Handled = true;
        }
    }

    private void CommitTyping()
    {
        if (!_typing)
        {
            return;
        }

        if (NoiseVolumeKeys.ParseTyped(VolumeBox.Text) is int volume && ViewModel is { } viewModel)
        {
            viewModel.Volume = volume;
        }

        EndTyping();
    }

    private void EndTyping()
    {
        _typing = false;
        VolumeBox.IsVisible = false;
        VolumeChip.IsVisible = true;
    }
}
