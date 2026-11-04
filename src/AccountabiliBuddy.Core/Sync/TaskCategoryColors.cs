using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The newest colour choice wins on every device (ADR 62): a category takes the colour another device lists for the same name (any case)
/// when that device chose it later. Nothing else about the category changes, and no category is created.
/// </summary>
public static class TaskCategoryColors
{
    /// <returns>The list with the newer colours taken; null when nothing changed.</returns>
    /// <param name="others">What the other devices list.</param>
    /// <param name="now">A stamp further ahead than <see cref="SharedListMerge.FutureLimit"/> doesn't count, the shared lists' rule.</param>
    public static IReadOnlyList<TaskCategory>? TakeNewer(IReadOnlyList<TaskCategory> own, IEnumerable<SyncCategory> others, DateTimeOffset now)
    {
        List<SyncCategory> stamped = others
            .Where(category => category.ColorEditedAt is DateTimeOffset at && at <= now + SharedListMerge.FutureLimit)
            .ToList();
        bool changed = false;
        var result = new List<TaskCategory>(own.Count);
        foreach (TaskCategory category in own)
        {
            SyncCategory? newest = stamped
                .Where(other => string.Equals(other.Name, category.Name, StringComparison.OrdinalIgnoreCase))
                .MaxBy(other => other.ColorEditedAt);
            if (newest is not null && (category.ColorEditedAt is null || newest.ColorEditedAt > category.ColorEditedAt))
            {
                result.Add(category with { Color = newest.Color, ColorEditedAt = newest.ColorEditedAt });
                changed = true;
            }
            else
            {
                result.Add(category);
            }
        }

        return changed ? result : null;
    }
}
