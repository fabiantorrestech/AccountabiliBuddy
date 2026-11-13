using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

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
/// <param name="TagsAvailable">
/// False where #tag rules aren't counted from the sessions themselves (the phone, plan §3b 5); it counts the PCs' #tag extras
/// (<see cref="GoalEntrySource.FocusLocal"/>) instead.
/// </param>
/// <param name="Steps">
/// The phone's daily step counts as the sync file carries them (<see cref="SyncSteps"/>; the PC's fresh copy), or null. A steps goal counts
/// a day from them only when no Health Connect entry holds that day (ADR 79: the derived entry when present, else these, never both).
/// </param>
/// <param name="DeviceId">
/// This device's id (ADR 79): its own #tag and calendar extras are left out (it counts its own sessions and calendar itself); null where it
/// isn't known (then every device's extras count).
/// </param>
public sealed record GoalSourceData(
    IReadOnlyList<GoalFocusSession> Focus,
    IReadOnlyList<GoalEntry> Entries,
    TimeZoneInfo Zone,
    IReadOnlyDictionary<Guid, IReadOnlyList<LocalWindow>>? CalendarWindows = null,
    bool TagsAvailable = true,
    SyncSteps? Steps = null,
    Guid? DeviceId = null);
