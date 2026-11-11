using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Changes to the account's Tasks settings (<see cref="SyncTaskAccountSettings"/>) made on this device. A choice is stamped now, or just
/// after the stamp it replaces when that one is ahead (another device's fast clock), so an edit made after seeing a value always wins
/// over it on every device. Choosing the value a field already has, with a real stamp, changes nothing; with only the adopted stamp
/// (<see cref="SyncTaskAccountSettings.AdoptedAt"/>) it is stamped now, so the explicit choice can't lose a tie to another device.
/// </summary>
public static class TaskAccountSettingsEdits
{
    public static SyncTaskAccountSettings WithDefaultView(SyncTaskAccountSettings? settings, Guid? viewId, DateTimeOffset now, Guid self)
    {
        SyncTaskAccountSettings current = settings ?? SyncTaskAccountSettings.Empty;
        Guid? wanted = viewId == Guid.Empty ? null : viewId;
        if (IsChosen(current.DefaultViewStamp) && current.DefaultViewId == wanted)
        {
            return current;
        }

        return current with { DefaultViewId = wanted, DefaultViewStamp = new TaskFieldStamp(After(current.DefaultViewStamp, now), self) };
    }

    public static SyncTaskAccountSettings WithPickedCalendar(SyncTaskAccountSettings? settings, string? calendarId, DateTimeOffset now, Guid self)
    {
        SyncTaskAccountSettings current = settings ?? SyncTaskAccountSettings.Empty;
        string? wanted = string.IsNullOrWhiteSpace(calendarId) ? null : calendarId.Trim();
        if (IsChosen(current.PickedCalendarStamp) && current.PickedCalendarId == wanted)
        {
            return current;
        }

        return current with { PickedCalendarId = wanted, PickedCalendarStamp = new TaskFieldStamp(After(current.PickedCalendarStamp, now), self) };
    }

    /// <summary>Both fields set to <paramref name="defaultViewId"/> and <paramref name="calendarId"/> (a chosen copy, or null for a fresh start).</summary>
    public static SyncTaskAccountSettings WithBoth(SyncTaskAccountSettings? settings, Guid? defaultViewId, string? calendarId, DateTimeOffset now, Guid self)
    {
        return WithPickedCalendar(WithDefaultView(settings, defaultViewId, now, self), calendarId, now, self);
    }

    /// <summary>
    /// The values this device kept on its own before the settings were shared, taken over once: only into a field nothing was chosen
    /// for yet, stamped <see cref="SyncTaskAccountSettings.AdoptedAt"/> so any real choice, on any device, wins over them.
    /// </summary>
    public static SyncTaskAccountSettings Adopted(SyncTaskAccountSettings? settings, Guid? legacyDefaultViewId, string? legacyCalendarId, Guid self)
    {
        SyncTaskAccountSettings current = settings ?? SyncTaskAccountSettings.Empty;
        var adopted = new TaskFieldStamp(SyncTaskAccountSettings.AdoptedAt, self);
        if (current.DefaultViewStamp is null && legacyDefaultViewId is Guid view && view != Guid.Empty)
        {
            current = current with { DefaultViewId = view, DefaultViewStamp = adopted };
        }

        if (current.PickedCalendarStamp is null && !string.IsNullOrWhiteSpace(legacyCalendarId))
        {
            current = current with { PickedCalendarId = legacyCalendarId.Trim(), PickedCalendarStamp = adopted };
        }

        return current;
    }

    /// <summary>
    /// A backup's copy restored (ADR 71's Restore): each field the backup chose becomes this device's choice now, so it wins over later
    /// edits; a field the backup never chose stays as it is.
    /// </summary>
    public static SyncTaskAccountSettings Restored(SyncTaskAccountSettings? settings, SyncTaskAccountSettings backup, DateTimeOffset now, Guid self)
    {
        SyncTaskAccountSettings current = settings ?? SyncTaskAccountSettings.Empty;
        if (backup.DefaultViewStamp is not null)
        {
            current = WithDefaultView(current, backup.DefaultViewId, now, self);
        }

        if (backup.PickedCalendarStamp is not null)
        {
            current = WithPickedCalendar(current, backup.PickedCalendarId, now, self);
        }

        return current;
    }

    /// <summary>A stamp of a real choice (not none, not the one-time take-over at the upgrade).</summary>
    public static bool IsChosen(TaskFieldStamp? stamp)
    {
        return stamp is not null && stamp.At > SyncTaskAccountSettings.AdoptedAt;
    }

    private static DateTimeOffset After(TaskFieldStamp? replaced, DateTimeOffset now)
    {
        return replaced is not null && replaced.At >= now ? replaced.At.AddTicks(1) : now;
    }
}
