using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// What the Search filters panel holds (Tasks redesign item 7; SPEC "Search and filters"): Look in (task names, steps, notes, tags; all on),
/// Bins (the Shelf only; all on), the ✓ / ✕ label and tag picks, and When. This PC's search state only: never stored in a view, never synced
/// (7.35). Immutable: every change gives a new value. Tag keys are folded (<see cref="TaskSearch.Fold"/>) so "#Docs" and "#docs" are one chip.
/// </summary>
public sealed record TaskSearchFilters
{
    private static readonly IReadOnlyDictionary<Guid, TaskSearchPick> NoLabelPicks = new Dictionary<Guid, TaskSearchPick>();
    private static readonly IReadOnlyDictionary<string, TaskSearchPick> NoTagPicks = new Dictionary<string, TaskSearchPick>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<TaskSearchBin> NoBins = new HashSet<TaskSearchBin>();

    // Declared after the empty collections above: static fields start in order, and these defaults use them.
    /// <summary>Nothing narrowed: every Look in and Bins chip on, no label or tag pick, Any time (what Reset gives back, 7.33).</summary>
    public static readonly TaskSearchFilters Default = new();

    /// <summary>Look in: plain words are looked for in task names.</summary>
    public bool InNames { get; init; } = true;

    /// <summary>Look in: plain words are looked for in steps.</summary>
    public bool InSteps { get; init; } = true;

    /// <summary>Look in: plain words are looked for in notes.</summary>
    public bool InNotes { get; init; } = true;

    /// <summary>Look in: plain words are looked for in tags.</summary>
    public bool InTags { get; init; } = true;

    /// <summary>The Bins chips turned off (the Shelf only; the Archive ignores them).</summary>
    public IReadOnlySet<TaskSearchBin> BinsOff { get; init; } = NoBins;

    /// <summary>The label picks by label id (no entry = any).</summary>
    public IReadOnlyDictionary<Guid, TaskSearchPick> Labels { get; init; } = NoLabelPicks;

    /// <summary>The "No label" chip's pick.</summary>
    public TaskSearchPick NoLabel { get; init; } = TaskSearchPick.Any;

    /// <summary>The tag picks by folded tag name, without "#" (no entry = any).</summary>
    public IReadOnlyDictionary<string, TaskSearchPick> Tags { get; init; } = NoTagPicks;

    public TaskSearchWhen When { get; init; } = TaskSearchWhen.AnyTime;

    /// <summary>Custom's first day (included); null = no start.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Custom's last day (included); null = no end.</summary>
    public DateOnly? To { get; init; }

    /// <summary>Nothing is narrowed (the filter button's "on" look shows otherwise).</summary>
    public bool IsDefault => InNames && InSteps && InNotes && InTags && BinsOff.Count == 0 && Labels.Count == 0
        && NoLabel == TaskSearchPick.Any && Tags.Count == 0 && When == TaskSearchWhen.AnyTime;

    /// <summary>The Bins chip is on.</summary>
    public bool HasBin(TaskSearchBin bin) => !BinsOff.Contains(bin);

    /// <summary>A label chip's pick (any when it has none).</summary>
    public TaskSearchPick LabelPick(Guid labelId) => Labels.GetValueOrDefault(labelId, TaskSearchPick.Any);

    /// <summary>A tag chip's pick (any when it has none); <paramref name="tag"/> with or without "#".</summary>
    public TaskSearchPick TagPick(string tag) => Tags.GetValueOrDefault(TagKey(tag), TaskSearchPick.Any);

    /// <summary>The next state of a chip's cycle: any → ✓ include → ✕ leave out → any (7.21).</summary>
    public static TaskSearchPick Cycle(TaskSearchPick pick)
    {
        return pick switch
        {
            TaskSearchPick.Any => TaskSearchPick.Include,
            TaskSearchPick.Include => TaskSearchPick.LeaveOut,
            _ => TaskSearchPick.Any,
        };
    }

    /// <summary>A tag as the picks key it: folded, without its "#".</summary>
    public static string TagKey(string tag) => TaskSearch.Fold(tag.Trim().TrimStart('#'));

    public TaskSearchFilters WithBin(TaskSearchBin bin, bool on)
    {
        var off = new HashSet<TaskSearchBin>(BinsOff);
        if (on)
        {
            off.Remove(bin);
        }
        else
        {
            off.Add(bin);
        }

        return this with { BinsOff = off };
    }

    public TaskSearchFilters WithLabel(Guid labelId, TaskSearchPick pick)
    {
        var picks = new Dictionary<Guid, TaskSearchPick>(Labels);
        if (pick == TaskSearchPick.Any)
        {
            picks.Remove(labelId);
        }
        else
        {
            picks[labelId] = pick;
        }

        return this with { Labels = picks };
    }

    public TaskSearchFilters WithTag(string tag, TaskSearchPick pick)
    {
        string key = TagKey(tag);
        var picks = new Dictionary<string, TaskSearchPick>(Tags, StringComparer.Ordinal);
        if (pick == TaskSearchPick.Any || key.Length == 0)
        {
            picks.Remove(key);
        }
        else
        {
            picks[key] = pick;
        }

        return this with { Tags = picks };
    }

    /// <summary>The same filters for the Archive ("Search the Archive for …", 7.40): Bins dropped (the Archive's pills do that job).</summary>
    public TaskSearchFilters ForArchive() => this with { BinsOff = NoBins };

    /// <summary>The Bins chip a task's bin falls under; null for none.</summary>
    public static TaskSearchBin? BinOf(TaskBin bin)
    {
        return bin switch
        {
            TaskBin.Next => TaskSearchBin.Next,
            TaskBin.Sort => TaskSearchBin.Sort,
            TaskBin.Later => TaskSearchBin.Later,
            _ => TaskSearchBin.Today,
        };
    }
}
