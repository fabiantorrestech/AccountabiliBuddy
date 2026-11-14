using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The PC's category list (ADR 24, 62) as edits of the labels (ADR 76), while its screens still edit categories: a new category is
/// a new label (the name-based id), a rename or recolour edits the label it maps to, a new order moves the labels, and a category
/// taken off deletes its label and leaves its tasks with no label (as deleting a category always did). Each category keeps its own
/// id on this PC through the map from label id to category id (<see cref="TaskFacadeLists.Categories"/>).
/// </summary>
public static class TaskCategoryEdits
{
    /// <param name="hiddenLabels">ADR 89: the labels this device doesn't show; never deleted for missing from <paramref name="categories"/>.</param>
    /// <returns>The lists with the labels as the categories say, and the id map with any new category's id.</returns>
    public static (TaskLists Lists, Dictionary<Guid, Guid> CategoryIds) Apply(
        TaskLists lists, IReadOnlyList<TaskCategory> categories, IReadOnlyDictionary<Guid, Guid> categoryIds, Guid self, DateTimeOffset now,
        IReadOnlySet<Guid>? hiddenLabels = null)
    {
        var labels = lists.Labels.Select(item => item.Label).ToList();
        var ids = new Dictionary<Guid, Guid>(categoryIds);
        Dictionary<Guid, Guid> labelOfCategory = ids.GroupBy(pair => pair.Value).ToDictionary(group => group.Key, group => group.First().Key);
        var kept = new List<Guid>();

        foreach (TaskCategory category in categories)
        {
            string name = category.Name.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            int index = FindLabel(labels, category, labelOfCategory, name);
            if (index < 0)
            {
                index = AddLabel(labels, name, category.Color, now, self);
            }
            else
            {
                TaskLabel held = labels[index];
                bool renamed = !string.Equals(held.Name, name, StringComparison.Ordinal);
                bool recoloured = !TaskLabelColors.SameOpaque(held.Color, category.Color);
                if (renamed || recoloured)
                {
                    labels[index] = held with
                    {
                        Name = name,
                        Color = recoloured ? category.Color : held.Color,
                        EditedAt = Next(now, held.EditedAt),
                    };
                }
            }

            Guid labelId = labels[index].Id;
            if (kept.Contains(labelId))
            {
                continue;
            }

            kept.Add(labelId);
            if (category.Id != labelId)
            {
                ids[labelId] = category.Id;
            }
        }

        // The order the categories are in, when it differs from the labels' own.
        List<Guid> current = labels.Where(label => label.IsLive && kept.Contains(label.Id))
            .OrderBy(label => label.Position).ThenBy(label => label.Id.ToString("D"), StringComparer.Ordinal)
            .Select(label => label.Id).ToList();
        if (!current.SequenceEqual(kept))
        {
            for (int position = 0; position < kept.Count; position++)
            {
                int index = labels.FindIndex(label => label.Id == kept[position]);
                if (labels[index].Position != position + 1)
                {
                    labels[index] = labels[index] with { Position = position + 1, EditedAt = Next(now, labels[index].EditedAt) };
                }
            }
        }

        // Categories taken off: their labels go, and their tasks are left with none.
        var gone = new HashSet<Guid>();
        for (int index = 0; index < labels.Count; index++)
        {
            // ADR 89: a label this device doesn't show (device-only elsewhere) isn't in its category list, so it is never "taken off" here.
            if (labels[index].IsLive && !kept.Contains(labels[index].Id) && hiddenLabels?.Contains(labels[index].Id) != true)
            {
                gone.Add(labels[index].Id);
                labels[index] = labels[index].Deleted(Next(now, labels[index].EditedAt));
            }
        }

        List<SyncTaskItem> tasks = lists.Tasks
            .Select(item => item.Task.IsLive && item.Task.LabelId is Guid labelId && gone.Contains(labelId)
                ? TaskEdits.Edit(item, TaskField.Label, item.Task with { LabelId = null }, now, self)
                : item)
            .ToList();

        return (lists with { Tasks = tasks, Labels = labels.Select(label => new SyncTaskLabel(label)).ToList() }, ids);
    }

    // The label a category is: by the id it had here, else (a category that came in by name) the live label of that name.
    private static int FindLabel(List<TaskLabel> labels, TaskCategory category, Dictionary<Guid, Guid> labelOfCategory, string name)
    {
        Guid labelId = labelOfCategory.TryGetValue(category.Id, out Guid mapped) ? mapped : category.Id;
        int index = labels.FindIndex(label => label.Id == labelId && label.IsLive);
        return index >= 0 ? index : labels.FindIndex(label => label.IsLive && TaskLabel.SameName(label.Name, name));
    }

    // A deleted label that comes back rings on every device again, stamped by self at the revive (TaskLabelMerge).
    private static int AddLabel(List<TaskLabel> labels, string name, string color, DateTimeOffset now, Guid self)
    {
        // The name-based id, or (taken by a label renamed since) the name's next numbered one.
        Guid id = TaskLabelBook.FreeIdFor(name, labels);

        double position = labels.Where(label => label.IsLive).Select(label => label.Position).DefaultIfEmpty(0).Max() + 1;
        int existing = labels.FindIndex(label => label.Id == id);
        DateTimeOffset stamp = existing >= 0 ? Next(now, labels[existing].DeletedAt ?? labels[existing].EditedAt) : now;
        var label = new TaskLabel(id, name, TaskLabelColors.IsValid(color) ? color.Trim() : TaskLabelColors.FirstUnused(labels), position, stamp);
        if (existing >= 0)
        {
            labels[existing] = label with { RingOnStamp = new TaskFieldStamp(stamp, self), OnlyOnStamp = new TaskFieldStamp(stamp, self) };
            return existing;
        }

        labels.Add(label);
        return labels.Count - 1;
    }

    private static DateTimeOffset Next(DateTimeOffset now, DateTimeOffset held)
    {
        return held >= now ? held.AddTicks(1) : now;
    }
}
