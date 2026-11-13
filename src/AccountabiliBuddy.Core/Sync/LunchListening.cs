using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Whether a device listens for sync files fast because a lunch is running or about to start (LU1-fix; the phone's Kotlin twin is the same rule).
/// A lunch is started on any device, so every device reads often while one runs or is due, and only then (never all night, to spare the relay's
/// request limit and the phone's battery). Listening when a lunch runs here or in any received file; or, when no lunch was taken today, from
/// <see cref="Lead"/> before a planned lunch window (the plan's flexible break or fixed lunch) until its end.
/// </summary>
public static class LunchListening
{
    /// <summary>How long before a planned lunch window the listening starts.</summary>
    public static readonly TimeSpan Lead = TimeSpan.FromHours(1);

    /// <summary>A lunch whose start is further ahead than this is a clock that runs ahead, not a lunch that runs.</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(2);

    /// <param name="now">The trusted instant.</param>
    /// <param name="localNow">The same instant on this device's wall clock (the plan's windows are local).</param>
    /// <param name="own">Today's lunch on this device, once started (it may be over); null before.</param>
    /// <param name="received">The lunch in each other device's latest received file (null entries: no lunch).</param>
    /// <param name="plannedLunch">Today's planned lunch windows (the plan's flexible break and fixed lunch).</param>
    public static bool IsListening(
        DateTimeOffset now, DateTime localNow, SyncLunch? own, IEnumerable<SyncLunch?> received, IEnumerable<LocalWindow> plannedLunch)
    {
        if (Running(own, now) || received.Any(lunch => Running(lunch, now)))
        {
            return true;
        }

        // A lunch taken today (and over) is not expected again.
        if (own is not null)
        {
            return false;
        }

        return plannedLunch.Any(window => localNow >= window.Start - Lead && localNow < window.End);
    }

    /// <summary>True while <paramref name="lunch"/> runs: started (a clock a little ahead counts) and not over.</summary>
    public static bool Running(SyncLunch? lunch, DateTimeOffset now) =>
        lunch is not null && lunch.Start - FutureTolerance <= now && now < lunch.End;

    /// <summary>
    /// A lunch from another device with its edit stamp held to <paramref name="now"/>: a device whose clock runs ahead stamps its edits in the future,
    /// and its lunch would then beat every real edit made later on the other devices. A stamp (or, with none, a start) more than
    /// <see cref="FutureTolerance"/> ahead becomes <paramref name="now"/>.
    /// </summary>
    public static SyncLunch ClampFuture(SyncLunch lunch, DateTimeOffset now) =>
        lunch.LastEdit > now + FutureTolerance ? lunch with { EditedAt = now } : lunch;
}
