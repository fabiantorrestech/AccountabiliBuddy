using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The one-time move of the old task lists into the one shared list (ADR 76). Pure: it reads the old stores' contents and the
/// time, and gives the new lists; the App backs up the database first, runs it once behind a marker document and keeps the old
/// documents for a release as the way back.
/// <list type="bullet">
/// <item>Categories become labels (id from the name, same name and colour); a category only an item names becomes one too.</item>
/// <item>Every open task of the old lists goes to To sort, in the order of the lists (next session, Later, come-backs, auto-saved,
/// each in its own order), so the owner sorts them once: next-session and Later tasks carry "From the earlier version", come-backs
/// "Parked" and when, auto-saved "Auto-saved" and its day; auto-saved past its 30 days is left out. Nothing goes to This session,
/// Queued, Next or Later.</item>
/// <item>Finished: tasks done today in the next session's list go to Finished; every other ticked task goes to History.</item>
/// <item>The old filter becomes the Default view's settings, the saved filters become views (their search words stay local).</item>
/// <item>Numbers go in creation order; ADR 72 labels become tags; a local item's keeper becomes its one device.</item>
/// </list>
/// Tombstones and local stubs stay tombstones. Ids are kept (labels and built-in views have fixed ids), nothing is random and the
/// only times taken from <see cref="TaskMigrationInput.Now"/> are every live task's bin stamp and edited time (the move into To sort is an edit; it
/// keeps OldTaskFieldsWriter's deletion markers for 30 days, so an older device sees each task once), so the same input always gives the
/// same result.
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
            winners.Add(ordered[0] with { Task = WithLoserConflicts(ordered[0].Task, ordered.Skip(1)) });
            duplicates += ordered.Skip(1).Count(draft => draft.Task.IsLive);
        }

        var tasks = new List<TaskItem>();
        var history = new List<TaskHistoryEntry>();
        var openDrafts = new List<Draft>();
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

            openDrafts.Add(draft);
        }

        // Every open task lands in To sort: the lists one after the other, each in its own order, then numbered 1, 2, 3 ...
        double sortPosition = 0;
        foreach (Draft draft in openDrafts
            .OrderBy(draft => draft.SourceOrder).ThenBy(draft => draft.Task.Position).ThenBy(draft => draft.Task.Id.ToString("D"), StringComparer.Ordinal))
        {
            sortPosition++;
            TaskItem open = draft.Task with { Bin = TaskBin.Sort, Position = sortPosition };
            tasks.Add(draft.Source is NextTasksSource or LaterSource ? open with { Origin = TaskOrigins.EarlierVersion } : open);
        }

        // Numbers in creation order over every live task and History snapshot.
        int nextNumber = Math.Max(1, input.FirstNumber);
        var numbered = new Dictionary<Guid, TaskNumber>();
        foreach (TaskItem task in tasks.Where(task => task.IsLive).Concat(history.Select(entry => entry.Task))
            .OrderBy(task => task.CreatedAt.UtcTicks).ThenBy(task => task.Id.ToString("D"), StringComparer.Ordinal))
        {
            numbered[task.Id] = new TaskNumber(nextNumber++, Temporary: !input.IsNumberingDevice);
        }

        // A live task changed bins here, so the move is an edit: its edited time (not its other parts' stamps) is at least the migration time,
        // never before the bin stamp, which is how OldTaskFieldsWriter's deletion markers stay older than the task's own copy.
        List<SyncTaskItem> syncTasks = tasks
            .Select(task => task.IsLive
                ? new SyncTaskItem(task with { Number = numbered[task.Id], EditedAt = task.EditedAt > input.Now ? task.EditedAt : input.Now }, StampsAt(task.EditedAt, input.Now))
                : new SyncTaskItem(task, TaskFieldStamps.None))
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

    // The live copy that lost keeps its title and note on the winner as "Changed on both devices" when they differ.
    private static TaskItem WithLoserConflicts(TaskItem winner, IEnumerable<Draft> losers)
    {
        if (!winner.IsLive)
        {
            return winner;
        }

        var conflicts = new List<TaskConflict>(winner.Conflicts ?? []);
        foreach (TaskItem loser in losers.Select(draft => draft.Task).Where(task => task.IsLive))
        {
            if (!string.Equals(loser.Title, winner.Title, StringComparison.Ordinal))
            {
                conflicts.Add(new TaskConflict(TaskConflict.TitleField, loser.Title, Guid.Empty, loser.EditedAt));
            }

            if (!string.Equals(loser.Note ?? string.Empty, winner.Note ?? string.Empty, StringComparison.Ordinal))
            {
                conflicts.Add(new TaskConflict(TaskConflict.NoteField, loser.Note, Guid.Empty, loser.EditedAt));
            }
        }

        return conflicts.Count == (winner.Conflicts?.Count ?? 0) ? winner : winner with { Conflicts = conflicts };
    }

    // Every part carries the task's own edit time, except the bin: every live task changed bins at the migration (into To sort, or Done), so
    // its bin is stamped at migration time. OldTaskFieldsWriter then leaves deletion markers in the old lists for 30 days, and an older
    // device sees each task once (in its list for this bin) instead of twice.
    private static TaskFieldStamps StampsAt(DateTimeOffset at, DateTimeOffset migratedAt)
    {
        return TaskFieldStamps.AllAt(new TaskFieldStamp(at, Guid.Empty)).With(TaskField.Bin, new TaskFieldStamp(migratedAt, Guid.Empty));
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
