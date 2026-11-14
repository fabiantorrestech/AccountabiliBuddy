namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// Where every app-owned document belongs in a backup (ADR 71). <see cref="DeviceKeys"/> make up "This PC's settings", each with its
/// ProductaBuddy copy (the key plus <see cref="ModeSettings.CompanionSuffix"/>, ADR 69). The synced settings and the task lists go
/// through their services in their own kinds, the calendars in their own part, and <see cref="StateKeys"/> never go in a backup:
/// the state of the moment, secrets sealed to this PC, and data that comes back by itself. A new document must join one of these
/// lists; in a dev build <see cref="Unclassified"/> names any that didn't, at start.
/// </summary>
public static class DeviceSettingsRegistry
{
    public static IReadOnlyList<string> DeviceKeys { get; } =
    [
        "quickSession", "privacy", "integrations.browsers.watched", "calendar.settings", "calendar.rules", "calendar.classifications",
        "overlay", "sounds", "theme", "headphones", "hyperfocus", "startNudge", "noise", "workHours", "lightsOut", "quit.guard",
        "session.pcOff", "stepAway", "shutdown", "batteryVisibility", "syncLight", "motion", "timers", "tasks.filterPresets", "tasks.view",
        "stats.view", "history.hidden", "history.hiddenDistractions", "history.names", "dailySteps", "enforcement.limit",
        "setup.computerType", "classify.exemptionProfiles", "dpAssistant", "planHelperDismissed", "updates", "backup.auto.settings",
        "tasks.viewSearches", "tasks.categoryIds", "tasks.settings", "tasks.reminderSettings", "tasks.layout", "today.layout",
        // Round 2 R4: the goals page's device settings (Deep work's weekly target restored through its waiting rule).
        "goals.deepWork", "goals.ui", "goals.settings",
    ];

    /// <summary>Restored through their services in synced settings, so the edit reaches the other devices.</summary>
    public static IReadOnlyList<string> SyncedKeys { get; } = ["profiles", "sleep", "daybar", "goal", "guidance"];

    /// <summary>The task lists' own documents (their kind exports them in their sync form).</summary>
    public static IReadOnlyList<string> TaskKeys { get; } =
        ["session.nextTasks", "session.later", "capture.comebacks", "session.categories", "session.movedToLater", "session.recentlyDeleted",
         "tasks.autoSaved", "tasks.pastSettled", "tasks.keptPast", "tasks.items", "tasks.labels", "tasks.views", "tasks.log",
         "tasks.account", "capture.notes"];

    /// <summary>
    /// Round 2 R4: the goal lists (sealed at rest with this PC's key); they go in backups with the task lists, readable inside the
    /// backup (<see cref="GoalsExport"/>), never as sealed blobs.
    /// </summary>
    public static IReadOnlyList<string> GoalKeys { get; } = ["goals.items", "goals.areas", "goals.entries", "goals.reminders", "goals.reviews"];

    /// <summary>The calendars part (sources with their secrets, in-app events).</summary>
    public static IReadOnlyList<string> CalendarKeys { get; } = ["calendar.sources", "calendar.events"];

    public static IReadOnlyList<string> StateKeys { get; } =
    [
        "app.version", "armedEnds", "beeminder", "calendar.cache", "calendar.classifications.pending", "calendar.firstKnown",
        "clock.anchor", "coldturkey.expected", "coldturkey.keys", "config.pending", "dayplan.editedAt",
        "dev.syncReceipts", "googleSetup", "guidance.asked", "guidance.reasons", "history.appPaths", "history.deleted", "history.gaps",
        "history.notReal", "integrations.coldTurkey.sightings", "learn.firstSession", "lunch", "pickups.pc", "prework",
        "quit.guard.pending", "review.ignored", "review.promoted", "session", "session.overLimit", "session.pcOff.pending",
        "session.taskHistory", "session.tasks", "setup.assistant", "setup.features", "shutdown.autoSaved", "shutdown.reviews", "tasks.receivedHistory", "tasks.handovers",
        "sleep.pendingOff", "sleep.reminders", "startNudge.notNow", "startOfDay", "this-pc", "unlock", "workEnded", "backup.auto", "dayplan.sleepSeeded", "history.sectionsFilled",
        "tasks.model", "tasks.modelAttempt", "tasks.running", "tasks.tidyUp", "tasks.earlierNotice",
        "tasks.reminderMissed", "tasks.volumeRestore", "tasks.calendarExport",
        "goals.model", "goals.modelAttempt", "goals.deepWork.pending", "goals.reminderState",
    ];

    // Whole families of state: the sync machinery (identity, keys, received files, settings), dev switches.
    private static readonly string[] StatePrefixes = ["sync.", "dev."];

    /// <summary>A device setting, or its ProductaBuddy copy.</summary>
    public static bool IsDevice(string key) =>
        DeviceKeys.Contains(key) || (key.EndsWith(ModeSettings.CompanionSuffix, StringComparison.Ordinal) && DeviceKeys.Contains(key[..^ModeSettings.CompanionSuffix.Length]));

    /// <summary>Stored keys that belong to no list (a new document someone forgot to classify).</summary>
    public static IReadOnlyList<string> Unclassified(IEnumerable<string> storedKeys) =>
        storedKeys.Where(key => !IsDevice(key) && !SyncedKeys.Contains(key) && !TaskKeys.Contains(key) && !GoalKeys.Contains(key) && !CalendarKeys.Contains(key)
                                && !StateKeys.Contains(key) && !StatePrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();
}
