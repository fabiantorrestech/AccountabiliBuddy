namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>The parts a backup can hold; each can be exported and restored on its own.</summary>
[Flags]
public enum BackupParts
{
    None = 0,

    /// <summary>Profiles, rules, the day plan (one-off days included), unlock and lock settings.</summary>
    Profiles = 1,

    /// <summary>Quick session, privacy choices, watched browsers, Cold Turkey sightings, calendar settings.</summary>
    Settings = 2,

    /// <summary>Calendar sources (links, Google accounts, shown/hidden calendars) and in-app events.</summary>
    Calendars = 4,

    /// <summary>The activity history: segments, classify answers and session events.</summary>
    History = 8,

    /// <summary>ADR 71: the settings every device shares besides the profiles: sleep (bedtime reminders too), the day bar, the goal, the mode.</summary>
    SyncedSettings = 16,

    /// <summary>ADR 71: every device-local setting in the registry (<see cref="DeviceSettingsRegistry"/>), both modes' copies.</summary>
    DeviceSettings = 32,

    /// <summary>ADR 71: the task lists: tasks, Later, come-backs, task categories (with their deletions).</summary>
    TaskLists = 64,

    All = Profiles | Settings | Calendars | History,
}
