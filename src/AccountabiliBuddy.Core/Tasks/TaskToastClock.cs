namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// A toast's life on a monotonic clock (any steadily rising <see cref="TimeSpan"/>, e.g. a stopwatch): it runs from
/// <see cref="Start"/>, stands still while the pointer rests on the toast (<see cref="Pause"/> / <see cref="Resume"/>), and is over once
/// it has run for its whole <see cref="Life"/>. <see cref="Left"/> is the timer bar's share (1 full, 0 gone). Pure and immutable.
/// </summary>
public readonly record struct TaskToastClock(TimeSpan Life, TimeSpan Used, TimeSpan? RunningSince)
{
    public static TaskToastClock Start(TimeSpan life, TimeSpan now)
    {
        return new TaskToastClock(life, TimeSpan.Zero, now);
    }

    public bool IsPaused => RunningSince is null;

    public TaskToastClock Pause(TimeSpan now)
    {
        return RunningSince is TimeSpan since ? this with { Used = Used + Positive(now - since), RunningSince = null } : this;
    }

    public TaskToastClock Resume(TimeSpan now)
    {
        return RunningSince is null ? this with { RunningSince = now } : this;
    }

    /// <summary>How long it has run so far (paused stretches left out).</summary>
    public TimeSpan Elapsed(TimeSpan now)
    {
        return Used + (RunningSince is TimeSpan since ? Positive(now - since) : TimeSpan.Zero);
    }

    /// <summary>The share of its life still to run, from 1 down to 0.</summary>
    public double Left(TimeSpan now)
    {
        if (Life <= TimeSpan.Zero)
        {
            return 0;
        }

        return Math.Clamp(1 - Elapsed(now) / Life, 0, 1);
    }

    public bool IsOver(TimeSpan now)
    {
        return Elapsed(now) >= Life;
    }

    private static TimeSpan Positive(TimeSpan span) => span < TimeSpan.Zero ? TimeSpan.Zero : span;
}
