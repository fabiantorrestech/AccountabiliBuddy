using System.Globalization;
using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// What a view shows and in what order (ADR 76, spec 05 section 10), as one pure function: <see cref="Apply"/> filters
/// (<see cref="Matches"/>), groups (bins, label, tag, none) and sorts (manual, due, newest, oldest, untouched, shuffle, longest,
/// shortest), with highlighted tasks first when the view pins them. Filters: ticks inside one list widen it (any can match),
/// different lists narrow it (each must match); an unknown value from a newer app matches nothing. Sort and group values this
/// app doesn't know fall back to manual order and bins.
/// </summary>
public static class TaskViewRules
{
    private const int WeekDays = 7;

    /// <summary>The tasks that stay, grouped and sorted as <paramref name="config"/> says.</summary>
    public static TaskViewResult Apply(IEnumerable<TaskItem> tasks, TaskViewConfig config, TaskViewContext context)
    {
        List<TaskItem> inScope = tasks.Where(task => task.IsLive && (context.Scope is null || context.Scope.Contains(task.Bin))).ToList();
        HashSet<Guid>? found = string.IsNullOrWhiteSpace(context.Search) ? null : TaskSearch.MatchingTaskIds(inScope, context.Search, context.Labels, context.LabelShown, context.TagShown);
        List<TaskItem> shown = inScope.Where(task => (found is null || found.Contains(task.Id)) && Matches(task, config, context)).ToList();
        Comparison<TaskItem> order = Order(config);

        List<TaskViewGroup> groups = config.Group switch
        {
            TaskViewConfig.GroupLabel => ByLabel(shown, context),
            TaskViewConfig.GroupTag => ByTag(shown, context),
            TaskViewConfig.GroupNone => [new TaskViewGroup("all", "All", null, null, null, shown)],
            _ => ByBin(shown, context),
        };
        return new TaskViewResult(groups.Select(group => group with { Tasks = group.Tasks.Order(Comparer<TaskItem>.Create(order)).ToList() }).ToList());
    }

