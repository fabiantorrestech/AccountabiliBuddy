using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A reminder card's Dismiss (ADR 76, spec 03 §D, spec 05 §7): while dismiss protection runs it is off and fills up smoothly from the
/// left; once the wait is over it works on a click, or, for Can't miss (<see cref="HoldTime"/> above zero), only when held that long
/// (the fill grows while held; letting go early empties it). A hold can't be skipped: Enter and Space don't click it, they hold it
/// like the mouse does. The wait itself (whether the button works) is the card's to decide from the trusted clock; this control only
/// draws it. The hold is timed by <see cref="Clock"/> (the trusted clock). It ticks only while it fills (about 30 times a second).
/// </summary>
public sealed class ReminderDismissButton : Button
{
    public static readonly StyledProperty<TimeSpan> HoldTimeProperty =
        AvaloniaProperty.Register<ReminderDismissButton, TimeSpan>(nameof(HoldTime));

    public static readonly StyledProperty<TimeSpan> WaitLeftProperty =
        AvaloniaProperty.Register<ReminderDismissButton, TimeSpan>(nameof(WaitLeft));

    public static readonly StyledProperty<TimeSpan> WaitTotalProperty =
        AvaloniaProperty.Register<ReminderDismissButton, TimeSpan>(nameof(WaitTotal));

    public static readonly StyledProperty<IBrush?> BaseBrushProperty =
        AvaloniaProperty.Register<ReminderDismissButton, IBrush?>(nameof(BaseBrush));

    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<ReminderDismissButton, IBrush?>(nameof(FillBrush));

    public static readonly StyledProperty<Func<DateTimeOffset>?> ClockProperty =
        AvaloniaProperty.Register<ReminderDismissButton, Func<DateTimeOffset>?>(nameof(Clock));

    // A wait the fill already shows is not restarted for a tick's small difference (the card reads the clock once a second).
    private static readonly TimeSpan RestartAbove = TimeSpan.FromMilliseconds(400);

    private readonly DispatcherTimer _frame = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private FillPhase _phase;
    private DateTimeOffset _phaseStart;
    private TimeSpan _phaseLength;
    private double _fillFrom;
    private double _fill;

    static ReminderDismissButton()
    {
        AffectsRender<ReminderDismissButton>(BaseBrushProperty, FillBrushProperty, CornerRadiusProperty);
    }

    public ReminderDismissButton()
    {
        _frame.Tick += (_, _) => OnFrame();
        LostFocus += (_, _) => CancelHold();
    }

    private enum FillPhase
    {
        None,
        Wait,
        Hold,
    }

    /// <summary>How long it must be held to dismiss once the wait is over; zero for a plain click.</summary>
    public TimeSpan HoldTime
    {
        get => GetValue(HoldTimeProperty);
        set => SetValue(HoldTimeProperty, value);
    }

    /// <summary>How much of dismiss protection is left now (zero: none).</summary>
    public TimeSpan WaitLeft
    {
        get => GetValue(WaitLeftProperty);
        set => SetValue(WaitLeftProperty, value);
    }

    /// <summary>The whole dismiss protection, for where the fill starts.</summary>
    public TimeSpan WaitTotal
    {
        get => GetValue(WaitTotalProperty);
        set => SetValue(WaitTotalProperty, value);
    }

    /// <summary>The button's own surface, under the fill.</summary>
    public IBrush? BaseBrush
    {
        get => GetValue(BaseBrushProperty);
        set => SetValue(BaseBrushProperty, value);
    }

