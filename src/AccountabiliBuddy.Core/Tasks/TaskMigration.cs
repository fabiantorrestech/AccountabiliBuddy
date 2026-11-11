using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The one-time move of the old task lists into the one shared list (ADR 76). Pure: it reads the old stores' contents and the
/// time, and gives the new lists; the App backs up the database first, runs it once behind a marker document and keeps the old
/// documents for a release as the way back.
/// <list type="bullet">
/// <item>Categories become labels (id from the name, same name and colour); a category only an item names becomes one too.</item>
/// <item>The next session's list: open tasks fill This session up to the session's actions in order (a task's first open steps
/// are its actions), the rest go to Queued; tasks done today go to Finished; tasks done before today go to History.</item>
/// <item>Later stays Later (Next when a newer PC marked it); ticked Later items go to History.</item>
/// <item>Come-backs and parked thoughts go to To sort ("Parked" and when); ticked ones go to History.</item>
/// <item>Auto-saved goes to To sort ("Auto-saved" and its day); past its 30 days it is left out.</item>
/// <item>The old filter becomes the Default view's settings, the saved filters become views (their search words stay local).</item>
/// <item>Numbers go in creation order; ADR 72 labels become tags; a local item's keeper becomes its one device.</item>
/// </list>
/// Tombstones and local stubs stay tombstones. Ids are kept (labels and built-in views have fixed ids), nothing is random and no
/// stamp comes from <see cref="TaskMigrationInput.Now"/>, so the same input always gives the same result, and merging a result
/// with another run's adds nothing.
/// </summary>
public static class TaskMigration
{
    public const string NextTasksSource = "nextTasks";
    public const string LaterSource = "later";
    public const string ComebacksSource = "comebacks";
    public const string AutoSavedSource = "autoSaved";

    // Stamps on what the user never stamped (a category's colour, the built-in views): older than any real edit.
    private static readonly DateTimeOffset UnstampedAt = GuidanceState.UpgradeStamp;

    private sealed record Draft(string Source, int SourceOrder, TaskItem Task);

    public static TaskMigrationResult Run(TaskMigrationInput input)
    {
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(input.Now, input.Zone).DateTime);

        var book = new TaskLabelBook([]);
        int labelsFromCategories = 0;
        foreach (TaskCategory category in input.Categories)
        {
            if (category.Name.Trim().Length == 0 || book.Named(category.Name) is not null)
            {
                continue;
            }

            book.IdForName(category.Name, category.Color, category.ColorEditedAt ?? UnstampedAt);
            labelsFromCategories++;
        }

        int labelsBeforeItems = book.Made;
        var sources = new Dictionary<string, int> { [NextTasksSource] = 0, [LaterSource] = 0, [ComebacksSource] = 0, [AutoSavedSource] = 0 };
        var sourceTombstones = new Dictionary<string, int>(sources);
        var drafts = new List<Draft>();
        int expired = 0;

        foreach (SyncNextTask item in input.NextTasks)
        {
            Count(item.DeletedAt, NextTasksSource);
            drafts.Add(new Draft(NextTasksSource, 0, FromNextTask(item, book)));
        }

        foreach (SyncLaterItem item in input.Later)
        {
            Count(item.DeletedAt, LaterSource);
            drafts.Add(new Draft(LaterSource, 1, FromLaterItem(item, book)));
        }

        foreach (SyncParked item in input.Comebacks)
        {
            Count(item.DeletedAt, ComebacksSource);
            drafts.Add(new Draft(ComebacksSource, 2, FromParked(item, book, input.Zone)));
        }

        foreach (SyncAutoSavedItem item in input.AutoSaved)
        {
            Count(item.DeletedAt, AutoSavedSource);
            if (item.DeletedAt is null && item.IsExpired(input.Now))
            {
                expired++;
                continue;
            }

            drafts.Add(new Draft(AutoSavedSource, 3, FromAutoSaved(item, book)));
        }

        // One task per id: a live copy over a tombstone, then the newest, then the earlier list.
        int duplicates = 0;
        var winners = new List<Draft>();
        foreach (IGrouping<Guid, Draft> group in drafts.GroupBy(draft => draft.Task.Id))
        {
            List<Draft> ordered = group
                .OrderByDescending(draft => draft.Task.IsLive)
                .ThenByDescending(draft => draft.Task.EditedAt.UtcTicks)
                .ThenBy(draft => draft.SourceOrder)
                .ToList();
            winners.Add(ordered[0]);
            duplicates += ordered.Skip(1).Count(draft => draft.Task.IsLive);
        }

        var tasks = new List<TaskItem>();
        var history = new List<TaskHistoryEntry>();
        var openNext = new List<TaskItem>();
        foreach (Draft draft in winners)
        {
            TaskItem task = draft.Task;
            if (!task.IsLive)
            {
                tasks.Add(task);
                continue;
            }

            if (task.DoneAt is DateTimeOffset doneAt)
            {
                bool doneToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(doneAt, input.Zone).DateTime) == today;
                TaskItem done = task with { Bin = TaskBin.Done, PrevBin = task.Bin };
                if (draft.Source == NextTasksSource && doneToday)
                {
                    tasks.Add(done);
                }
                else
                {
                    history.Add(new TaskHistoryEntry(task.Id, TaskHistoryKind.Finished, doneAt, done, task.EditedAt));
                    tasks.Add(task.Deleted(task.EditedAt));
                }

                continue;
            }

