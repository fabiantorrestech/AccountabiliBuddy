using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// What this PC keeps for the shared goal history (ADR 79, GS3b), in one device-local document ("goals.history.sync", sealed with this
/// PC's key like the goal lists): when each device's regular file last merged here, the "merged year files" markers (the kept copy of a
/// received file has none: privacy), this PC's own marker, the kept set of pruned goal ids (Q649) and, per transport, the files read and
/// written. Reset when the group's key changes (every device writes its months again under the new key).
/// </summary>
public sealed class GoalHistoryState
{
    /// <summary>The key (<see cref="SyncKey.Fingerprint"/>) this state belongs to; a different key resets it.</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>A regular file's goals part has merged here: until then this PC is new to the group and reads every history file.</summary>
    public bool GoalsPartMerged { get; set; }

    /// <summary>When each device's regular sync file (with goal parts) last merged here.</summary>
    public Dictionary<Guid, DateTimeOffset> LastRegularMerge { get; set; } = [];

    /// <summary>Each device's last complete merge here: its regular file, or a full read of its history files (<c>GoalHistoryRules.ToRead</c>).</summary>
    public Dictionary<Guid, DateTimeOffset> LastMergeFrom { get; set; } = [];

    /// <summary>Each device's latest "merged year files" marker from its regular file.</summary>
    public Dictionary<Guid, List<SyncGoalYearMerged>> DeviceMarkers { get; set; } = [];

    /// <summary>This PC's own marker: the year files it merged, newest version of each.</summary>
    public List<SyncGoalYearMerged> OwnMarker { get; set; } = [];

    /// <summary>The goal ids held as tombstones when last looked at: one that is gone later was pruned (<c>GoalHistoryRules.PrunedGoalIds</c>).</summary>
    public HashSet<Guid> TombstonedGoals { get; set; } = [];

    /// <summary>The ids of goals whose tombstone this PC pruned, kept for good (about 16 bytes a goal): their rows in history files never keep a file unread.</summary>
    public HashSet<Guid> PrunedGoals { get; set; } = [];

    /// <summary>
    /// When the first merge ran that dropped a row of a goal this PC doesn't hold (null: none open). The late joiner's rule
    /// (<c>GoalHistoryRules.LatePrunable</c>) waits until every active device's regular file has merged after it.
    /// </summary>
    public DateTimeOffset? FirstDropAt { get; set; }

    /// <summary>Names put under an earlier key, by transport: deleted at the next pass of a store that deletes (Drive), kept until they are gone.</summary>
    public Dictionary<string, List<string>> StaleNames { get; set; } = [];

    /// <summary>By transport ("drive", "relay").</summary>
    public Dictionary<string, GoalHistoryTransportState> Transports { get; set; } = [];

    public GoalHistoryTransportState Transport(string name)
    {
        if (!Transports.TryGetValue(name, out GoalHistoryTransportState? state))
        {
            state = new GoalHistoryTransportState();
            Transports[name] = state;
        }

        return state;
    }
}
