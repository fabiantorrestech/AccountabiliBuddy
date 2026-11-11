using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// An old PC filter (a saved filter or the current one, <see cref="LegacyTaskFilter"/>) as a view's settings (ADR 76): the migration
/// uses it for the filters that existed then, and the PC's old filter screen for the ones saved afterwards, so they become views too.
/// Statuses become bins (To sort for Come back to and Auto-saved, Next and Later for Later, Finished today for Done), categories
/// labels, the old due and sort names their new values. Search words are not part of it (they stay on this PC).
/// </summary>
public static class LegacyTaskFilterMapper
{
    /// <param name="categoryNames">Old category ids to their names (the label is found by name).</param>
    /// <param name="labelByName">The label id for a name; null when there is none (the category is then left out of the filter).</param>
    /// <param name="unmapped">How many status names had no bin (counted for the migration's report).</param>
    public static TaskViewConfig Config(LegacyTaskFilter filter, IReadOnlyDictionary<Guid, string> categoryNames, Func<string, Guid?> labelByName, ref int unmapped)
    {
        var bins = new List<TaskBin>();
        foreach (string status in filter.Statuses)
        {
            IEnumerable<TaskBin> mapped = status switch
            {
                "ComeBack" or "AutoSaved" => [TaskBin.Sort],
                "Later" => [TaskBin.Next, TaskBin.Later],
                "Done" => [TaskBin.Done],
                _ => [],
            };
            List<TaskBin> list = mapped.ToList();
            if (list.Count == 0)
            {
                unmapped++;
            }

            bins.AddRange(list.Where(bin => !bins.Contains(bin)));
        }

        List<Guid> labels = filter.CategoryIds
            .Select(id => categoryNames.TryGetValue(id, out string? name) ? labelByName(name) : null)
            .OfType<Guid>()
            .Distinct()
            .ToList();
        IReadOnlyList<string>? due = filter.Due switch
        {
            "Overdue" => ["overdue"],
            "Today" => ["today"],
            "ThisWeek" => ["thisWeek"],
            "NoDate" => ["none"],
            _ => null,
        };
        string sort = filter.Sort switch
        {
            "DueAscending" => TaskViewConfig.SortDueAscending,
            "DueDescending" => TaskViewConfig.SortDueDescending,
            "MostRecent" => TaskViewConfig.SortNewest,
            "Oldest" => TaskViewConfig.SortOldest,
            _ => TaskViewConfig.SortDefault,
        };

        return new TaskViewConfig(
            new TaskViewFilter(Due: due),
            Labels: labels.Count == 0 ? null : labels,
            NoLabel: filter.NoCategory,
            Sort: sort,
            Bins: bins.Count == 0 ? null : bins);
    }
}
