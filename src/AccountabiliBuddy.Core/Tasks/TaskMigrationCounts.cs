using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// How many items the task migration (ADR 76) read and where they went, for the verification log (counts only, never text).
/// Every item with text that was read is in exactly one place: a bin, History, skipped as expired, or folded into a copy of the
/// same id from another list (<see cref="IsBalanced"/>).
/// </summary>
/// <param name="Sources">Items with text read per old list ("nextTasks", "later", "comebacks", "autoSaved").</param>
/// <param name="SourceTombstones">Tombstones and local stubs read per old list.</param>
/// <param name="Bins">Live tasks made per bin.</param>
/// <param name="History">History entries made (finished).</param>
/// <param name="Expired">Auto-saved items past their 30 days, not carried over.</param>
/// <param name="Duplicates">Items whose id another list also had (the newer copy was kept).</param>
/// <param name="Tombstones">Task tombstones written (old tombstones, local stubs and tasks moved to History).</param>
/// <param name="Labels">Labels made from categories.</param>
/// <param name="LabelsFromItems">Labels made from a category name only an item carried.</param>
/// <param name="Views">Views made (the three built-ins and the presets).</param>
/// <param name="UnmappedStatuses">Old filter statuses with no bin ("Unfinished"), left out of a view.</param>
public sealed record TaskMigrationCounts(
    IReadOnlyDictionary<string, int> Sources,
    IReadOnlyDictionary<string, int> SourceTombstones,
    IReadOnlyDictionary<TaskBin, int> Bins,
    int History,
    int Expired,
    int Duplicates,
    int Tombstones,
    int Labels,
    int LabelsFromItems,
    int Views,
    int UnmappedStatuses)
{
    /// <summary>Items with text read = live tasks + History + expired + duplicates.</summary>
    public bool IsBalanced => Sources.Values.Sum() == Bins.Values.Sum() + History + Expired + Duplicates;
}