            if (draft.Source == NextTasksSource)
            {
                openNext.Add(task);
            }
            else
            {
                tasks.Add(task);
            }
        }

        tasks.AddRange(FillSession(openNext, input.SessionActions));

        // Numbers in creation order over every live task and History snapshot.
        int nextNumber = Math.Max(1, input.FirstNumber);
        var numbered = new Dictionary<Guid, TaskNumber>();
        foreach (TaskItem task in tasks.Where(task => task.IsLive).Concat(history.Select(entry => entry.Task))
            .OrderBy(task => task.CreatedAt.UtcTicks).ThenBy(task => task.Id.ToString("D"), StringComparer.Ordinal))
        {
            numbered[task.Id] = new TaskNumber(nextNumber++, Temporary: !input.IsNumberingDevice);
        }

        List<SyncTaskItem> syncTasks = tasks
            .Select(task => task.IsLive ? task with { Number = numbered[task.Id] } : task)
            .Select(task => new SyncTaskItem(task, task.IsLive ? StampsAt(task.EditedAt) : TaskFieldStamps.None))
            .OrderBy(item => item.Task.Bin).ThenBy(item => item.Position).ThenBy(item => item.Id.ToString("D"), StringComparer.Ordinal)
            .ToList();
        List<SyncTaskLogItem> syncHistory = history
            .Select(entry => entry with { Task = entry.Task with { Number = numbered[entry.Id] } })
            .Select(entry => new SyncTaskLogItem(entry))
            .OrderBy(item => item.Position).ThenBy(item => item.Id.ToString("D"), StringComparer.Ordinal)
            .ToList();

        (List<TaskView> views, Dictionary<Guid, string> searches, int unmapped) = Views(input, book);

        var bins = Enum.GetValues<TaskBin>().ToDictionary(bin => bin, bin => syncTasks.Count(item => item.Task.IsLive && item.Task.Bin == bin));
        var counts = new TaskMigrationCounts(
            sources,
            sourceTombstones,
            bins,
            syncHistory.Count,
            expired,
            duplicates,
            syncTasks.Count(item => !item.Task.IsLive),
            labelsFromCategories,
            book.Made - labelsBeforeItems,
            views.Count,
            unmapped);

        return new TaskMigrationResult(
            syncTasks,
            book.Labels.Select(label => new SyncTaskLabel(label)).ToList(),
            views.Select(view => new SyncTaskView(view)).ToList(),
            syncHistory,
            searches,
            counts);

        void Count(DateTimeOffset? deletedAt, string source)
        {
            if (deletedAt is null)
            {
                sources[source]++;
            }
            else
            {
                sourceTombstones[source]++;
            }
        }
    }

    /// <summary>
    /// The next session's open tasks in their order: each goes to This session while its actions fit (its first open steps take
    /// the free places and are focused; a task without open steps takes one), the rest to Queued.
    /// </summary>
    public static IEnumerable<TaskItem> FillSession(IEnumerable<TaskItem> openTasks, int sessionActions)
    {
        int free = Math.Max(0, sessionActions);
        foreach (TaskItem task in openTasks.OrderBy(task => task.Position).ThenBy(task => task.Id.ToString("D"), StringComparer.Ordinal))
        {
            if (free == 0)
            {
                yield return task with { Bin = TaskBin.Queued };
                continue;
            }

            List<TaskStep> steps = (task.Steps ?? []).ToList();
            int openSteps = steps.Count(step => !step.IsDone);
            if (openSteps == 0)
            {
                free--;
                yield return task with { Bin = TaskBin.Session };
                continue;
            }

            int toFocus = Math.Min(free, openSteps);
            free -= toFocus;
            var focused = new List<TaskStep>(steps.Count);
            foreach (TaskStep step in steps)
            {
                bool focus = !step.IsDone && toFocus > 0;
                if (focus)
                {
                    toFocus--;
                }

                focused.Add(step with { Focus = focus });
            }

            yield return task with { Bin = TaskBin.Session, Steps = focused };
        }
    }

    private static TaskFieldStamps StampsAt(DateTimeOffset at)
    {
        return TaskFieldStamps.AllAt(new TaskFieldStamp(at, Guid.Empty));
    }

    private static TaskItem FromNextTask(SyncNextTask item, TaskLabelBook book)
    {
        if (item.DeletedAt is DateTimeOffset deletedAt)
        {
            return Tombstone(item.Id, TaskBin.Queued, item.Position, item.EditedAt, deletedAt, item.LocalTo);
        }

        return new TaskItem(
            item.Id, item.Title, TaskBin.Queued, item.Position, item.EditedAt, item.EditedAt, item.EditedAt,
            LabelId: book.IdForName(item.Category, item.CategoryColor, item.EditedAt),
            Tags: TaskLabels.Clean(item.Labels),
            Steps: TaskSessionMapper.StepsFrom(item.Subtasks, null),
            EstimateMinutes: item.EstimateMinutes,
            Note: item.Note,
            DoneAt: item.DoneAt,
            Devices: DevicesOf(item.LocalTo),
            PlannedFor: item.PlannedFor);
    }

    private static TaskItem FromLaterItem(SyncLaterItem item, TaskLabelBook book)
    {
        TaskBin bin = item.Bin == SyncLaterItem.NextBin ? TaskBin.Next : TaskBin.Later;
        if (item.DeletedAt is DateTimeOffset deletedAt)
        {
            return Tombstone(item.Id, bin, item.Position, item.EditedAt, deletedAt, item.LocalTo);
        }

        return new TaskItem(
            item.Id, item.Title, bin, item.Position, item.EditedAt, item.EditedAt, item.EditedAt,
            LabelId: book.IdForName(item.Category, item.CategoryColor, item.EditedAt),
            Tags: TaskLabels.Clean(item.Labels),
            Steps: TaskSessionMapper.StepsFrom(item.Subtasks, null),
            EstimateMinutes: item.EstimateMinutes,
            Note: item.Note,
            DueOn: item.DueOn,
            DoneAt: item.DoneAt,
            Devices: DevicesOf(item.LocalTo),
            PlannedFor: item.PlannedFor);
    }

    private static TaskItem FromParked(SyncParked item, TaskLabelBook book, TimeZoneInfo zone)
    {
        if (item.DeletedAt is DateTimeOffset deletedAt)
        {
            return Tombstone(item.Id, TaskBin.Sort, item.Position, item.EditedAt, deletedAt, item.LocalTo) with { CreatedAt = item.CreatedAt };
        }

        return new TaskItem(
            item.Id, item.Text, TaskBin.Sort, item.Position, item.CreatedAt, item.EditedAt, item.EditedAt,
            LabelId: book.IdForName(item.Category, item.CategoryColor, item.EditedAt),
            Tags: TaskLabels.Clean(item.Labels),
            Origin: TaskOrigins.Parked(item.CreatedAt, zone),
            DoneAt: item.DoneAt,
            Devices: DevicesOf(item.LocalTo));
    }

    private static TaskItem FromAutoSaved(SyncAutoSavedItem item, TaskLabelBook book)
    {
        if (item.DeletedAt is DateTimeOffset deletedAt)
        {
            return Tombstone(item.Id, TaskBin.Sort, item.Position, item.EditedAt, deletedAt, item.LocalTo) with { CreatedAt = item.SavedAt };
        }

        return new TaskItem(
            item.Id, item.Title, TaskBin.Sort, item.Position, item.SavedAt, item.EditedAt, item.EditedAt,
            LabelId: book.IdForName(item.Category, item.CategoryColor, item.EditedAt),
            Tags: TaskLabels.Clean(item.Labels),
            Origin: TaskOrigins.AutoSaved(item.WrapUpOf),
            Devices: DevicesOf(item.LocalTo),
            AutoSavedFor: item.WrapUpOf);
    }

    private static TaskItem Tombstone(Guid id, TaskBin bin, double position, DateTimeOffset editedAt, DateTimeOffset deletedAt, Guid? localTo)
    {
        return new TaskItem(id, string.Empty, bin, position, editedAt, editedAt, editedAt, Devices: DevicesOf(localTo), DeletedAt: deletedAt);
    }

    private static IReadOnlyList<Guid>? DevicesOf(Guid? localTo)
    {
        return localTo is Guid keeper ? [keeper] : null;
    }

    private static (List<TaskView> Views, Dictionary<Guid, string> Searches, int Unmapped) Views(TaskMigrationInput input, TaskLabelBook book)
    {
        var categoryNames = input.Categories.GroupBy(category => category.Id).ToDictionary(group => group.Key, group => group.First().Name);
        int unmapped = 0;
        TaskViewConfig? current = input.CurrentFilter is null ? null : ConfigOf(input.CurrentFilter);
        var views = TaskView.BuiltIns(UnstampedAt, current).ToList();
        var searches = new Dictionary<Guid, string>();
        double position = views.Count;
        foreach (LegacyTaskFilter preset in input.FilterPresets)
        {
            if (views.Any(view => view.Id == preset.Id))
            {
                continue;
            }

            position++;
            views.Add(new TaskView(preset.Id, preset.Name, null, ConfigOf(preset), position, UnstampedAt));
            if (preset.Search.Trim().Length > 0)
            {
                searches[preset.Id] = preset.Search;
            }
        }

        return (views, searches, unmapped);

        TaskViewConfig ConfigOf(LegacyTaskFilter filter)
        {
            return LegacyTaskFilterMapper.Config(filter, categoryNames, name => book.Named(name)?.Id, ref unmapped);
        }
    }
}
