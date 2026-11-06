namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// How the app helps (ADR 68), one setting for every linked device. Shown as AccountabiliBuddy (<see cref="Guarding"/>) and
/// ProductaBuddy (<see cref="Companion"/>).
/// </summary>
public enum GuidanceMode
{
    /// <summary>Watches for drift and guards: escalation, work hours, Pre-Work, nudges, bedtime reminders.</summary>
    Guarding,

    /// <summary>The companion only: sessions, the day, tasks, stats and the blockers' own blocks; no watching, nudging or guarding.</summary>
    Companion,
}
