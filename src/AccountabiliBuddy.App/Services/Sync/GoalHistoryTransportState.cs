using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// What this PC keeps of the goal history files on one transport (Drive or the relay; ADR 79 shared history, GS3b). Device-local, sealed
/// at rest with the rest of <see cref="GoalHistoryState"/>, never in a backup or a sync file.
/// </summary>
public sealed class GoalHistoryTransportState
{
    /// <summary>The files read, by key text (<see cref="GoalHistoryNames.KeyText"/>), at the version listed when they were read.</summary>
    public Dictionary<string, DateTimeOffset> Read { get; set; } = [];

    /// <summary>The content tag of each read file, where the store gives one: an unchanged tag is not downloaded again.</summary>
    public Dictionary<string, string> ReadTag { get; set; } = [];

    /// <summary>This PC's own month files as last written, by <see cref="GoalHistoryNames.MonthText"/>: what "the content changed" compares and what is put again.</summary>
    public Dictionary<string, GoalHistoryMonth> Written { get; set; } = [];

    /// <summary>When each own file (by name) was last put: the relay's 25-day re-upload.</summary>
    public Dictionary<string, DateTimeOffset> Uploaded { get; set; } = [];

    /// <summary>The own month files that rolled up and are no longer kept (deleted on Drive, not put again on the relay), by month text.</summary>
    public HashSet<string> Removed { get; set; } = [];

    /// <summary>The year files seen (read, or rolled here), without their rows: the pieces and the version, by key text. <c>MayRemoveMonth</c> asks them.</summary>
    public Dictionary<string, GoalHistoryYear> Years { get; set; } = [];

    /// <summary>The names of the year files this PC rolled (put again every 25 days on the relay).</summary>
    public HashSet<string> OwnYearNames { get; set; } = [];

    /// <summary>Every name this PC ever put here: they are deleted when it leaves the group.</summary>
    public HashSet<string> OwnNames { get; set; } = [];

    /// <summary>When each year was last checked for a roll-up (at most daily).</summary>
    public Dictionary<int, DateTimeOffset> RollChecked { get; set; } = [];
}
