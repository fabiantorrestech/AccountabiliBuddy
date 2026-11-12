using System.Diagnostics;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The title bar's noise control. Its volume popover opens centred under the volume button with the keyboard on the slider:
/// arrows ±5, Shift+arrows ±1, Page Up/Down ±10, Home/End, a click on the track jumps there; a digit (or a click on the % chip)
/// switches the chip to a box to type 0–100 (Enter or leaving the box sets it, Esc keeps the old value). When the player is locked to
/// headphones and none are connected, the name box shows "Locked to headphones · connect headphones" once (scrolling through it, or a
/// fade with Animations Off) and then the sound's name again, without changing its width (owner 2026-11-14).
/// </summary>
public partial class TitleBarNoiseView : UserControl
{
    private const string OpenClass = "open";

    // A name box narrower than this can't show any of the notice; it is skipped (the volume popover's lock tooltip still says it).
    private const double NarrowestNoticeBox = 24;

    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _noticeClock = new();
    private NoticeMarqueePlan? _notice;
    private NoisePlayerViewModel? _viewModel;
    private bool _typing;

    public TitleBarNoiseView()
    {
        InitializeComponent();
        _noticeTimer.Tick += (_, _) => StepNotice();
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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.HeadphonesNeeded -= OnHeadphonesNeeded;
        }

        _viewModel = DataContext as NoisePlayerViewModel;
        if (_viewModel is not null)
        {
            _viewModel.HeadphonesNeeded += OnHeadphonesNeeded;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        EndNotice();
        base.OnDetachedFromVisualTree(e);
    }

    // Each time the lock stops or refuses play: the notice runs once. While one is showing, a repeat lets it finish rather than
    // jumping back to the start.
    private void OnHeadphonesNeeded(object? sender, EventArgs e)
    {
        if (_notice is not null || NameBox.Bounds.Width < NarrowestNoticeBox)
        {
            return;
        }

        bool reducedMotion = Services.Motion.Current is { IsOn: false };
        NoticeText.Text = NoisePlayerViewModel.HeadphonesNoticeText;
        NoticeText.TextTrimming = reducedMotion ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        NoticeText.Width = reducedMotion ? NameBox.Bounds.Width : double.NaN;
        NoticeText.Measure(Size.Infinity);
        Canvas.SetTop(NoticeText, Math.Max(0, (NameBox.Bounds.Height - NoticeText.DesiredSize.Height) / 2));
        _notice = new NoticeMarqueePlan(NameBox.Bounds.Width, NoticeText.DesiredSize.Width, reducedMotion);
        // The box's tooltip holds the whole notice while it shows (the name's own tooltip is back once it is gone).
        SoundName.IsHitTestVisible = false;
        ToolTip.SetTip(NameBox, NoisePlayerViewModel.HeadphonesNoticeText);
        _noticeClock.Restart();
        StepNotice();
        _noticeTimer.Start();
    }

    private void StepNotice()
    {
        if (_notice is null)
        {
            return;
        }

        double seconds = _noticeClock.Elapsed.TotalSeconds;
        if (seconds >= _notice.Duration)
        {
            EndNotice();
            return;
        }

        double opacity = _notice.Opacity(seconds);
        NoticeText.Opacity = opacity;
        SoundName.Opacity = 1 - opacity;
        if (NoticeText.RenderTransform is TranslateTransform shift)
        {
            shift.X = _notice.Offset(seconds);
        }
    }

    private void EndNotice()
    {
        _noticeTimer.Stop();
        _noticeClock.Reset();
        _notice = null;
        NoticeText.Opacity = 0;
        NoticeText.Text = null;
        SoundName.Opacity = 1;
        SoundName.IsHitTestVisible = true;
        ToolTip.SetTip(NameBox, null);
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
