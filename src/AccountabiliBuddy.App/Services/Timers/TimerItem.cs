namespace AccountabiliBuddy.App.Services.Timers;

/// <summary>
/// One timer on this PC (V1: local only). A running timer keeps its end time (trusted clock), so it survives a restart;
/// a paused one keeps what was left.
/// </summary>
/// <param name="Length">What it was set for; Reset goes back to it.</param>
/// <param name="EndsAt">While running.</param>
/// <param name="Remaining">While paused.</param>
/// <param name="Dismissed">Done and dismissed: it leaves the on-screen window (it stays on Today until deleted).</param>
public sealed record TimerItem(
    Guid Id,
    string Name,
    TimeSpan Length,
    TimerState State,
    DateTimeOffset? EndsAt,
    TimeSpan? Remaining,
    bool Dismissed)
{
    public const string DefaultName = "Timer";

    /// <summary>Time left at <paramref name="now"/>: none once done.</summary>
    public TimeSpan LeftAt(DateTimeOffset now)
    {
        return State switch
        {
            TimerState.Running when EndsAt is DateTimeOffset end => end > now ? end - now : TimeSpan.Zero,
            TimerState.Paused => Remaining ?? Length,
            _ => TimeSpan.Zero,
        };
    }
}
