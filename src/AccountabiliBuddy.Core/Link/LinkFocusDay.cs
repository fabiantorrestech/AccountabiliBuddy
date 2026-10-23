namespace AccountabiliBuddy.Core.Link;

/// <summary>
/// Today's focus as the PC counts it, for paired devices and, through the phone, the watch (ADR 21): the plan's parts,
/// the sessions that count, drift, and focused and planned minutes for today and the 7 days ending today. Only
/// readable fields (ADR 15): never titles, addresses, reasons or an added session's note.
/// </summary>
/// <param name="Date">The calendar day (local midnight to midnight).</param>
public sealed record LinkFocusDay(
    DateOnly Date,
    IReadOnlyList<LinkPlanPart> Parts,
    IReadOnlyList<LinkFocusSession> Sessions,
    IReadOnlyList<LinkWindow> Drift,
    int FocusedMinutes,
    int PlannedMinutes,
    IReadOnlyList<LinkDayTotal> Week)
{
    /// <summary>The daily deep-work goal in minutes (ADR 29); null when this PC has none to send.</summary>
    public int? GoalMinutes { get; init; }
}
