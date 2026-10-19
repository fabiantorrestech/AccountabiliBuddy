namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>The parts a backup can hold; each can be exported and restored on its own.</summary>
[Flags]
public enum BackupParts
{
    None = 0,

    /// <summary>Profiles, rules, the weekly schedule, time off, one-off blocks, unlock and lock settings.</summary>
    Profiles = 1,

    /// <summary>Quick session, privacy choices, watched browsers, Cold Turkey sightings, calendar settings.</summary>
    Settings = 2,

    /// <summary>Calendar sources (links, Google accounts, shown/hidden calendars) and in-app events.</summary>
    Calendars = 4,

    /// <summary>The activity history: segments, classify answers and session events.</summary>
    History = 8,

    All = Profiles | Settings | Calendars | History,
}
