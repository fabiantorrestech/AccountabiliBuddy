namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// The calendar rules in force plus a change waiting to take over (ADR 58). Changing a rule is a looser change: the new set
/// waits for the looser-change delay (by the trusted clock) and never takes over during a session the user started.
/// </summary>
/// <param name="Active">The rules guards follow now.</param>
/// <param name="Pending">The set that replaces <paramref name="Active"/> at <paramref name="PendingTakesOverAt"/>; null = none waiting.</param>
public sealed record CalendarRules(CalendarRuleSet Active, CalendarRuleSet? Pending = null, DateTimeOffset? PendingTakesOverAt = null)
{
    public static CalendarRules Empty { get; } = new(CalendarRuleSet.Empty);

    /// <summary>True when a waiting set's time has come.</summary>
    public bool PendingIsDue(DateTimeOffset now)
    {
        return Pending is not null && PendingTakesOverAt is DateTimeOffset at && at <= now;
    }

    /// <summary>The waiting set made the active one (this when none waits).</summary>
    public CalendarRules Promoted()
    {
        return Pending is null ? this : new CalendarRules(Pending);
    }

    /// <summary>A new set to take over at <paramref name="takesOverAt"/>, replacing any set already waiting (the wait starts over).</summary>
    public CalendarRules WithPending(CalendarRuleSet set, DateTimeOffset takesOverAt)
    {
        return this with { Pending = set, PendingTakesOverAt = takesOverAt };
    }
}
