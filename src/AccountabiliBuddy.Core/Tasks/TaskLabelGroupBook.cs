using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Reads the label groups (ADR 93; "categories" in the UI) for display and edits: the live groups in their order, a group by id or name, and the
/// group a label shows under. A label whose group is missing or deleted reads as uncategorised; nothing here (or anywhere) rewrites the label
/// for it.
/// </summary>
public static class TaskLabelGroupBook
{
    /// <summary>The live groups, by position, then id (the same order on every device).</summary>
    public static IReadOnlyList<TaskLabelGroup> Live(IEnumerable<SyncTaskLabelGroup>? groups)
    {
        return (groups ?? [])
            .Select(item => item.Group)
            .Where(group => group is { IsLive: true })
            .OrderBy(group => group.Position)
            .ThenBy(group => group.Id.ToString("D"), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The live group with <paramref name="id"/>; null when there is none (never made, or deleted).</summary>
    public static TaskLabelGroup? Find(IEnumerable<SyncTaskLabelGroup>? groups, Guid id)
    {
        return (groups ?? []).Select(item => item.Group).FirstOrDefault(group => group is { IsLive: true } && group.Id == id);
    }

    /// <summary>The live group named <paramref name="name"/> (case-insensitive, trimmed); null when none.</summary>
    public static TaskLabelGroup? Named(IEnumerable<SyncTaskLabelGroup>? groups, string name)
    {
        return (groups ?? []).Select(item => item.Group).FirstOrDefault(group => group is { IsLive: true } && TaskLabel.SameName(group.Name, name));
    }

    /// <summary>The live groups by id, for looking up many labels at once.</summary>
    public static IReadOnlyDictionary<Guid, TaskLabelGroup> LiveById(IEnumerable<TaskLabelGroup>? groups)
    {
        return (groups ?? []).Where(group => group.IsLive).GroupBy(group => group.Id).ToDictionary(group => group.Key, group => group.First());
    }

    /// <summary>The live group <paramref name="label"/> shows under; null when uncategorised (no group, or its group is missing or deleted).</summary>
    public static TaskLabelGroup? GroupOf(TaskLabel label, IReadOnlyDictionary<Guid, TaskLabelGroup> liveById)
    {
        return label.GroupId is Guid id && liveById.TryGetValue(id, out TaskLabelGroup? group) ? group : null;
    }

    /// <summary>
    /// The id for a new group named <paramref name="name"/>: its name-based id (<see cref="TaskLabelGroup.IdFor"/>), unless a live group renamed
    /// since holds that id; then the next free one of the name's numbered ids (the name with "#2", "#3"... appended before hashing), as labels
    /// do. Never a live group's id; a tombstone's id may be given (that group comes back).
    /// </summary>
    public static Guid FreeIdFor(string name, IReadOnlyList<TaskLabelGroup> groups)
    {
        Guid id = TaskLabelGroup.IdFor(name);
        for (int suffix = 2; groups.Any(group => group.Id == id && group.IsLive); suffix++)
        {
            id = TaskLabelGroup.IdFor(name.Trim() + "#" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return id;
    }
}
