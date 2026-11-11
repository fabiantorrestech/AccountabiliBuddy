using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Changes to the synced list of saved views (ADR 76), pure: every change stamps the view (the newest version wins on every
/// device, <see cref="SharedListMerge"/>). The three built-in views (<see cref="TaskView.BuiltIns"/>) can be changed but never deleted.
/// A view's name is private text: a deleted view keeps none.
/// </summary>
public static class TaskViewEdits
{
    public const int MaxNameLength = 40;
    public const int MaxViews = 30;
    public const string DefaultName = "My view";

    /// <summary>The live views in their order.</summary>
    public static IReadOnlyList<TaskView> Live(TaskLists lists)
    {
        return lists.Views.Select(item => item.View).Where(view => view.IsLive).OrderBy(view => view.Position).ThenBy(view => view.Id).ToList();
    }

    /// <summary>The lists with the built-in views that are missing (a fresh list, or an older one): Default stays as it is when present.</summary>
    public static TaskLists WithBuiltIns(TaskLists lists, DateTimeOffset editedAt)
    {
        List<SyncTaskView> views = lists.Views.ToList();
        foreach (TaskView builtIn in TaskView.BuiltIns(editedAt))
        {
            if (!views.Any(item => item.Id == builtIn.Id))
            {
                views.Add(new SyncTaskView(builtIn));
            }
        }

        return views.Count == lists.Views.Count ? lists : lists with { Views = views };
    }

    /// <summary>Keeps the settings as a new view at the end of the list; null when the list already holds <see cref="MaxViews"/>.</summary>
    public static (TaskLists Lists, TaskView? Saved) Save(TaskLists lists, string name, string? icon, TaskViewConfig config, DateTimeOffset now)
    {
        IReadOnlyList<TaskView> live = Live(lists);
        if (live.Count >= MaxViews)
        {
            return (lists, null);
        }

        var view = new TaskView(Guid.NewGuid(), CleanName(name), icon, config, (live.Count == 0 ? 0 : live.Max(other => other.Position)) + 1, now);
        return (lists with { Views = [.. lists.Views, new SyncTaskView(view)] }, view);
    }

    /// <summary>Changes a live view's name, icon or settings (null keeps what it has).</summary>
    public static TaskLists Update(TaskLists lists, Guid id, string? name, string? icon, TaskViewConfig? config, DateTimeOffset now)
    {
        return Replace(lists, id, view => view with
        {
            Name = name is null ? view.Name : CleanName(name),
            Icon = icon ?? view.Icon,
            Config = config ?? view.Config,
        }, now);
    }

    /// <summary>
    /// A saved filter of the PC's old filter screen (<paramref name="id"/> is the preset's) as a view: new at the end, or the view
    /// with that id updated in place.
    /// </summary>
    public static TaskLists Upsert(TaskLists lists, Guid id, string name, TaskViewConfig config, DateTimeOffset now)
    {
        if (lists.Views.Any(item => item.Id == id && item.View.IsLive))
        {
            return Update(lists, id, name, null, config, now);
        }

        IReadOnlyList<TaskView> live = Live(lists);
        double position = (live.Count == 0 ? 0 : live.Max(other => other.Position)) + 1;
        var view = new TaskView(id, CleanName(name), null, config, position, now);
        int index = lists.Views.ToList().FindIndex(item => item.Id == id);
        if (index < 0)
        {
            return lists with { Views = [.. lists.Views, new SyncTaskView(view)] };
        }

        // A deleted view comes back: stamped after its tombstone.
        List<SyncTaskView> views = lists.Views.ToList();
        views[index] = new SyncTaskView(view with { EditedAt = SharedListMerge.NextStamp(now, views[index]) });
        return lists with { Views = views };
    }

    /// <summary>Deletes a view (a tombstone with no text). A built-in view or an unknown id changes nothing.</summary>
    public static TaskLists Delete(TaskLists lists, Guid id, DateTimeOffset now)
    {
        if (lists.Views.FirstOrDefault(item => item.Id == id) is not { View: { IsLive: true, BuiltIn: false } })
        {
            return lists;
        }

        return Replace(lists, id, view => view with { Name = string.Empty, Icon = null, Config = TaskViewConfig.Everything, DeletedAt = now }, now);
    }

    /// <summary>Stores the order Shuffle gave (a new seed) in the view's settings.</summary>
    public static TaskLists Reshuffled(TaskLists lists, Guid id, int seed, DateTimeOffset now)
    {
        return Replace(lists, id, view => view with { Config = view.Config with { Seed = seed } }, now);
    }

    public static string CleanName(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
        {
            trimmed = trimmed[..MaxNameLength].TrimEnd();
        }

        return trimmed.Length == 0 ? DefaultName : trimmed;
    }

    private static TaskLists Replace(TaskLists lists, Guid id, Func<TaskView, TaskView> change, DateTimeOffset now)
    {
        List<SyncTaskView> views = lists.Views.ToList();
        int index = views.FindIndex(item => item.Id == id && item.View.IsLive);
        if (index < 0)
        {
            return lists;
        }

        SyncTaskView held = views[index];
        TaskView changed = change(held.View);
        DateTimeOffset at = SharedListMerge.NextStamp(now, held);
        views[index] = new SyncTaskView(changed with { EditedAt = at, DeletedAt = changed.DeletedAt is null ? null : at });
        return lists with { Views = views };
    }
}
