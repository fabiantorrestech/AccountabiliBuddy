using System.Globalization;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// The startup step that makes the one task list (ADR 76) the source, once. With the marker stored it only reads the task list;
/// otherwise it copies the whole database ("tasks-model" backup), runs <see cref="TaskMigration"/> on what the old stores hold
/// (tombstones and local items included) and switches over only when every item is accounted for. The old documents stay as they
/// are, for one release, as the way back. Anything that goes wrong leaves no marker and the app on its old stores. The log
/// ("tasks-model.log" in the data folder) holds counts only, never text.
/// </summary>
public static class TaskModelMigration
{
    public const string LogFileName = "tasks-model.log";

    /// <returns>Where the task lists come from from now on. <see cref="TaskModelState.OldLists"/> here always means the migration failed (the caller shows a notice).</returns>
    public static TaskModelState RunBlocking(
        LocalDatabase database, TaskStore store, SessionTasksService sessionTasks, LaterStore later, ComebackStore comebacks,
        AutoSavedTaskStore autoSaved, TaskFilterPresetStore presets, TaskViewStore view, TimeProvider time, string dataDirectory,
        EarlierTasksNoticeStore? earlierNotice = null)
    {
        string logPath = Path.Combine(dataDirectory, LogFileName);
        bool migrated;
        try
        {
            migrated = store.HasMarkerBlocking();
        }
        catch (Exception ex)
        {
            // The marker can't be read, so whether the migration ran isn't known: the old lists may have stopped at it, so they are
            // never taken as the source on a guess.
            Log(logPath, time, "migration marker could not be read (" + ex.GetType().Name + "); task lists closed until rebuilt");
            return TaskModelState.Unreadable;
        }

        try
        {
            if (migrated)
            {
                if (store.LoadBlocking())
                {
                    return TaskModelState.TaskList;
                }

                // The old documents stopped at the migration: using them would bring back deleted tasks and lose later edits.
                Log(logPath, time, "task list could not be read; task lists closed until rebuilt (the old lists are not used)");
                return TaskModelState.Unreadable;
            }

            // One backup per attempt version: a migration that keeps failing must not copy the whole database on every start.
            if (store.HasAttemptedBlocking())
            {
                Log(logPath, time, "backup of this version's attempt already written");
            }
            else
            {
                string backup = Task.Run(() => database.BackupAsync("tasks-model")).GetAwaiter().GetResult();
                store.RecordAttemptBlocking();
                Log(logPath, time, "backup written: " + Path.GetFileName(backup));
            }

            DateTimeOffset now = time.GetUtcNow();
            Guid? numbering = store.NumberingDevice();
            Guid self = store.SelfId() ?? Guid.Empty;
            TaskViewPreferences? current = view.Load();
            var input = new TaskMigrationInput(
                sessionTasks.Categories,
                sessionTasks.StoredNextTasks(),
                later.StoredItems,
                comebacks.StoredItems,
                autoSaved.StoredItems,
                presets.Load().Select(preset => Legacy(preset.Id, preset.Name, preset.View, preset.Search)).ToList(),
                current is null ? null : Legacy(Guid.Empty, string.Empty, current, string.Empty),
                now,
                TimeZoneInfo.Local,
                IsNumberingDevice: numbering == self,
                FirstNumber: 1,
                SessionActions: store.SessionActions());
            TaskMigrationResult result = TaskMigration.Run(input);
            Log(logPath, time, Describe(result.Counts));
            if (!result.Counts.IsBalanced)
            {
                Log(logPath, time, "counts don't balance; staying on the old lists (no marker written)");
                return TaskModelState.OldLists;
            }

            store.ActivateBlocking(result, CategoryIds(sessionTasks.Categories, result.Labels));
            // MIG1: the open tasks of the earlier version all wait in To sort; the card tells the owner how many (a number, never text).
            earlierNotice?.RecordBlocking(result.Counts.Bins.GetValueOrDefault(TaskBin.Sort));
            Log(logPath, time, $"switched over: {store.Lists.Tasks.Count(item => item.Task.IsLive)} live tasks, {store.Lists.Log.Count} History entries");
            return TaskModelState.TaskList;
        }
        catch (Exception ex)
        {
            // Never the message: it could quote a stored value. The type is enough to find it.
            if (migrated)
            {
                Log(logPath, time, "task list could not be read (" + ex.GetType().Name + "); task lists closed until rebuilt");
                return TaskModelState.Unreadable;
            }

            Log(logPath, time, "migration failed (" + ex.GetType().Name + "); staying on the old lists (no marker written)");
            return TaskModelState.OldLists;
        }
    }

