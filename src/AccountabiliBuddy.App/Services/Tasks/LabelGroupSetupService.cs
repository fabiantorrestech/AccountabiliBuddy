using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// The "Label categories" setup page's path to the task list (ADR 93): the groups suggested from shared name prefixes
/// (<see cref="LabelGroupSuggestions"/>) and making the ones the user ticks. Each group is a stamped group edit and each label's move a stamped
/// edit of its group choice alone (one change of the task list, so it syncs); "Shorten the names" renames through the PC's category list, as the
/// Labels screen does (it also carries the names to a running checklist), skipping any name that would clash. Labels loosen nothing, so this works
/// during a focus session too. Use from the UI thread only.
/// </summary>
public sealed class LabelGroupSetupService
{
    private readonly TaskStore _store;
    private readonly SessionTasksService _tasks;

    public LabelGroupSetupService(TaskStore store, SessionTasksService tasks)
    {
        _store = store;
        _tasks = tasks;
    }

    /// <summary>Whether a live label group exists (the setup feature counts as set up).</summary>
    public static bool HasGroups(TaskStore store)
    {
        return store.IsActive && TaskLabelGroupBook.Live(store.Lists.LabelGroups).Count > 0;
    }

    /// <summary>Whether the labels this PC shows have anything to suggest.</summary>
    public static bool HasSuggestions(TaskStore store)
    {
        return store.IsActive && Suggest(store).Count > 0;
    }

    /// <summary>The suggested groups, from the labels this PC shows (ADR 89: never another device's device-only label).</summary>
    public IReadOnlyList<LabelGroupSuggestion> Suggestions()
    {
        return _store.IsActive ? Suggest(_store) : [];
    }

    /// <summary>
    /// Makes the <paramref name="chosen"/> groups (a live group that already has the name is used as it is) and moves their labels into them;
    /// with <paramref name="shorten"/>, renames each label to its short name, skipping the ones marked skipped and any name taken by then.
    /// Returns how many labels were renamed.
    /// </summary>
    public async Task<int> CreateAsync(IReadOnlyList<LabelGroupSuggestion> chosen, bool shorten)
    {
        if (!_store.IsActive || chosen.Count == 0)
        {
            return 0;
        }

        await _store.ChangeAsync(this, (lists, self, now) =>
        {
            TaskLists changed = lists;
            foreach (LabelGroupSuggestion suggestion in chosen)
            {
                Guid? groupId = GroupFor(ref changed, suggestion, now);
                if (groupId is not Guid id)
                {
                    continue;
                }

                foreach (LabelGroupSuggestionItem item in suggestion.Labels)
                {
                    changed = TaskLabelEdits.SetGroup(changed, item.LabelId, id, now, self);
                }
            }

            return changed;
        });

        return shorten ? await ShortenAsync(chosen) : 0;
    }

    private static IReadOnlyList<LabelGroupSuggestion> Suggest(TaskStore store)
    {
        List<TaskLabel> labels = store.Lists.Labels.Select(item => item.Label).ToList();
        return LabelGroupSuggestions.For(labels, store.Lists.LabelGroups, store.LabelShown);
    }

    // The suggestion's group: a live one of that name (made meanwhile, or before), else a new one (or a deleted one of that name brought back).
    private static Guid? GroupFor(ref TaskLists lists, LabelGroupSuggestion suggestion, DateTimeOffset now)
    {
        if (TaskLabelGroupBook.Named(lists.LabelGroups, suggestion.Name) is { } held)
        {
            return held.Id;
        }

        lists = TaskLabelGroupEdits.Add(lists, suggestion.Name, null, now, out Guid? id);
        return id;
    }

    private async Task<int> ShortenAsync(IReadOnlyList<LabelGroupSuggestion> chosen)
    {
        int renamed = 0;
        foreach (LabelGroupSuggestionItem item in chosen.SelectMany(suggestion => suggestion.Labels).Where(item => !item.Skipped))
        {
            // Checked again against the labels as they are now (an earlier rename, another device's label arrived meanwhile).
            List<TaskLabel> labels = _store.Lists.Labels.Select(entry => entry.Label).ToList();
            if (!labels.Any(label => label.Id == item.LabelId && label.IsLive)
                || TaskLabelEdits.CheckName(labels, item.LabelId, item.ShortName, _store.SelfDevice, _store.Removed) != TaskLabelNameCheck.Fine)
            {
                continue;
            }

            Guid categoryId = _store.CategoryIds.TryGetValue(item.LabelId, out Guid mapped) ? mapped : item.LabelId;
            await _tasks.RenameCategoryAsync(categoryId, item.ShortName);
            renamed++;
        }

        return renamed;
    }
}
