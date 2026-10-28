namespace AccountabiliBuddy.App.Services.Setup;

/// <summary>
/// Every feature a user may need to set up after an upgrade (project rule: a new or reworked feature that needs
/// setting up gets an entry here with the version it arrives in, its "set up?" check and its assistant page). The
/// migration assistant (5 taps on the About logo; a quiet Today card after an upgrade) runs the pages of the ones not
/// set up yet.
/// </summary>
public static class SetupFeatures
{
    public static IReadOnlyList<SetupFeature> All(ColdTurkeyKeysService keys, ColdTurkeyService coldTurkey, Sync.SyncSettingsService sync,
        ConfigurationService configuration, StartNudgeService startNudge, DpAssistantService dpAssistant, Theming.AppTheme appearance,
        SessionTasksService tasks, PlanHelperDismissals planHelper, OverlaySettingsService overlay) => new[]
    {
        new SetupFeature(
            "coldturkey.keys", "0.1.106",
            "Cold Turkey: let AccountabiliBuddy hold the keys",
            "Locks the Cold Turkey blocks you pick during focus sessions, Pre-Work and work hours, with a spare key for emergencies.",
            IsRelevant: () => coldTurkey.Blocks is not null,
            IsSetUp: () => keys.Held.Count > 0 && keys.HasSpareKey,
            Step: ViewModels.AssistantStep.ColdTurkeyKeys),
        new SetupFeature(
            "sync.relay", "0.1.111",
            "Own relay: sync through your own server",
            "Syncs your devices through a small relay you run, with changes arriving in about a second and no Google account.",
            IsRelevant: () => true,
            IsSetUp: () => sync.Current.RelayOn && !string.IsNullOrWhiteSpace(sync.Current.RelayUrl),
            Step: ViewModels.AssistantStep.OwnRelay),
        new SetupFeature(
            "dayplan.modes", "0.1.111",
            "Day plan modes: a work window, a budget of hours, alternating weeks",
            "Describes your workdays as just a work window or a number of productive hours instead of a full timeline, with free days and week A / week B.",
            IsRelevant: () => true,
            IsSetUp: () => configuration.Current.DayPlan is { } plan
                && (plan.Cycle.Weeks.Count > 1 || plan.Templates.Any(template => template.Template.Mode != Core.Domain.DayMode.Schedule || template.Template.WorkWindow is not null)),
            Step: ViewModels.AssistantStep.Workdays),
        new SetupFeature(
            "startofday.assistant", "0.1.111",
            "Start-of-day assistant: hours, first tasks, start",
            "On your first arrival of a workday, asks how many hours you hope to get done, what's first, and starts your session.",
            IsRelevant: () => startNudge.Settings.On,
            IsSetUp: () => startNudge.AssistantSeen,
            Step: ViewModels.AssistantStep.StartOfDay),
        new SetupFeature(
            "dayplan.assistant", "0.1.112",
            "Setup DP Assistant: your day plan, step by step",
            "A button at the top right of the Plan page (also in Settings › Setup) that sets up workdays, work time, deep work goal, lunch, sleep and weeks with sensible defaults, then shows how the buddy will behave before it saves.",
            IsRelevant: () => true,
            IsSetUp: () => dpAssistant.HasRun,
            Step: ViewModels.AssistantStep.DpAssistant),
        new SetupFeature(
            "tasks.details", "0.1.114",
            "Tasks: estimates, notes, a Later list, Put in plan",
            "Give a task an estimate and a private note, move it to Later, or put it in the plan for a coming part of your day, all on the Tasks page. Notes stay sealed and sync only between your devices.",
            IsRelevant: () => true,
            IsSetUp: () => tasks.UsesTaskDetails,
            Step: ViewModels.AssistantStep.Tasks),
        new SetupFeature(
            "plan.helper", "0.1.114",
            "Plan helper: suggestions on the Plan page",
            "Tells you when a coming day's calendar covers most of its work, when today's goal no longer fits the work time left, or when a day next week has no day type, and can move the work for you the way you would by hand.",
            IsRelevant: () => true,
            IsSetUp: () => planHelper.HasUsed,
            Step: ViewModels.AssistantStep.PlanHelper),
        new SetupFeature(
            "appearance.themes", "0.1.114",
            "Appearance: three themes, light and dark on a schedule",
            "Pick Instrument, Night Desk or Paper, and have the app go dark at a time you set or after sunset, with a place that stays on this PC.",
            IsRelevant: () => true,
            IsSetUp: () => appearance.HasPickedTheme,
            Step: ViewModels.AssistantStep.Appearance),
        new SetupFeature(
            "overlay.taskbar", "0.1.116",
            "Overlay: docked over the taskbar",
            "The on-screen timer sits over the taskbar next to the tray, follows the taskbar when it hides or moves, and shows a corner clock over full-screen apps. Pick where it lives, its size and what it shows in Settings.",
            IsRelevant: () => true,
            IsSetUp: () => overlay.HasChosenPlacement,
            Step: ViewModels.AssistantStep.OverlayTaskbar),
        new SetupFeature(
            "panel.ctrlalt", "0.1.116",
            "Ctrl+Alt panel: Done, Pause and your tasks from anywhere",
            "Hold Ctrl+Alt (or press Ctrl+Alt+Space) and a panel rises from the overlay with Done, Pause, +10 min, this session's tasks, the sound and shortcuts.",
            IsRelevant: () => true,
            IsSetUp: () => overlay.Current.PanelSeen,
            Step: ViewModels.AssistantStep.PanelCtrlAlt),
    };
}
