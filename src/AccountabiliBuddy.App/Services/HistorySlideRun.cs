using AccountabiliBuddy.App.Services.Navigation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// One running back / forward page slide: the old page slides out, <c>apply</c> puts the place back, the page now showing slides in.
/// Only render transforms and opacity are touched, so layout never moves. <see cref="FinishNow"/> ends it at once (the place is
/// applied if it wasn't yet, every page is back to opaque and in place), which a following Back / Forward press uses so presses
/// never pile up. Use from the UI thread; made by <see cref="Motion.HistorySlide"/>.
/// </summary>
public sealed class HistorySlideRun
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    private readonly Control _outgoing;
    private readonly Func<Control> _apply;
    private readonly HistoryDirection _direction;
    private readonly TimeSpan _outDuration;
    private readonly TimeSpan _inDuration;
    private Control? _incoming;
    private DispatcherTimer? _timer;
    private bool _applied;

    internal HistorySlideRun(Control outgoing, Func<Control> apply, HistoryDirection direction, TimeSpan outDuration, TimeSpan inDuration)
    {
        _outgoing = outgoing;
        _apply = apply;
        _direction = direction;
        _outDuration = outDuration;
        _inDuration = inDuration;
    }

    /// <summary>True once the slide is over (finished or cut short).</summary>
    public bool IsDone { get; private set; }

    /// <summary>True once the place has been put back (the old page is out).</summary>
    public bool IsApplied => _applied;

    internal void Start()
    {
        Phase(_outgoing, _outDuration, t => HistorySlidePlan.Out(_direction, t), () =>
        {
            // The old page stays gone until the new one is up; the place goes back while it is invisible.
            _outgoing.Opacity = 0;
            ApplyOnce();
            if (!ReferenceEquals(_incoming, _outgoing))
            {
                Rest(_outgoing);
            }

            Control incoming = _incoming!;
            Place(incoming, 0, HistorySlidePlan.InStart(_direction));
            Phase(incoming, _inDuration, t => HistorySlidePlan.In(_direction, t), Complete);
        });
    }

    /// <summary>Ends the slide at once: the place is applied if it wasn't, and every page is opaque and in place.</summary>
    public void FinishNow()
    {
        if (IsDone)
        {
            return;
        }

        _timer?.Stop();
        _timer = null;
        ApplyOnce();
        RestAll();
        IsDone = true;
    }

    private void Complete()
    {
        if (IsDone)
        {
            return;
        }

        RestAll();
        IsDone = true;
    }

    private void RestAll()
    {
        Rest(_outgoing);
        if (_incoming is not null)
        {
            Rest(_incoming);
        }
    }

    private void ApplyOnce()
    {
        if (!_applied)
        {
            _applied = true;
            _incoming = _apply();
        }
    }

    // One eased phase on one page: a frame timer reads the plan; the end values are set exactly when it is over.
    private void Phase(Control page, TimeSpan duration, Func<double, (double Opacity, double X)> plan, Action done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = Frame };
        _timer = timer;
        timer.Tick += (_, _) =>
        {
            if (!ReferenceEquals(_timer, timer))
            {
                timer.Stop();
                return;
            }

            double t = duration <= TimeSpan.Zero ? 1 : Math.Clamp(clock.Elapsed / duration, 0, 1);
            (double opacity, double x) = plan(t);
            Place(page, opacity, x);
            if (t >= 1)
            {
                timer.Stop();
                _timer = null;
                done();
            }
        };
        timer.Start();
    }

    private static void Place(Control page, double opacity, double x)
    {
        page.Opacity = opacity;
        TranslateTransform shift = page.RenderTransform as TranslateTransform ?? new TranslateTransform();
        page.RenderTransform = shift;
        shift.X = x;
    }

    private static void Rest(Control page)
    {
        page.Opacity = 1;
        if (page.RenderTransform is TranslateTransform shift)
        {
            shift.X = 0;
        }
    }
}
