using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// What a saved view shows and how (ADR 76): its filters, grouping, sort and density. Values are plain strings (see each
/// parameter) so a newer app's value survives in an older one, which then uses its default.
/// </summary>
/// <param name="Only">The "Show only" filters.</param>
/// <param name="Labels">Only tasks with one of these labels; null for any.</param>
/// <param name="NoLabel">Tasks without a label match too; alone (no <paramref name="Labels"/>), only they match.</param>
/// <param name="Tags">Only tasks with these tags (by <paramref name="Match"/>); null for any.</param>
/// <param name="Match">"any" or "all" for <paramref name="Tags"/>.</param>
/// <param name="Group">"bins", "label", "tag" or "none".</param>
/// <param name="Sort">"default" (manual order), "dueAsc", "dueDesc", "newest", "oldest", "untouched", "shuffle", "longest", "shortest".</param>
/// <param name="PinHighlights">Highlighted tasks first.</param>
/// <param name="Density">"normal" or "compact".</param>
/// <param name="Bins">Only these bins; null for all. Carries the old filter's status choices (Later, Come back to, Done).</param>
/// <param name="Seed">The order Shuffle gave: stored with the view (and synced) until it is reshuffled; null before the first shuffle.</param>
public sealed record TaskViewConfig(
    TaskViewFilter Only,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? Labels = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool NoLabel = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Tags = null,
    string Match = TaskViewConfig.MatchAny,
    string Group = TaskViewConfig.GroupBins,
    string Sort = TaskViewConfig.SortDefault,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool PinHighlights = false,
    string Density = TaskViewConfig.DensityNormal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<TaskBin>? Bins = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Seed = null)
{
    public const string MatchAny = "any";
    public const string MatchAll = "all";
    public const string GroupBins = "bins";
    public const string SortDefault = "default";
    public const string DensityNormal = "normal";
    public const string DensityCompact = "compact";
    public const string GroupLabel = "label";
    public const string GroupTag = "tag";
    public const string GroupNone = "none";
    public const string SortDueAscending = "dueAsc";
    public const string SortDueDescending = "dueDesc";
    public const string SortNewest = "newest";
    public const string SortOldest = "oldest";
    public const string SortUntouched = "untouched";
    public const string SortShuffle = "shuffle";
    public const string SortLongest = "longest";
    public const string SortShortest = "shortest";

    /// <summary>Everything, grouped by bins, default sort.</summary>
    public static TaskViewConfig Everything { get; } = new(TaskViewFilter.None);
}
