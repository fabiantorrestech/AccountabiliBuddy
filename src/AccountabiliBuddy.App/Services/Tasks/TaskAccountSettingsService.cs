using System.Text.Json;
using AccountabiliBuddy.App.Services.Backup;
using AccountabiliBuddy.Core.Sync;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// The Tasks settings that belong to the user's account, not to this PC (owner, 2026-11-11): the view the Shelf opens with and ADR 78's
/// experimental picked calendar for tasks with a due date. Kept with the task list (<see cref="TaskStore"/>, document "tasks.account"),
/// so they sync with it (the sync file's <see cref="SyncFile.TaskAccountSettings"/>, governed by "Sync task lists"), go in task-list
/// backups and merge field by field, newest choice first (<see cref="TaskAccountSettingsMerge"/>). The same in both modes.
/// <para>
/// For the setup assistant and Settings (M7): <see cref="AccountCopy"/>, <see cref="FromBackup"/> and <see cref="Fresh"/> are the
/// three copies to offer, each with its time; <see cref="ApplyAsync"/> makes one of them the account's (a choice made now, so it
/// reaches every device) and <see cref="ResetAsync"/> starts fresh.
/// </para>
/// Use from the UI thread only.
/// </summary>
public sealed class TaskAccountSettingsService
{
    private readonly TaskStore _store;
    private SyncTaskAccountSettings _seen;

