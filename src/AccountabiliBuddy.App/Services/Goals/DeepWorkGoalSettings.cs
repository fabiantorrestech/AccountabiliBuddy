using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// The built-in Deep work goal's own values on this PC, device-local (plan R4.4: the "goal" document travels in sync files, so adding a
/// field there would be a new synced field). Its daily target is not here: that stays the "goal" document (<see cref="GoalService"/>,
/// ADR 29). Held together in memory, stored in two plain documents (no private text): the weekly target and Track time only in
/// "goals.deepWork" (a device setting in backups, restored through the Deep work rules, <see cref="GoalsService.RestoreDeepWorkAsync"/>)
/// and the waiting changes in "goals.deepWork.pending" (state of the moment: never in a backup, ADR 71).
/// </summary>
/// <param name="WeeklyMinutes">The weekly target in minutes; null = none (the default).</param>
/// <param name="TrackTimeOnly">
/// "Track time only, no target": Goals shows Deep work without a daily or weekly target. The guards keep the "goal" document's daily
/// minutes as they are (they never read this; Q374).
/// </param>
/// <param name="PendingDaily">A lower daily target waiting for its delay (applied through <see cref="GoalService.SetAsync"/>).</param>
/// <param name="PendingWeekly">A lower weekly target (or none) waiting for its delay.</param>
/// <param name="PendingTrackTimeOnly">"Track time only" waiting for its delay (its amount is null).</param>
/// <param name="Stamp">
/// ADR 79 (GS3a): when and on which device the weekly target and "Track time only" in effect here were last changed (Deep work's
/// <see cref="GoalField.SecondTarget"/> part as this PC sends it, <see cref="GoalDeepWorkSync.Compose"/>): this PC's own change when it
/// applied, or the stamp of a change received from another device when it applied here. Never the stamp of a change still waiting, so a
/// device never sends a value it doesn't hold yet. Null before the first one (the stored record's own stamp counts then).
/// </param>
/// <param name="PendingStamp">
/// The stamp the waiting weekly target or "Track time only" takes when it lands, for one received from another device (it then holds
/// that device's stamp, so nothing echoes); null for one made here (stamped when it lands). State, with the waiting changes.
/// </param>
/// <param name="PendingAfterSession">
/// A looser weekly target or "Track time only" received during a focus session the user started (AccountabiliBuddy mode): held, its
/// looser-change wait counted from the session's end. State, with the waiting changes.
/// </param>
public sealed record DeepWorkGoalSettings(
    int? WeeklyMinutes = null,
    bool TrackTimeOnly = false,
    GoalPendingChange? PendingDaily = null,
    GoalPendingChange? PendingWeekly = null,
    GoalPendingChange? PendingTrackTimeOnly = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalFieldStamp? Stamp = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalFieldStamp? PendingStamp = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool PendingAfterSession = false)
{
    public static DeepWorkGoalSettings Default { get; } = new();

    /// <summary>The weekly target in effect (none while "Track time only" is on).</summary>
    [JsonIgnore]
    public int? EffectiveWeekly => TrackTimeOnly ? null : WeeklyMinutes;

    /// <summary>Whether any change is waiting.</summary>
    public bool HasPending => PendingDaily is not null || PendingWeekly is not null || PendingTrackTimeOnly is not null;
}