    public IBrush? FillBrush
    {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    /// <summary>The trusted clock the hold is timed by.</summary>
    public Func<DateTimeOffset>? Clock
    {
        get => GetValue(ClockProperty);
        set => SetValue(ClockProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Button);

    private bool Holds => HoldTime > TimeSpan.Zero;

    public override void Render(DrawingContext context)
    {
        var box = new Rect(Bounds.Size);
        var shape = new RoundedRect(box, CornerRadius);
        if (BaseBrush is { } baseBrush)
        {
            context.DrawRectangle(baseBrush, null, shape);
        }

        if (_fill > 0 && FillBrush is { } fill)
        {
            using (context.PushClip(shape))
            {
                context.DrawRectangle(fill, null, new Rect(0, 0, box.Width * Math.Min(1, _fill), box.Height));
            }
        }

        base.Render(context);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WaitLeftProperty || change.Property == WaitTotalProperty)
        {
            FollowWait();
        }
        else if (change.Property == HoldTimeProperty && !Holds && _phase == FillPhase.Hold)
        {
            StopFill();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _frame.Stop();
    }

    // A click never dismisses a Can't miss: only a full hold does.
    protected override void OnClick()
    {
        if (!Holds)
        {
            base.OnClick();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!Holds)
        {
            base.OnPointerPressed(e);
            return;
        }

        if (IsEffectivelyEnabled && _phase != FillPhase.Wait && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            e.Pointer.Capture(this);
            StartHold();
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!Holds)
        {
            base.OnPointerReleased(e);
            return;
        }

        CancelHold();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelHold();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Holds && e.Key is Key.Enter or Key.Space)
        {
            // Held like the mouse: the key's repeats keep the same hold going.
            if (IsEffectivelyEnabled && _phase == FillPhase.None)
            {
                StartHold();
            }

            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (Holds && e.Key is Key.Enter or Key.Space)
        {
            CancelHold();
            e.Handled = true;
            return;
        }

        base.OnKeyUp(e);
    }

    private DateTimeOffset Now => Clock?.Invoke() ?? DateTimeOffset.MinValue;

    // The wait fill runs from where the wait stands now to full, then empties: the button works.
    private void FollowWait()
    {
        TimeSpan left = WaitLeft;
        if (left <= TimeSpan.Zero || WaitTotal <= TimeSpan.Zero)
        {
            if (_phase == FillPhase.Wait)
            {
                StopFill();
            }

            return;
        }

        if (_phase == FillPhase.Wait)
        {
            TimeSpan shownLeft = _phaseLength - (Now - _phaseStart);
            if ((shownLeft - left).Duration() < RestartAbove)
            {
                return;
            }
        }

        _phase = FillPhase.Wait;
        _fillFrom = Math.Clamp(1 - (left.TotalMilliseconds / WaitTotal.TotalMilliseconds), 0, 1);
        _phaseLength = left;
        _phaseStart = Now;
        _fill = _fillFrom;
        _frame.Start();
        InvalidateVisual();
    }

    private void StartHold()
    {
        if (Clock is null)
        {
            return;
        }

        _phase = FillPhase.Hold;
        _fillFrom = 0;
        _phaseLength = HoldTime;
        _phaseStart = Now;
        _fill = 0;
        _frame.Start();
        InvalidateVisual();
    }

    private void CancelHold()
    {
        if (_phase == FillPhase.Hold)
        {
            StopFill();
        }
    }

    private void StopFill()
    {
        _phase = FillPhase.None;
        _fill = 0;
        _frame.Stop();
        InvalidateVisual();
    }

    private void OnFrame()
    {
        if (_phase == FillPhase.None || _phaseLength <= TimeSpan.Zero)
        {
            StopFill();
            return;
        }

        double progress = Math.Clamp((Now - _phaseStart).TotalMilliseconds / _phaseLength.TotalMilliseconds, 0, 1);
        _fill = _fillFrom + ((1 - _fillFrom) * progress);
        InvalidateVisual();
        if (progress < 1)
        {
            return;
        }

        FillPhase finished = _phase;
        StopFill();
        if (finished == FillPhase.Hold && IsEffectivelyEnabled && Command is ICommand command && command.CanExecute(CommandParameter))
        {
            command.Execute(CommandParameter);
        }
    }
}
