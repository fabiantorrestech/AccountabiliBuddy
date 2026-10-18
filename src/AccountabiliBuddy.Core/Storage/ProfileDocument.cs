using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Storage;

/// <summary>
/// Everything the user configures, as one versioned JSON document (ADR 7).
/// This is the export/import and sync format shared with future companion apps.
/// Version 2 added <see cref="Locks"/>, <see cref="FocusProfile.UnlockOptions"/> and <see cref="WeeklySchedule.OffDays"/>.
/// Version 3 added <see cref="LockSettings.LooseningDelay"/> and <see cref="FocusProfile.AskAboutUnlistedSites"/>.
/// </summary>
public sealed record ProfileDocument(
    int SchemaVersion,
    IReadOnlyList<FocusProfile> Profiles,
    WeeklySchedule Schedule,
    PomodoroSettings PomodoroDefaults,
    EscalationSettings Escalation,
    BypassSettings Bypass)
{
    public const int CurrentSchemaVersion = 3;

    public LockSettings Locks { get; init; } = LockSettings.Default;

    public static ProfileDocument CreateDefault()
    {
        return new ProfileDocument(
            CurrentSchemaVersion,
            Array.Empty<FocusProfile>(),
            WeeklySchedule.Empty,
            PomodoroSettings.Default,
            EscalationSettings.Default,
            BypassSettings.Default);
    }
}