    public TaskAccountSettingsService(TaskStore store)
    {
        _store = store;
        _seen = Current;
        store.Changed += (_, _) =>
        {
            SyncTaskAccountSettings current = Current;
            if (current != _seen)
            {
                _seen = current;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>The account's copy as this PC holds it (empty while the task list isn't in use).</summary>
    public SyncTaskAccountSettings Current => _store.IsActive ? _store.Lists.Account ?? SyncTaskAccountSettings.Empty : SyncTaskAccountSettings.Empty;

    /// <summary>The view the Shelf opens with; null for the built-in Default.</summary>
    public Guid? DefaultViewId => Current.DefaultViewId;

    /// <summary>The calendar tasks with a due date go to (ADR 78's experimental option); null when off.</summary>
    public string? PickedCalendarId => Current.PickedCalendarId;

    /// <summary>The device that chose the picked calendar (it had calendar.events then); null when it was never chosen.</summary>
    public Guid? PickedCalendarBy => Current.PickedCalendarStamp?.By;

    /// <summary>
    /// The pick was really chosen on a device (not only taken over from one device's old setting at the upgrade, which says nothing
    /// about that device's consent).
    /// </summary>
    public bool PickedCalendarChosen => TaskAccountSettingsEdits.IsChosen(Current.PickedCalendarStamp);

    /// <summary>A setting changed, here or on another device.</summary>
    public event EventHandler? Changed;

    /// <summary>The view the Shelf opens with, for every device (null: the built-in Default).</summary>
    public Task<bool> SetDefaultViewAsync(Guid? viewId)
    {
        return _store.ChangeAsync(this, (lists, self, now) => lists with { Account = TaskAccountSettingsEdits.WithDefaultView(lists.Account, viewId, now, self) });
    }

    /// <summary>
    /// The calendar tasks with a due date go to, for every device (null: off). Each device writes there only with its own consent
    /// (<see cref="TaskCalendarExportService"/>).
    /// </summary>
    public Task<bool> SetPickedCalendarAsync(string? calendarId)
    {
        return _store.ChangeAsync(this, (lists, self, now) => lists with { Account = TaskAccountSettingsEdits.WithPickedCalendar(lists.Account, calendarId, now, self) });
    }

    /// <summary>
    /// The values this PC kept on its own before the settings were shared, taken over once (only where the account has none yet, and
    /// older than any real choice). The change is in memory when this returns; the task is the save.
    /// </summary>
    /// <returns>Whether anything was taken over.</returns>
    public Task<bool> AdoptLegacyAsync(Guid? defaultViewId, string? pickedCalendarId)
    {
        if (defaultViewId is null && string.IsNullOrWhiteSpace(pickedCalendarId))
        {
            return Task.FromResult(false);
        }

        return _store.ChangeAsync(this, (lists, self, _) => lists with { Account = TaskAccountSettingsEdits.Adopted(lists.Account, defaultViewId, pickedCalendarId, self) });
    }

    /// <summary>
    /// A task-list backup's copy (ADR 71): Restore makes each setting the backup chose this PC's choice now (it wins over later edits);
    /// Merge keeps the newest choice of each.
    /// </summary>
    public Task<bool> ImportAsync(SyncTaskAccountSettings backup, bool restore)
    {
        SyncTaskAccountSettings? clean = TaskAccountSettingsMerge.Clean(backup);
        if (clean is null || clean.IsEmpty)
        {
            return Task.FromResult(false);
        }

        return _store.ChangeAsync(this, (lists, self, now) => lists with
        {
            Account = restore ? TaskAccountSettingsEdits.Restored(lists.Account, clean, now, self) : TaskAccountSettingsMerge.Merge(lists.Account, clean, now),
        });
    }

    /// <summary>The account's copy as this PC holds it, with when it was last chosen.</summary>
    public TaskAccountSettingsChoice AccountCopy()
    {
        SyncTaskAccountSettings current = Current;
        return new TaskAccountSettingsChoice(TaskAccountSettingsOrigin.Account, current.DefaultViewId, current.PickedCalendarId, current.EditedAt, null);
    }

    /// <summary>A fresh start: the built-in Default view, no picked calendar.</summary>
    public static TaskAccountSettingsChoice Fresh { get; } = new(TaskAccountSettingsOrigin.Fresh, null, null, null, null);

    /// <summary>
    /// A backup file's copy (<see cref="BackupService.Read"/>), with when it was chosen and when the backup was made: from its task lists,
    /// or, in a backup made before the settings were shared, the default view in its "This PC's settings". Null when it holds neither.
    /// </summary>
    public static TaskAccountSettingsChoice? FromBackup(BackupContent content)
    {
        SyncTaskAccountSettings? shared = null;
        if (content.Tasks is JsonElement tasks)
        {
            try
            {
                shared = TaskAccountSettingsMerge.Clean(tasks.Deserialize<TaskListsExport>(SyncJson.Options)?.AccountSettings);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                shared = null;
            }
        }

        if (shared is { IsEmpty: false })
        {
            return new TaskAccountSettingsChoice(TaskAccountSettingsOrigin.Backup, shared.DefaultViewId, shared.PickedCalendarId, shared.EditedAt, content.CreatedAt);
        }

        Guid? legacyView = LegacyDefaultView(content.Device) ?? LegacyDefaultView(content.Settings);
        return legacyView is null
            ? null
            : new TaskAccountSettingsChoice(TaskAccountSettingsOrigin.Backup, legacyView, null, null, content.CreatedAt);
    }

    /// <summary>Makes <paramref name="choice"/> the account's settings, as a choice made now on this PC (a setting it already has stays as it is).</summary>
    /// <returns>Whether anything changed.</returns>
    public Task<bool> ApplyAsync(TaskAccountSettingsChoice choice)
    {
        return _store.ChangeAsync(this, (lists, self, now) => lists with
        {
            Account = TaskAccountSettingsEdits.WithBoth(lists.Account, choice.DefaultViewId, choice.PickedCalendarId, now, self),
        });
    }

    /// <summary>Starts fresh for the whole account: the built-in Default view, no picked calendar.</summary>
    public Task<bool> ResetAsync()
    {
        return ApplyAsync(Fresh);
    }

    private static Guid? LegacyDefaultView(IReadOnlyDictionary<string, JsonElement>? documents)
    {
        if (documents is null || !documents.TryGetValue(TaskSettingsService.DocumentKey, out JsonElement value))
        {
            return null;
        }

        try
        {
            return value.Deserialize<TaskSettings>(SyncJson.Options)?.DefaultViewId is Guid id && id != Guid.Empty ? id : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}