    /// <summary>
    /// The user's "Rebuild" for an unreadable task list (<see cref="TaskModelState.Unreadable"/>): the whole database is copied first
    /// ("tasks-unreadable" backup, which keeps the unreadable documents), then the task list's documents and the marker go, so the
    /// next start migrates again from the old lists as they were at the migration (the same content as the "tasks-model" backup).
    /// Changes made to tasks after the migration are not in them.
    /// </summary>
    public static async Task SetAsideForRebuildAsync(LocalDatabase database, SqliteJsonDocumentStore documents, TimeProvider time, string dataDirectory)
    {
        string logPath = Path.Combine(dataDirectory, LogFileName);
        string backup = await database.BackupAsync("tasks-unreadable");
        foreach (string key in new[] { TaskStore.MarkerKey, TaskStore.ItemsKey, TaskStore.LabelsKey, TaskStore.ViewsKey, TaskStore.LogKey, TaskStore.ViewSearchesKey, TaskStore.CategoryIdsKey })
        {
            await documents.DeleteAsync(key);
        }

        Log(logPath, time, "rebuild chosen: unreadable task list set aside (" + Path.GetFileName(backup) + "); the next start migrates again");
    }

    // Each category keeps its id on this PC: running checklists and saved filters name it.
    private static Dictionary<Guid, Guid> CategoryIds(IReadOnlyList<TaskCategory> categories, IReadOnlyList<SyncTaskLabel> labels)
    {
        var ids = new Dictionary<Guid, Guid>();
        foreach (TaskCategory category in categories)
        {
            TaskLabel? label = labels.Select(item => item.Label).FirstOrDefault(candidate => candidate.IsLive && TaskLabel.SameName(candidate.Name, category.Name));
            if (label is not null && label.Id != category.Id && !ids.ContainsKey(label.Id))
            {
                ids[label.Id] = category.Id;
            }
        }

        return ids;
    }

    private static LegacyTaskFilter Legacy(Guid id, string name, TaskViewPreferences preferences, string search)
    {
        return new LegacyTaskFilter(
            id, name, preferences.Statuses ?? [], preferences.CategoryIds ?? [], preferences.NoCategory,
            preferences.Due ?? string.Empty, preferences.Sort ?? string.Empty, search ?? string.Empty);
    }

    private static string Describe(TaskMigrationCounts counts)
    {
        static string Join<TKey>(IReadOnlyDictionary<TKey, int> values)
            where TKey : notnull
        {
            return string.Join(" ", values.Select(pair => $"{pair.Key}={pair.Value}"));
        }

        return $"read: {Join(counts.Sources)}; tombstones read: {Join(counts.SourceTombstones)}; bins: {Join(counts.Bins)}; "
            + $"history={counts.History} expired={counts.Expired} duplicates={counts.Duplicates} tombstones={counts.Tombstones} "
            + $"labels={counts.Labels} labelsFromItems={counts.LabelsFromItems} views={counts.Views} unmappedStatuses={counts.UnmappedStatuses} "
            + $"balanced={counts.IsBalanced}";
    }

    private static void Log(string path, TimeProvider time, string line)
    {
        try
        {
            File.AppendAllText(path, time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture) + "  " + line + Environment.NewLine);
        }
        catch (IOException)
        {
            // The log is a help, never a reason to stop.
        }
        catch (UnauthorizedAccessException)
        {
        }

        System.Diagnostics.Debug.WriteLine("tasks-model: " + line);
    }
}
