using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

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

    public static TaskListsAccess Tasks(SessionTasksService tasks, ComebackStore comebacks, LaterStore later, TimeProvider time)
    {
        return new TaskListsAccess(
            Export: () => JsonSerializer.SerializeToElement(
                new TaskListsExport(
                    tasks.NextTasksForSync(),
                    comebacks.ForSync(),
                    later.ForSync(),
                    tasks.Categories.Select(category => new SyncCategory(category.Name, category.Color, category.ColorEditedAt)).ToList()),
                SyncJson.Options),
            Import: async (value, restore) =>
            {
                TaskListsExport? lists = value.Deserialize<TaskListsExport>(SyncJson.Options);
                if (lists is null)
                {
                    return "The task lists couldn't be read.";
                }

                // Restore: the backup's live items are stamped now, so they win over later edits and deletions; its deletions don't.
                DateTimeOffset now = time.GetUtcNow();
                IReadOnlyList<SyncNextTask> next = (lists.NextTasks ?? [])
                    .Where(item => !restore || item.DeletedAt is null).Select(item => restore ? item with { EditedAt = now } : item).ToList();
                IReadOnlyList<SyncParked> parked = (lists.Comebacks ?? [])
                    .Where(item => !restore || item.DeletedAt is null).Select(item => restore ? item with { EditedAt = now } : item).ToList();
                IReadOnlyList<SyncLaterItem> laterItems = (lists.Later ?? [])
                    .Where(item => !restore || item.DeletedAt is null).Select(item => restore ? item with { EditedAt = now } : item).ToList();
                await tasks.MergeNextTasksAsync(next);
                await comebacks.MergeAsync(parked);
                await later.MergeAsync(laterItems);
                foreach (SyncCategory category in lists.Categories ?? [])
                {
                    await tasks.AdoptCategoryAsync(category.Name, category.Color);
                }

                int count = next.Count(item => item.DeletedAt is null) + parked.Count(item => item.DeletedAt is null) + laterItems.Count(item => item.DeletedAt is null);
                return restore
                    ? $"Task lists restored ({count} items from the backup)."
                    : $"Task lists merged ({count} items in the backup; the newest edit of each kept).";
            });
    }
}
