using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Storage;

/// <summary>
/// Everything the user configures, as one versioned JSON document (ADR 7).
/// This is the export/import and sync format shared with future companion apps.
/// Version 2 added <see cref="Locks"/>, <see cref="FocusProfile.UnlockOptions"/> and off days.
/// Version 3 added <see cref="LockSettings.LooseningDelay"/> and <see cref="FocusProfile.AskAboutUnlistedSites"/>.
/// Version 4 replaced off days with time off and added one-off blocks.
/// Version 5 replaced the weekly schedule (blocks, time off, one-off blocks) with <see cref="DayPlan"/> (ADR 18).
/// Version 12 gave the day plan named templates, a week map (A/B weeks) and a mode per template (ADR 47).
/// Version 13 turned the week map into a repeating cycle of up to 8 weeks and added a part's AutoStart, a fixed lunch (ADR 47).
/// </summary>
public sealed record ProfileDocument(
    int SchemaVersion,
    IReadOnlyList<FocusProfile> Profiles,
    PomodoroSettings PomodoroDefaults,
    EscalationSettings Escalation,
    BypassSettings Bypass)
{
    public const int CurrentSchemaVersion = 13;

    public LockSettings Locks { get; init; } = LockSettings.Default;

    public DayPlan DayPlan { get; init; } = DayPlan.Default;

    public static ProfileDocument CreateDefault()
    {
        return new ProfileDocument(
            CurrentSchemaVersion,
            Array.Empty<FocusProfile>(),
            PomodoroSettings.Default,
            EscalationSettings.Default,
            BypassSettings.Default);
    }
}
