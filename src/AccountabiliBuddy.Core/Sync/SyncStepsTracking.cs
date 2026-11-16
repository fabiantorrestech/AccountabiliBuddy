namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The synced "Track daily steps" switch (ADR 91): on or off, when it was set and by whom, nothing else. JSON keys in this order: <c>on</c>,
/// <c>at</c>, <c>by</c>. Newest wins on every Android device (<see cref="Merge"/>); the PC neither writes nor carries it. Each device still
/// asks for its own Health Connect permission. Twin: Kotlin <c>SyncStepsTracking</c>.
/// </summary>
/// <param name="On">Whether steps are tracked.</param>
/// <param name="At">When it was chosen, from the trusted clock; a received time more than 2 minutes ahead of the receiver's now is ignored until time catches up. <see cref="UpgradeStamp"/> for a switch that was already on before the part existed.</param>
/// <param name="By">The device that chose it.</param>
public sealed record SyncStepsTracking(bool On, DateTimeOffset At, Guid By)
{
    /// <summary>The time an upgraded device's already-on switch is stamped with, as <c>GuidanceState.UpgradeStamp</c> does: any real choice beats it.</summary>
    public static DateTimeOffset UpgradeStamp { get; } = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The part an upgraded device sends for its local switch: an <b>on</b> switch with no stamp is stamped <see cref="UpgradeStamp"/>, so a phone
    /// that tracked steps spreads "on" without a tie turning anyone off; an <b>off</b> or fresh switch is never sent (null), so it can't flip a
    /// phone that tracks.
    /// </summary>
    public static SyncStepsTracking? ForUpgrade(bool localOn, Guid self)
    {
        return localOn ? new SyncStepsTracking(true, UpgradeStamp, self) : null;
    }

    /// <summary>
    /// The newer of the two (either may be null = none). The later time wins; a tie goes to the higher <c>by</c> (ordinal D text), then to
    /// off over on (the safer side for health data; only a damaged file ties on both). An <paramref name="incoming"/> value whose
    /// time is more than <see cref="SharedListMerge.FutureLimit"/> past <paramref name="now"/> is ignored until the receiver's time catches up
    /// (the rule <c>GuidanceRules.Receive</c> follows: only the received value is filtered), so nothing is stored capped and a stored time never
    /// moves. A <paramref name="held"/> value is never dropped by time, so a clock that steps back loses nothing; a null result therefore means
    /// "nothing held and nothing usable arrived", never "off". Idempotent.
    /// </summary>
    public static SyncStepsTracking? Merge(SyncStepsTracking? held, SyncStepsTracking? incoming, DateTimeOffset now)
    {
        DateTimeOffset limit = now + SharedListMerge.FutureLimit;
        SyncStepsTracking? first = held;
        SyncStepsTracking? second = Clamped(incoming, limit);
        if (first is null || second is null)
        {
            return first ?? second;
        }

        return Newer(second, first) ? second : first;
    }

    private static SyncStepsTracking? Clamped(SyncStepsTracking? value, DateTimeOffset limit)
    {
        return value is not null && value.At <= limit ? value : null;
    }

    private static bool Newer(SyncStepsTracking candidate, SyncStepsTracking held)
    {
        if (candidate.At != held.At)
        {
            return candidate.At > held.At;
        }

        int byOrder = string.CompareOrdinal(candidate.By.ToString("D"), held.By.ToString("D"));
        return byOrder != 0 ? byOrder > 0 : !candidate.On && held.On;
    }
}
