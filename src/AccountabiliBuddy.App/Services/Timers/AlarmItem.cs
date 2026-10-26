namespace AccountabiliBuddy.App.Services.Timers;

/// <summary>
/// One alarm on this PC (V1: local only, and it doesn't wake a sleeping PC). It rings at <see cref="Time"/> on the
/// chosen days, or once when no days are chosen (then it turns itself off). Times are this PC's wall clock.
/// </summary>
/// <param name="Days">None = once.</param>
/// <param name="ArmedFrom">It rings at the first chosen time after this (local); moved on each time it rings.</param>
/// <param name="SnoozedUntil">Rings again then (local), whether or not it is still on.</param>
public sealed record AlarmItem(
    Guid Id,
    string Name,
    TimeOnly Time,
    IReadOnlyList<DayOfWeek> Days,
    bool IsOn,
    DateTime ArmedFrom,
    DateTime? SnoozedUntil)
{
    public const string DefaultName = "Alarm";

    public bool IsOnce => Days.Count == 0;

    /// <summary>When it rings next (local), or null when it's off and not snoozed.</summary>
    public DateTime? NextRing()
    {
        if (SnoozedUntil is DateTime snoozed)
        {
            return snoozed;
        }

        return IsOn ? NextAfter(ArmedFrom) : null;
    }

    /// <summary>The first chosen time strictly after <paramref name="after"/>.</summary>
    public DateTime NextAfter(DateTime after)
    {
        for (int day = 0; day <= 7; day++)
        {
            DateTime candidate = after.Date.AddDays(day) + Time.ToTimeSpan();
            if (candidate > after && (IsOnce || Days.Contains(candidate.DayOfWeek)))
            {
                return candidate;
            }
        }

        // Not reached: some day in the next 8 is always a chosen one.
        return after.Date.AddDays(8) + Time.ToTimeSpan();
    }
}