    /// <summary>Whether a task passes the view's filters (not its search words, which <see cref="Apply"/> takes from the context).</summary>
    public static bool Matches(TaskItem task, TaskViewConfig config, TaskViewContext context)
    {
        if (config.Bins is { Count: > 0 } bins && !bins.Contains(task.Bin))
        {
            return false;
        }

        TaskViewFilter only = config.Only ?? TaskViewFilter.None;
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(context.Now, context.Zone).DateTime);
        TimeOnly time = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(context.Now, context.Zone).DateTime);
        return (only.Due is not { Count: > 0 } || only.Due.Any(pick => DueMatches(task, pick, today, time)))
            && (only.Time is not { Count: > 0 } || only.Time.Any(pick => TimeMatches(task, pick)))
            && (only.Old is not { Count: > 0 } || only.Old.Any(pick => OldMatches(task, pick, context.Now)))
            && (!only.HighlightedOnly || task.Highlight)
            && LabelMatches(task, config, context)
            && TagsMatch(task, config);
    }

    /// <summary>The view's settings as one text, to tell whether two are the same (Save is faded until something changed).</summary>
    public static string Signature(TaskViewConfig config)
    {
        return JsonSerializer.Serialize(config, SyncJson.Options);
    }

    private static bool DueMatches(TaskItem task, string pick, DateOnly today, TimeOnly time)
    {
        if (pick == "none")
        {
            return task.DueOn is null;
        }

        if (task.DueOn is not DateOnly due)
        {
            return false;
        }

        if (pick.StartsWith("range:", StringComparison.Ordinal))
        {
            string[] ends = pick["range:".Length..].Split("..");
            return ends.Length == 2
                && DateOnly.TryParseExact(ends[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly from)
                && DateOnly.TryParseExact(ends[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly to)
                && due >= from && due <= to;
        }

        return pick switch
        {
            "overdue" => !task.IsDone && (due < today || (due == today && task.DueAt is TimeOnly at && at < time)),
            "today" => due == today,
            "tomorrow" => due == today.AddDays(1),
            "thisWeek" => due >= today && due <= today.AddDays(WeekDays - 1),
            _ => false,
        };
    }

    private static bool TimeMatches(TaskItem task, string pick)
    {
        int? minutes = task.EstimateMinutes;
        return pick switch
        {
            "le10" => minutes <= 10,
            "le25" => minutes <= 25,
            "le50" => minutes <= 50,
            "ge50" => minutes >= 50,
            "none" => minutes is null,
            _ => false,
        };
    }

    private static bool OldMatches(TaskItem task, string pick, DateTimeOffset now)
    {
        if (pick.Length < 2 || pick[^1] != 'w' || !int.TryParse(pick[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int weeks))
        {
            return false;
        }

        return TaskUntouched.IsOlderThan(task, weeks, now);
    }

    // A task whose label this device doesn't show (ADR 89) reads as No label.
    private static bool LabelMatches(TaskItem task, TaskViewConfig config, TaskViewContext context)
    {
        if (config.Labels is not { Count: > 0 } && !config.NoLabel)
        {
            return true;
        }

        Guid? label = context.LabelShown is null || task.LabelId is not Guid held || context.ShownLabelIds.Contains(held) ? task.LabelId : null;
        return label is Guid shown ? config.Labels?.Contains(shown) == true : config.NoLabel;
    }

    private static bool TagsMatch(TaskItem task, TaskViewConfig config)
    {
        if (config.Tags is not { Count: > 0 } tags)
        {
            return true;
        }

        return config.Match == TaskViewConfig.MatchAll ? tags.All(tag => TaskLabels.Has(task.Tags, tag)) : tags.Any(tag => TaskLabels.Has(task.Tags, tag));
    }

    private static List<TaskViewGroup> ByBin(List<TaskItem> shown, TaskViewContext context)
    {
        IEnumerable<TaskBin> bins = context.ShowEmptyBins ? context.Scope ?? Enum.GetValues<TaskBin>() : shown.Select(task => task.Bin).Distinct().Order();
        return bins.Select(bin => new TaskViewGroup("bin:" + bin.ToString().ToLowerInvariant(), TaskBins.Name(bin), bin, null, null, shown.Where(task => task.Bin == bin).ToList())).ToList();
    }

    private static List<TaskViewGroup> ByLabel(List<TaskItem> shown, TaskViewContext context)
    {
        var groups = new List<TaskViewGroup>();
        foreach (TaskLabel label in context.ShownLabels.Where(label => label.IsLive).OrderBy(label => label.Position).ThenBy(label => label.Name, StringComparer.OrdinalIgnoreCase))
        {
            List<TaskItem> of = shown.Where(task => task.LabelId == label.Id).ToList();
            if (of.Count > 0)
            {
                groups.Add(new TaskViewGroup("label:" + label.Id.ToString("D"), label.Name, null, label.Id, null, of));
            }
        }

        HashSet<Guid> known = context.ShownLabelIds;
        List<TaskItem> without = shown.Where(task => task.LabelId is not Guid id || !known.Contains(id)).ToList();
        if (without.Count > 0)
        {
            groups.Add(new TaskViewGroup("label:none", "No label", null, null, null, without));
        }

        return groups;
    }

    private static List<TaskViewGroup> ByTag(List<TaskItem> shown, TaskViewContext context)
    {
        var spellings = new List<string>();
        foreach (string tag in shown.SelectMany(task => context.ShownTags(task) ?? []))
        {
            if (!spellings.Any(other => TaskLabels.Same(other, tag)))
            {
                spellings.Add(tag);
            }
        }

        var groups = spellings.Order(StringComparer.OrdinalIgnoreCase)
            .Select(tag => new TaskViewGroup("tag:" + tag.ToLowerInvariant(), "#" + tag, null, null, tag, shown.Where(task => TaskLabels.Has(context.ShownTags(task), tag)).ToList()))
            .ToList();
        List<TaskItem> without = shown.Where(task => context.ShownTags(task) is not { Count: > 0 }).ToList();
        if (without.Count > 0)
        {
            groups.Add(new TaskViewGroup("tag:none", "No tag", null, null, null, without));
        }

        return groups;
    }

    // Highlighted first (when pinned), then the sort, then the manual order (bin, place, age, id) so equal tasks never swap places.
    private static Comparison<TaskItem> Order(TaskViewConfig config)
    {
        Comparison<TaskItem> sort = Sort(config);
        return (first, second) =>
        {
            int result = config.PinHighlights ? second.Highlight.CompareTo(first.Highlight) : 0;
            result = result != 0 ? result : sort(first, second);
            return result != 0 ? result : Manual(first, second);
        };
    }

    private static int Manual(TaskItem first, TaskItem second)
    {
        int result = first.Bin.CompareTo(second.Bin);
        result = result != 0 ? result : first.Position.CompareTo(second.Position);
        result = result != 0 ? result : first.CreatedAt.CompareTo(second.CreatedAt);
        return result != 0 ? result : first.Id.CompareTo(second.Id);
    }

    private static Comparison<TaskItem> Sort(TaskViewConfig config)
    {
        int seed = config.Seed ?? 0;
        return config.Sort switch
        {
            TaskViewConfig.SortDueAscending => (a, b) => CompareLast(DueKey(a), DueKey(b), descending: false),
            TaskViewConfig.SortDueDescending => (a, b) => CompareLast(DueKey(a), DueKey(b), descending: true),
            TaskViewConfig.SortNewest => (a, b) => b.CreatedAt.CompareTo(a.CreatedAt),
            TaskViewConfig.SortOldest => (a, b) => a.CreatedAt.CompareTo(b.CreatedAt),
            TaskViewConfig.SortUntouched => (a, b) => a.TouchedAt.CompareTo(b.TouchedAt),
            TaskViewConfig.SortShuffle => (a, b) => ShuffleKey(a.Id, seed).CompareTo(ShuffleKey(b.Id, seed)),
            TaskViewConfig.SortLongest => (a, b) => CompareLast(a.EstimateMinutes, b.EstimateMinutes, descending: true),
            TaskViewConfig.SortShortest => (a, b) => CompareLast(a.EstimateMinutes, b.EstimateMinutes, descending: false),
            _ => (_, _) => 0,
        };
    }

    private static long? DueKey(TaskItem task)
    {
        return task.DueOn is DateOnly due ? due.ToDateTime(task.DueAt ?? TimeOnly.MinValue).Ticks : null;
    }

    // Tasks with no value (no due date, no time limit) go last in either direction.
    private static int CompareLast(long? first, long? second, bool descending)
    {
        if (first is null || second is null)
        {
            return first is null ? (second is null ? 0 : 1) : -1;
        }

        return descending ? second.Value.CompareTo(first.Value) : first.Value.CompareTo(second.Value);
    }

    // A fixed hash (FNV-1a over the id's bytes, then the seed), so a stored seed gives the same order on every device and every run.
    private static uint ShuffleKey(Guid id, int seed)
    {
        uint hash = 2166136261;
        foreach (byte value in id.ToByteArray())
        {
            hash = (hash ^ value) * 16777619;
        }

        foreach (byte value in BitConverter.GetBytes(seed))
        {
            hash = (hash ^ value) * 16777619;
        }

        return hash;
    }
}
