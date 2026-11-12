using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The Tasks settings that belong to the user's account, shared by all their devices, not to one device (owner, 2026-11-11): the view
/// the Shelf opens with, ADR 78's experimental picked calendar for tasks with a due date and the devices task reminders ring on. The sync file's optional
/// <see cref="SyncFile.TaskAccountSettings"/> part, governed by "Sync task lists" like ADR 76's other parts.
/// <para>
/// Each field has its own stamp and merges on its own, newest first (<see cref="TaskAccountSettingsMerge"/>). A field without a stamp
/// was never chosen: its default holds and any chosen value wins over it. A stamp with a null value is a choice too (the built-in
/// Default view; no picked calendar). Ids only, no text; the calendar id can be the account's address, so like everything in the
/// file it travels only inside the sealed files and is never logged.
/// </para>
/// </summary>
/// <param name="DefaultViewId">The saved view the Shelf opens with; null for the built-in Default.</param>
/// <param name="DefaultViewStamp">When and on which device <paramref name="DefaultViewId"/> was chosen; null when never.</param>
/// <param name="PickedCalendarId">The Google calendar (of the export account) that tasks with a due date go to; null when off.</param>
/// <param name="PickedCalendarStamp">When and on which device <paramref name="PickedCalendarId"/> was chosen; null when never.</param>
/// <param name="RingOn">
/// "Task reminders ring on" (Settings › Alerts &amp; on-screen): the device ids that ring for task reminders; null for every device
/// (<see cref="Tasks.TaskRingDevices"/>: a PC always rings for its own reminders; phones honour it with the label's own list).
/// </param>
/// <param name="RingOnStamp">When and on which device <paramref name="RingOn"/> was chosen; null when never.</param>
public sealed record SyncTaskAccountSettings(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? DefaultViewId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? DefaultViewStamp = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PickedCalendarId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? PickedCalendarStamp = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? RingOn = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? RingOnStamp = null)
{
    /// <summary>The longest calendar id taken from a file (Google's ids are far shorter).</summary>
    public const int MaxCalendarIdLength = 1024;

    /// <summary>
    /// The stamp time of a value a device kept on its own before these settings were shared (taken over once at the upgrade): older
    /// than any real choice, so a choice made on any device wins over it.
    /// </summary>
    public static readonly DateTimeOffset AdoptedAt = DateTimeOffset.UnixEpoch;

    public static SyncTaskAccountSettings Empty { get; } = new();

    /// <summary>Nothing was ever chosen.</summary>
    [JsonIgnore]
    public bool IsEmpty => DefaultViewStamp is null && PickedCalendarStamp is null && RingOnStamp is null;

    /// <summary>When the newest field was chosen; null when nothing was chosen since these settings were shared (only adopted values).</summary>
    [JsonIgnore]
    public DateTimeOffset? EditedAt
    {
        get
        {
            DateTimeOffset?[] times = [DefaultViewStamp?.At, PickedCalendarStamp?.At, RingOnStamp?.At];
            DateTimeOffset? newest = null;
            foreach (DateTimeOffset? at in times)
            {
                if (at is DateTimeOffset time && time > AdoptedAt && (newest is null || time > newest))
                {
                    newest = time;
                }
            }

            return newest;
        }
    }
}
