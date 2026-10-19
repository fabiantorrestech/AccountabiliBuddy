using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// A clock that changing the Windows clock can't move. It follows the system clock while that stays within
/// <see cref="JumpTolerance"/> of the real elapsed time (an elapsed-time counter that keeps counting during sleep and
/// can't be set). When the system clock jumps further, the jump is ignored: sessions, waits and countdowns keep
/// running on real time until the system clock is set back.
/// Small differences (time sync, counter drift) are taken over slowly, at most <see cref="MaxDriftRate"/>, so nudging
/// the clock a minute at a time gains at most the tolerance in total. Thread-safe.
/// </summary>
public sealed class TrustedClock : TimeProvider
{
    public static readonly TimeSpan JumpTolerance = TimeSpan.FromMinutes(2);

    /// <summary>How fast the trusted time may follow small system-clock differences: 1 second per 10 minutes.</summary>
    public const double MaxDriftRate = 1.0 / 600;

    private readonly TimeProvider _system;
    private readonly Func<long> _elapsedMilliseconds;
    private readonly string? _bootId;
    private readonly object _gate = new();

    // Trusted time at a moment of the elapsed-time counter; trusted now = anchor + counter difference.
    private DateTimeOffset _anchorTrusted;
    private long _anchorElapsed;
    private bool _isIgnoringSystemClock;

    /// <param name="system">The system clock (and time zone).</param>
    /// <param name="elapsedMilliseconds">An elapsed-time counter that counts sleep and can't be set,
    /// e.g. <see cref="Environment.TickCount64"/>.</param>
    /// <param name="bootId">The current boot, used to match a saved <see cref="ClockAnchor"/>; null disables that.</param>
    public TrustedClock(TimeProvider system, Func<long> elapsedMilliseconds, string? bootId)
    {
        _system = system;
        _elapsedMilliseconds = elapsedMilliseconds;
        _bootId = bootId;
        _anchorTrusted = system.GetUtcNow();
        _anchorElapsed = elapsedMilliseconds();
    }

    /// <summary>Raised (with the system clock's offset from real time) when a jump starts or stops being ignored;
    /// <see cref="TimeSpan.Zero"/> means the clocks agree again. Raised on the thread that read the time.</summary>
    public event EventHandler<TimeSpan>? SystemClockOffsetChanged;

    /// <summary>How far the system clock is from the trusted time while a jump is ignored; zero otherwise.</summary>
    public TimeSpan SystemClockOffset
    {
        get
        {
            GetUtcNow();
            lock (_gate)
            {
                return _isIgnoringSystemClock ? _system.GetUtcNow() - _anchorTrusted : TimeSpan.Zero;
            }
        }
    }

    public override TimeZoneInfo LocalTimeZone => _system.LocalTimeZone;

    /// <summary>
    /// Carries on from an anchor saved earlier in the same boot, so a clock changed while the app was closed is
    /// still ignored. Ignored when the boot differs or is unknown. Call before anything reads the time.
    /// </summary>
    public void Restore(ClockAnchor? saved)
    {
        if (saved is null || _bootId is null || saved.BootId != _bootId)
        {
            return;
        }

        lock (_gate)
        {
            if (_elapsedMilliseconds() < saved.ElapsedMilliseconds)
            {
                return; // The counter went back, so this is not the boot the anchor came from.
            }

            _anchorTrusted = saved.TrustedUtc;
            _anchorElapsed = saved.ElapsedMilliseconds;
        }
    }

    /// <returns>An anchor to save, or null if the boot is unknown.</returns>
    public ClockAnchor? CreateAnchor()
    {
        if (_bootId is null)
        {
            return null;
        }

        GetUtcNow();
        lock (_gate)
        {
            return new ClockAnchor(_bootId, _anchorTrusted, _anchorElapsed);
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        DateTimeOffset result;
        bool changed;
        TimeSpan offset;
        lock (_gate)
        {
            long elapsed = _elapsedMilliseconds();
            DateTimeOffset system = _system.GetUtcNow();
            TimeSpan sinceAnchor = TimeSpan.FromMilliseconds(Math.Max(0, elapsed - _anchorElapsed));
            DateTimeOffset expected = _anchorTrusted + sinceAnchor;
            TimeSpan gap = system - expected;
            bool wasIgnoring = _isIgnoringSystemClock;

            if (gap.Duration() <= JumpTolerance)
            {
                // The clocks agree: follow the system clock, and let the trusted time drift towards it slowly.
                TimeSpan maxStep = sinceAnchor * MaxDriftRate;
                TimeSpan step = gap > maxStep ? maxStep : gap < -maxStep ? -maxStep : gap;
                _anchorTrusted = expected + step;
                _isIgnoringSystemClock = false;
                result = system;
                offset = TimeSpan.Zero;
            }
            else
            {
                // The system clock jumped: keep real elapsed time.
                _anchorTrusted = expected;
                _isIgnoringSystemClock = true;
                result = expected;
                offset = gap;
            }

            _anchorElapsed = elapsed;
            changed = wasIgnoring != _isIgnoringSystemClock;
        }

        if (changed)
        {
            SystemClockOffsetChanged?.Invoke(this, offset);
        }

        return result;
    }
}
