using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// What the one-time task migration (ADR 76) reads: the old stores' contents as they are kept today, tombstones and local items
/// included. The App loads and opens the documents; the migration itself touches no storage.
/// </summary>
/// <param name="Categories">"session.categories".</param>
/// <param name="NextTasks">"session.nextTasks" (the next session's list).</param>
/// <param name="Later">"session.later".</param>
/// <param name="Comebacks">"capture.comebacks" in its shared form (a come-back kept here as a local item keeps its text).</param>
/// <param name="AutoSaved">"tasks.autoSaved".</param>
/// <param name="FilterPresets">"tasks.filterPresets".</param>
/// <param name="CurrentFilter">"tasks.view"; null when there is none.</param>
/// <param name="Now">The trusted time of the run.</param>
/// <param name="Zone">The local time zone, for "done today" and origin lines.</param>
/// <param name="IsNumberingDevice">This device hands out final numbers (<see cref="TaskNumbering.NumberingDevice"/>); otherwise numbers are temporary.</param>
/// <param name="FirstNumber">The first number to give (above any number already used).</param>
/// <param name="SessionActions">How many actions This session takes (the session cap, 2 to 5; 3 by default).</param>
public sealed record TaskMigrationInput(
    IReadOnlyList<TaskCategory> Categories,
    IReadOnlyList<SyncNextTask> NextTasks,
    IReadOnlyList<SyncLaterItem> Later,
    IReadOnlyList<SyncParked> Comebacks,
    IReadOnlyList<SyncAutoSavedItem> AutoSaved,
    IReadOnlyList<LegacyTaskFilter> FilterPresets,
    LegacyTaskFilter? CurrentFilter,
    DateTimeOffset Now,
    TimeZoneInfo Zone,
    bool IsNumberingDevice,
    int FirstNumber = 1,
    int SessionActions = 3);
