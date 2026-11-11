using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>What <see cref="OldTaskFieldsReader"/> needs: another device's old fields and this device's task list.</summary>
/// <param name="Incoming">The old fields of a file that has no <see cref="SyncFile.Tasks"/>.</param>
/// <param name="SenderId">The device that wrote the file; its changes are stamped with it.</param>
/// <param name="SenderKind">"pc", "phone" or "watch" (<see cref="SyncDeviceCard.Kind"/>), for the origin of a task it added.</param>
/// <param name="Tasks">This device's task list, tombstones included.</param>
/// <param name="Labels">This device's labels.</param>
/// <param name="History">This device's History.</param>
/// <param name="Self">This device's id (its own local items' stubs never replace its copy).</param>
/// <param name="Now">The trusted time.</param>
/// <param name="Zone">The local time zone (origin lines).</param>
/// <param name="SessionActions">The session cap: a task added to the next session's list joins This session only while it fits.</param>
public sealed record OldTaskReadInput(
    OldTaskFields Incoming,
    Guid SenderId,
    string SenderKind,
    IReadOnlyList<SyncTaskItem> Tasks,
    IReadOnlyList<SyncTaskLabel> Labels,
    IReadOnlyList<SyncTaskLogItem> History,
    Guid Self,
    DateTimeOffset Now,
    TimeZoneInfo Zone,
    int SessionActions = 3);
