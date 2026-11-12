using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Merges two copies of <see cref="SyncTaskAccountSettings"/> field by field: a stamped field beats an unstamped one; between two
/// stamps the later time wins, then the device id, then the value, so every device picks the same whatever order the files come in.
/// A stamp more than <see cref="SharedListMerge.FutureLimit"/> past the receiver's now counts as that far ahead (the shared lists' rule).
/// </summary>
public static class TaskAccountSettingsMerge
{
    public static SyncTaskAccountSettings Merge(SyncTaskAccountSettings? first, SyncTaskAccountSettings? second, DateTimeOffset now)
    {
        DateTimeOffset limit = now + SharedListMerge.FutureLimit;
        SyncTaskAccountSettings a = Clamped(first ?? SyncTaskAccountSettings.Empty, limit);
        SyncTaskAccountSettings b = Clamped(second ?? SyncTaskAccountSettings.Empty, limit);

        bool viewFromFirst = FirstWins(a.DefaultViewStamp, b.DefaultViewStamp, ViewText(a.DefaultViewId), ViewText(b.DefaultViewId));
        bool calendarFromFirst = FirstWins(a.PickedCalendarStamp, b.PickedCalendarStamp, a.PickedCalendarId ?? string.Empty, b.PickedCalendarId ?? string.Empty);
        bool ringFromFirst = FirstWins(a.RingOnStamp, b.RingOnStamp, TaskRingDevices.Text(a.RingOn), TaskRingDevices.Text(b.RingOn));
        return new SyncTaskAccountSettings(
            viewFromFirst ? a.DefaultViewId : b.DefaultViewId,
            viewFromFirst ? a.DefaultViewStamp : b.DefaultViewStamp,
            calendarFromFirst ? a.PickedCalendarId : b.PickedCalendarId,
            calendarFromFirst ? a.PickedCalendarStamp : b.PickedCalendarStamp,
            ringFromFirst ? a.RingOn : b.RingOn,
            ringFromFirst ? a.RingOnStamp : b.RingOnStamp);
    }

    /// <summary>
    /// A copy read from a file or a backup (not trusted): a value without a stamp is dropped (it can't take part in a merge), the empty
    /// id reads as the built-in Default, a blank calendar id as off, and a calendar id longer than
    /// <see cref="SyncTaskAccountSettings.MaxCalendarIdLength"/> drops that field. A "rings on" list loses empty and repeated ids and is
    /// kept in id order; one longer than <see cref="TaskRingDevices.MaxDevices"/> drops that field. Null stays null.
    /// </summary>
    public static SyncTaskAccountSettings? Clean(SyncTaskAccountSettings? settings)
    {
        if (settings is null)
        {
            return null;
        }

        TaskFieldStamp? viewStamp = Usable(settings.DefaultViewStamp);
        Guid? view = viewStamp is null || settings.DefaultViewId == Guid.Empty ? null : settings.DefaultViewId;

        string? calendar = string.IsNullOrWhiteSpace(settings.PickedCalendarId) ? null : settings.PickedCalendarId.Trim();
        TaskFieldStamp? calendarStamp = calendar is { Length: > SyncTaskAccountSettings.MaxCalendarIdLength } ? null : Usable(settings.PickedCalendarStamp);
        IReadOnlyList<Guid>? ringOn = TaskRingDevices.Normalized(settings.RingOn);
        TaskFieldStamp? ringStamp = ringOn is { Count: > TaskRingDevices.MaxDevices } ? null : Usable(settings.RingOnStamp);
        return new SyncTaskAccountSettings(view, viewStamp, calendarStamp is null ? null : calendar, calendarStamp, ringStamp is null ? null : ringOn, ringStamp);
    }

    private static TaskFieldStamp? Usable(TaskFieldStamp? stamp)
    {
        // Only the time and the device take part; earlier hashes belong to a task's title and note.
        return stamp is null ? null : new TaskFieldStamp(stamp.At, stamp.By);
    }

    private static bool FirstWins(TaskFieldStamp? first, TaskFieldStamp? second, string firstValue, string secondValue)
    {
        if (first is null || second is null)
        {
            return first is not null;
        }

        int byTime = first.At.UtcTicks.CompareTo(second.At.UtcTicks);
        if (byTime != 0)
        {
            return byTime > 0;
        }

        int byDevice = string.CompareOrdinal(first.By.ToString("D"), second.By.ToString("D"));
        if (byDevice != 0)
        {
            return byDevice > 0;
        }

        return string.CompareOrdinal(firstValue, secondValue) >= 0;
    }

    private static string ViewText(Guid? id)
    {
        return id?.ToString("D") ?? string.Empty;
    }

    private static SyncTaskAccountSettings Clamped(SyncTaskAccountSettings settings, DateTimeOffset limit)
    {
        return settings with
        {
            DefaultViewStamp = settings.DefaultViewStamp is { } view && view.At > limit ? view with { At = limit } : settings.DefaultViewStamp,
            PickedCalendarStamp = settings.PickedCalendarStamp is { } calendar && calendar.At > limit ? calendar with { At = limit } : settings.PickedCalendarStamp,
            RingOnStamp = settings.RingOnStamp is { } ring && ring.At > limit ? ring with { At = limit } : settings.RingOnStamp,
        };
    }
}
