namespace AccountabiliBuddy.App.Services.Navigation;

/// <summary>
/// Turns the horizontal mouse-wheel events of a touchpad's two-finger swipe into "back" / "forward" (owner 2026-11-14), the way
/// Chrome does: the horizontal motion is added up while it dominates the vertical, shown as progress toward a threshold and, once
/// past it, navigates once. A pause (<see cref="IdleReset"/>) ends the swipe; what is left of the same swipe afterwards (the
/// touchpad's inertia) does nothing. Pure and UI-free; the window feeds it <c>PointerWheelChanged</c> and polls <see cref="Tick"/>.
/// </summary>
public sealed class SwipeTracker
{
    /// <summary>One wheel notch in Avalonia's <c>Delta</c> units (a precision touchpad sends fractions of it).</summary>
    public const double NotchDelta = 1.0;

    /// <summary>How much horizontal motion makes a swipe navigate, in notches. Tuned by hand on the touchpad (reasoning only until then).</summary>
    public const double ThresholdNotches = 3.0;

    /// <summary>A gap this long between two inputs ends the swipe.</summary>
    public static readonly TimeSpan IdleReset = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The sign assumption: Avalonia's positive <c>Delta.X</c> is a scroll toward the left (like a positive <c>Delta.Y</c> is up), which
    /// is a swipe to the right with the touchpad's "natural" direction. Flip this constant if the hand test shows the touchpad sends the
    /// other sign (the Settings choice <see cref="SwipeStyle"/> then still means what it says).
    /// </summary>
    public const bool PositiveDeltaXIsSwipeRight = true;

    private double _sum;
    private bool _spent;
    private TimeSpan _last;
    private bool _hasInput;

    /// <summary>Settings › Touchpad swipe: Default = swipe right goes Back, Natural = swipe right goes Forward. Applies at once.</summary>
    public SwipeStyle Style { get; set; } = SwipeStyle.Default;

    /// <summary>The way a horizontal delta goes in the given style.</summary>
    public static HistoryDirection DirectionOf(double deltaX, SwipeStyle style)
    {
        bool toTheRight = (deltaX > 0) == PositiveDeltaXIsSwipeRight;
        bool back = toTheRight == (style == SwipeStyle.Default);
        return back ? HistoryDirection.Back : HistoryDirection.Forward;
    }

    /// <summary>Takes one wheel event (its deltas and its time) and says where the swipe is now.</summary>
    public SwipeState Feed(double deltaX, double deltaY, TimeSpan at)
    {
        bool expired = ExpireIfIdle(at);
        _last = at;
        _hasInput = true;

        // A mostly vertical motion is a scroll, not a swipe: it adds nothing (and a swipe that never started stays idle).
        if (Math.Abs(deltaX) <= Math.Abs(deltaY) || deltaX == 0)
        {
            return _spent ? SwipeState.Idle : Current(expired);
        }

        if (_spent)
        {
            return SwipeState.Idle;
        }

        _sum += deltaX;
        double threshold = ThresholdNotches * NotchDelta;
        if (Math.Abs(_sum) >= threshold)
        {
            HistoryDirection direction = DirectionOf(_sum, Style);
            _spent = true;
            _sum = 0;
            return new SwipeState(SwipePhase.Commit, direction, 1);
        }

        return Current(false);
    }

    /// <summary>Called on a timer while a swipe tracks: a pause since the last input cancels it.</summary>
    public SwipeState Tick(TimeSpan now)
    {
        return ExpireIfIdle(now) ? new SwipeState(SwipePhase.Cancelled, HistoryDirection.Back, 0) : Current(false);
    }

    /// <summary>Forgets the swipe (a modal opened, the window lost focus).</summary>
    public void Reset()
    {
        _sum = 0;
        _spent = false;
        _hasInput = false;
    }

    private SwipeState Current(bool expiredJustNow)
    {
        if (_sum == 0)
        {
            return expiredJustNow ? new SwipeState(SwipePhase.Cancelled, HistoryDirection.Back, 0) : SwipeState.Idle;
        }

        return new SwipeState(SwipePhase.Tracking, DirectionOf(_sum, Style), Math.Clamp(Math.Abs(_sum) / (ThresholdNotches * NotchDelta), 0, 1));
    }

    // True when a swipe with something to show (progress) ended by the pause; the state is cleared either way.
    private bool ExpireIfIdle(TimeSpan at)
    {
        if (!_hasInput || at - _last < IdleReset)
        {
            return false;
        }

        bool hadProgress = _sum != 0;
        _sum = 0;
        _spent = false;
        _hasInput = false;
        return hadProgress;
    }
}
