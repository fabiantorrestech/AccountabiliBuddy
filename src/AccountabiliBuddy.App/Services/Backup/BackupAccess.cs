using System.Text.Json;
using AccountabiliBuddy.App.Services.Goals;
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
    /// <param name="setDailyGoal">
    /// The goals' one daily-goal rule (Q375; milestone review S3: <see cref="GoalsService.SetDailyGoalAsync"/>): a lower daily goal from a
    /// backup waits the looser-change delay and is refused during a session outside ProductaBuddy mode, like the weekly target; returns
    /// the note for the restore summary. Null: written directly (only where no goals service exists).
    /// </param>
    public static SyncedSettingsAccess Synced(
        SleepSettingsService sleep, DayBarSettingsService dayBar, GoalService goal, GuidanceModeService guidance,
        Func<int, Task<string?>>? setDailyGoal = null)
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
                if (value.Deserialize<GoalSettings>(SyncJson.Options) is not { } settings)
                {
                    return null;
                }

                if (setDailyGoal is not null)
                {
                    return await setDailyGoal(settings.DailyMinutes) is string note ? $"Daily goal from the backup: {note}" : null;
                }

                await goal.SetAsync(settings.DailyMinutes);
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

    /// <summary>
    /// This PC's goals with the task lists (round 2 R4 review; the way task lists travel): readable inside the backup so it restores on
    /// another PC; notes and reasons only in a file with a password; a restore goes through the goals store's guards and is re-sealed
    /// with this PC's key there. ADR 79 (GS3a): a phone's goals backup restores here the same way (one shape); Deep work's weekly target
    /// travels with the lists and comes back through its looser-change rule (<paramref name="goals"/>); a restore during a focus session
    /// the user started is refused (<paramref name="enforcing"/>), and a restored goal syncs out like an edit made here.
    /// </summary>
    public static GoalsAccess Goals(GoalStore store, GoalsService? goals = null, Func<bool>? enforcing = null)
    {
        return new GoalsAccess(
            Export: includeSecrets =>
            {
                if (!store.IsReady)
                {
                    return null;
                }

                GoalLists lists = store.Lists;
                var export = new GoalsExport(
                    lists.Areas,
                    includeSecrets ? lists.Goals : lists.Goals.Select(goal => goal with { Notes = null }).ToList(),
                    lists.Entries,
                    includeSecrets ? lists.Reminders : lists.Reminders.Select(reminder => reminder with { Reason = null }).ToList(),
                    includeSecrets ? lists.Reviews : lists.Reviews.Select(review => review with { Notes = null }).ToList(),
                    IncludesNotes: includeSecrets,
                    DeepWork: new GoalsBackupDeepWork(store.DeepWork.EffectiveWeekly, store.DeepWork.TrackTimeOnly));
                return JsonSerializer.SerializeToElement(export, SyncJson.Options);
            },
            Import: async (value, restore) =>
            {
                GoalsExport? export;
                try
                {
                    export = value.Deserialize<GoalsExport>(SyncJson.Options);
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException)
                {
                    export = null;
                }

                if (export is null)
                {
                    return "The goals in this backup couldn't be read; this PC's goals were kept.";
                }

                if (enforcing?.Invoke() == true)
                {
                    return "Goals weren't restored during a focus session you started; this PC's goals were kept.";
                }

                var lists = new GoalLists(export.Areas ?? [], export.Goals ?? [], export.Entries ?? [], export.Reminders ?? [], export.Reviews ?? []);
                string note = await store.ImportAsync(lists, replace: restore, includesNotes: export.IncludesNotes);
                if (goals is null || export.DeepWork is not { } deepWork || !store.IsReady)
                {
                    return note;
                }

                // Deep work's weekly target: a raise at once, a lower one or none waits (never during a session), as a change made here.
                System.Text.Json.Nodes.JsonNode part = JsonSerializer.SerializeToNode(
                    new DeepWorkGoalSettings(deepWork.WeeklyMinutes, deepWork.TrackTimeOnly ?? false), GoalStore.DocumentJson)!;
                if (deepWork.TrackTimeOnly is null)
                {
                    part.AsObject().Remove("trackTimeOnly"); // a phone-shaped part: no word on it, so this PC's stays
                }

                string? weeklyNote = await goals.RestoreDeepWorkAsync(JsonSerializer.SerializeToElement(part));
                return weeklyNote is null ? note : note + " " + weeklyNote;
            });
    }

    /// <param name="store">ADR 76: the one task list; while it is the source, a backup holds it too and a restore goes into it.</param>
    /// <param name="account">The account's Tasks settings: in the backup with the task list, restored through their service.</param>
    /// <param name="notes">The session notes (ADR 87): in the backup with the task lists, merged back like another device's notes.</param>
    public static TaskListsAccess Tasks(
        SessionTasksService tasks, ComebackStore comebacks, LaterStore later, TimeProvider time, TaskStore? store = null, TaskAccountSettingsService? account = null,
        SessionNotesStore? notes = null)
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
                    TaskLog: store is { IsActive: true } ? store.Lists.Log : null,
                    AccountSettings: store is { IsActive: true, Lists.Account: { IsEmpty: false } held } ? held : null,
                    SessionNotes: notes is null ? null : notes.ForSync()),
                SyncJson.Options),
            Import: async (value, restore) =>
            {
                TaskListsExport? lists = value.Deserialize<TaskListsExport>(SyncJson.Options);
                if (lists is null)
                {
                    return "The task lists couldn't be read.";
                }

                if (notes is not null && lists.SessionNotes is { Count: > 0 } backupNotes)
                {
                    // ADR 87: merged item by item like another device's file, so what was edited or deleted since the backup is kept.
                    await notes.MergeAsync(backupNotes);
                }

                if (store is { IsActive: true } && lists.Tasks is { } backupTasks)
                {
                    // ADR 76: the backup's task list goes into this one (the old documents stay as the migration left them).
                    int live = await store.ImportAsync(new TaskLists(backupTasks, lists.Labels ?? [], lists.Views ?? [], lists.TaskLog ?? []), restore);
                    if (account is not null && lists.AccountSettings is { } accountSettings)
                    {
                        await account.ImportAsync(accountSettings, restore);
                    }

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
