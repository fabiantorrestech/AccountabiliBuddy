using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// The left-to-right order of the Shelf's columns (round 2 R3, brief §4.6): per grouping, the order this PC stored
/// (<see cref="TaskLayoutPreferences.ColumnOrder"/>) first, then any column it doesn't name in the grouping's own order (a new label or
/// tag lands at the right). Moving a column stores the whole order shown, followed by the stored keys not shown now (a label filtered
/// away keeps its place for when it comes back). Pure, so the checks can call it.
/// </summary>
public static class TaskColumnOrder
{
    /// <summary>
    /// The grouping a view's Group by stores its order under: "bins", "label" or "tag"; null for None (one list, nothing to order).
    /// </summary>
    public static string? GroupingOf(string group)
    {
        return group switch
        {
            TaskViewConfig.GroupBins => TaskViewConfig.GroupBins,
            TaskViewConfig.GroupLabel => TaskViewConfig.GroupLabel,
            TaskViewConfig.GroupTag => TaskViewConfig.GroupTag,
            _ => null,
        };
    }

    /// <summary><paramref name="items"/> in the stored order (by <paramref name="keyOf"/>), the ones it doesn't name after them as they came.</summary>
    public static List<T> Apply<T>(IReadOnlyList<T> items, Func<T, string> keyOf, IReadOnlyList<string>? stored)
    {
        if (stored is not { Count: > 0 })
        {
            return items.ToList();
        }

        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < stored.Count; index++)
        {
            rank.TryAdd(stored[index], index);
        }

        // A stable sort: named columns by their stored place, the rest after them in their own order.
        return items
            .Select((item, index) => (Item: item, Index: index, Rank: rank.TryGetValue(keyOf(item), out int place) ? place : int.MaxValue))
            .OrderBy(entry => entry.Rank)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Item)
            .ToList();
    }

    /// <summary>
    /// The order to store after <paramref name="key"/> moved <paramref name="step"/> places (Alt+←/→: -1 or 1) among the columns
    /// <paramref name="shown"/>; null when it can't move (an end, or not shown).
    /// </summary>
    public static IReadOnlyList<string>? MovedBy(IReadOnlyList<string> shown, IReadOnlyList<string>? stored, string key, int step)
    {
        int from = IndexOf(shown, key);
        int to = from + step;
        return from < 0 || to < 0 || to >= shown.Count ? null : MovedTo(shown, stored, key, to);
    }

    /// <summary>
    /// The order to store after <paramref name="key"/> was dragged onto the place of <paramref name="targetKey"/> among the columns
    /// <paramref name="shown"/> (it takes that place; the others shift by one); null when nothing changes.
    /// </summary>
    public static IReadOnlyList<string>? MovedOnto(IReadOnlyList<string> shown, IReadOnlyList<string>? stored, string key, string targetKey)
    {
        int from = IndexOf(shown, key);
        int to = IndexOf(shown, targetKey);
        return from < 0 || to < 0 || from == to ? null : MovedTo(shown, stored, key, to);
    }

    private static List<string> MovedTo(IReadOnlyList<string> shown, IReadOnlyList<string>? stored, string key, int to)
    {
        List<string> order = shown.ToList();
        order.Remove(key);
        order.Insert(to, key);
        foreach (string hidden in stored ?? [])
        {
            if (!order.Contains(hidden, StringComparer.Ordinal))
            {
                order.Add(hidden);
            }
        }

        return order;
    }

    private static int IndexOf(IReadOnlyList<string> keys, string key)
    {
        for (int index = 0; index < keys.Count; index++)
        {
            if (string.Equals(keys[index], key, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
