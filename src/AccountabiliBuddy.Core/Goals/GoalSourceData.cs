using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// What goals are counted from on one device (<see cref="GoalCounting"/>).
/// </summary>
/// <param name="Focus">
/// The focus sessions across the user's devices: this PC's own (<see cref="GoalFocusAttribution.FromRecords"/>) and each other
/// device's (<see cref="GoalFocusAttribution.FromSynced"/>); time two devices both had counts once per goal.
/// </param>
/// <param name="Entries">Goal entries (any goals'; each goal reads its own live ones).</param>
/// <param name="Zone">The local time zone (dates, back-fills).</param>
/// <param name="CalendarWindows">
/// Calendar time per ADR 58 category, local windows (PC only, plan §3b 7; never in focus totals, Stats or guards); null = none.
/// </param>
/// <param name="TagsAvailable">False where #tag rules aren't counted (the phone until ADR 79, plan §3b 5).</param>
public sealed record GoalSourceData(
    IReadOnlyList<GoalFocusSession> Focus,
    IReadOnlyList<GoalEntry> Entries,
    TimeZoneInfo Zone,
    IReadOnlyDictionary<Guid, IReadOnlyList<LocalWindow>>? CalendarWindows = null,
    bool TagsAvailable = true);
