using System.Text.Json;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// Builds what <see cref="BackupService"/> needs to back up and restore the synced settings and the task lists (ADR 71) from the
/// services that own them, so a restore goes through the same paths as an edit made by hand.
/// </summary>
public static class BackupAccess
{
    public static SyncedSettingsAccess Synced(
        SleepSettingsService sleep, DayBarSettingsService dayBar, GoalService goal, GuidanceModeService guidance)
    {
        return new SyncedSettingsAccess(
            Sleep: () => JsonSerializer.SerializeToElement(sleep.Current, SyncJson.Options),
            RestoreSleep: async value => value.Deserialize<SleepSettings>(SyncJson.Options) is { } settings ? await sleep.SaveAsync(settings) : null,
            DayBar: () => JsonSerializer.SerializeToElement(dayBar.Current, SyncJson.Options),
            RestoreDayBar: async value =>
            {
                if (value.Deserialize<DayBarSettings>(SyncJson.Options) is { } settings)
                {
                    await dayBar.SaveAsync(settings);
                }

                return null;
            },
            Goal: () => JsonSerializer.SerializeToElement(goal.Current, SyncJson.Options),
            RestoreGoal: async value =>
            {
                if (value.Deserialize<GoalSettings>(SyncJson.Options) is { } settings)
                {
                    await goal.SetAsync(settings.DailyMinutes);
                }

                return null;
            },
            Mode: () => JsonSerializer.SerializeToElement(guidance.State.Mode == GuidanceMode.Companion ? "companion" : "guarding", SyncJson.Options),
            RestoreMode: async value =>
            {
                // The mode follows its own rules (ADR 68): AccountabiliBuddy at once; ProductaBuddy after the looser-change wait,
                // refused during a session (then it says so).
                if (!Enum.TryParse(value.GetString(), ignoreCase: true, out GuidanceMode mode) || mode == guidance.State.Mode)
                {
                    return null;
                }

                if (mode == GuidanceMode.Guarding)
                {
                    await guidance.ChooseGuardingAsync();
                    return null;
                }

                return await guidance.RequestCompanionAsync()
                    ? $"{AppBrand.CompanionName} takes effect after the usual wait."
                    : $"{AppBrand.CompanionName} wasn't restored: a focus session is running. Choose it in Settings afterwards.";
            });
    }

    /// <param name="store">ADR 76: the one task list; while it is the source, a backup holds it too and a restore goes into it.</param>
    public static TaskListsAccess Tasks(SessionTasksService tasks, ComebackStore comebacks, LaterStore later, TimeProvider time, TaskStore? store = null)
    {
        return new TaskListsAccess(
            Export: () => JsonSerializer.SerializeToElement(
                new TaskListsExport(
                    tasks.NextTasksForBackup(),
                    comebacks.ForBackup(),
                    later.ForBackup(),
                    tasks.Categories.Select(category => new SyncCategory(category.Name, category.Color, category.ColorEditedAt)).ToList(),
                    Tasks: store is { IsActive: true } ? store.Lists.Tasks : null,
                    Labels: store is { IsActive: true } ? store.Lists.Labels : null,
                    Views: store is { IsActive: true } ? store.Lists.Views : null,
                    TaskLog: store is { IsActive: true } ? store.Lists.Log : null),
                SyncJson.Options),
            Import: async (value, restore) =>
            {
                TaskListsExport? lists = value.Deserialize<TaskListsExport>(SyncJson.Options);
                if (lists is null)
                {
                    return "The task lists couldn't be read.";
                }

                if (store is { IsActive: true } && lists.Tasks is { } backupTasks)
                {
                    // ADR 76: the backup's task list goes into this one (the old documents stay as the migration left them).
                    int live = await store.ImportAsync(new TaskLists(backupTasks, lists.Labels ?? [], lists.Views ?? [], lists.TaskLog ?? []), restore);
                    return restore
                        ? $"Task lists restored ({live} tasks from the backup)."
                        : $"Task lists merged ({live} tasks in the backup; the newest edit of each kept).";
                }

                // Restore: the backup's live items are stamped now, so they win over later edits and deletions; its deletions don't.
                DateTimeOffset now = time.GetUtcNow();
                IReadOnlyList<SyncNextTask> next = (lists.NextTasks ?? [])
                    .Where(item => !restore || item.DeletedAt is null).Select(item => restore ? item with { EditedAt = now } : item).ToList();
                IReadOnlyList<SyncParked> parked = (lists.Comebacks ?? [])
                    .Where(item => !restore || item.DeletedAt is null).Select(item => restore ? item with { EditedAt = now } : item).ToList();
                IReadOnlyList<SyncLaterItem> laterItems = (lists.Later ?? [])
                    .Where(item => !restore || item.DeletedAt is null).Select(item => restore ? item with { EditedAt = now } : item).ToList();
                if (store is { IsActive: true })
                {
                    // An older backup (no task list in it) into the task list: its items as edits made here.
                    await store.ImportOldAsync(new OldTaskFields(next, laterItems, parked, [], lists.Categories ?? [], []));
                }
                else
                {
                    await tasks.MergeNextTasksAsync(next);
                    await comebacks.MergeAsync(parked);
                    await later.MergeAsync(laterItems);
                    foreach (SyncCategory category in lists.Categories ?? [])
                    {
                        await tasks.AdoptCategoryAsync(category.Name, category.Color);
                    }
                }

                int count = next.Count(item => item.DeletedAt is null) + parked.Count(item => item.DeletedAt is null) + laterItems.Count(item => item.DeletedAt is null);
                return restore
                    ? $"Task lists restored ({count} items from the backup)."
                    : $"Task lists merged ({count} items in the backup; the newest edit of each kept).";
            });
    }
}
