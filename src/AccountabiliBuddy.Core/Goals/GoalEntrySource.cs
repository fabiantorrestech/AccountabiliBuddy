using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>Where a goal entry came from (brief §5.1). Pixel Watch data arrives as <see cref="HealthConnect"/>.</summary>
[JsonConverter(typeof(GoalEnumConverter<GoalEntrySource>))]
public enum GoalEntrySource
{
    /// <summary>Typed, ticked or added by hand: the user's own decision, which wins over any reading.</summary>
    Hand,

    HealthConnect,
    ScreenTime,
    Beeminder,

    /// <summary>
    /// A PC's derived calendar minutes for a goal and day (ADR 79, owner 2026-11-14): what its calendar windows add beyond every focus
    /// window it knows. Receivers take the highest across PCs; never in focus totals, Stats focus or a guard (ADR 58).
    /// </summary>
    Calendar,

    /// <summary>
    /// A PC's extra #tag focus minutes for a goal and day (ADR 79, owner 2026-11-14): what its local-only #tag attribution adds for its
    /// OWN sessions beyond the label attribution every device can make from the synced sessions (<see cref="GoalDerivedMinutes.FocusLocalExtra"/>).
    /// Receivers add the other devices' extras to the focus they count themselves. An older app keeps it as a newer item (ADR 79).
    /// </summary>
    FocusLocal,
}
