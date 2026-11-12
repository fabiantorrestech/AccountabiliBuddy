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
        SessionTasksService tasks, PlanHelperDismissals planHelper, OverlaySettingsService overlay, CalendarOverlayStore calendarOverlay, QuitGuardService quitGuard,
        ShutdownService shutdown, GuidanceModeService guidance, Tasks.TaskStore taskStore, Tasks.TaskAccountSettingsService taskAccount,
        Tasks.TaskCalendarExportService? taskExport, Tasks.TaskReminderSettingsService? taskReminderSettings, Func<bool> hasPhones) => new[]
    {
        // ADR 76: the Tasks redesign (Phase 8). Its page is an introduction; "where your Tasks view comes from" follows it (set up once a
        // default view was chosen for the account, on any device).
        new SetupFeature(
            "tasks.redesign", "0.1.131",
            "Tasks: one list with Today and the Shelf",
            "Today holds This session, Queued and Finished today; the Shelf holds To sort, Next and Later with saved views. Labels, #tags, steps, reminders and a weekly tidy-up. Pick where your Tasks view comes from: a backup file from this PC, the copy stored in your account, or a fresh start.",
            IsRelevant: () => taskStore.IsActive,
            IsSetUp: () => Core.Tasks.TaskAccountSettingsEdits.IsChosen(taskAccount.Current.DefaultViewStamp),
            Step: ViewModels.AssistantStep.TasksRedesign),
        // 13s: which devices ring for task reminders, the account's list (only phones can be turned off, so only with a phone linked).
        new SetupFeature(
            "tasks.ringDevices", "0.1.131",
            "Task reminders: which devices ring",
            "Task reminders ring on this PC and on the phones you leave on, for your whole account. Each label can ring on fewer devices.",
            IsRelevant: () => taskStore.IsActive && hasPhones(),
            IsSetUp: () => Core.Tasks.TaskAccountSettingsEdits.IsChosen(taskAccount.Current.RingOnStamp),
            Step: ViewModels.AssistantStep.TaskRingDevices),
        // ADR 78: task reminders to Google Calendar need one more OK from Google on installs that connected Google before.
        new SetupFeature(
            "tasks.calendarExport", "0.1.131",
            "Task reminders in Google Calendar",
            "Tasks with a reminder or a due date can go to an \"AB Reminders\" calendar of the app's own (title and times only). Google needs one more OK for it, or choose Nowhere.",
            IsRelevant: () => taskStore.IsActive && taskExport?.ExportAccountName is not null,
            IsSetUp: () => taskExport is null || taskExport.IsAllowed
                || taskReminderSettings?.Current.NewRemindersTo == Core.Tasks.TaskCalendarDestination.Nowhere,
            Step: ViewModels.AssistantStep.TaskCalendarExport),
        new SetupFeature(
            "guidance.mode", "0.1.129",
            "ProductaBuddy: a companion without the guarding",
            "Keep sessions, your day, tasks, stats, sounds and your Cold Turkey and Curbox blocks, with nothing watching your screens or nudging you, on all your devices. Or stay with AccountabiliBuddy as it is.",
            IsRelevant: () => true,
            IsSetUp: () => guidance.IsSetUp,
            Step: ViewModels.AssistantStep.Guidance),
        new SetupFeature(
            "shutdown.routine", "0.1.126",
            "Shutdown routine: wrap up the day, then \"Work over for today\"",
            "A short optional flow on Today when work ends or when you press Start shutdown: empty your capture, sort unfinished tasks, look ahead, plan tomorrow, review the day. Every step can be skipped and nothing is locked.",
            IsRelevant: () => true,
            IsSetUp: () => shutdown.HasBeenConfigured,
            Step: ViewModels.AssistantStep.ShutdownRoutine),
        new SetupFeature(
            "quit.safeguard", "0.1.124",
            $"Quit safeguard: stop {AppBrand.Name} being closed when it matters",
            "Refuses the tray's Quit during the parts of your day you pick, during a session or all the time, and brings the app back if it's ended from Task Manager then.",
            IsRelevant: () => true,
            IsSetUp: () => quitGuard.Current.On,
            Step: ViewModels.AssistantStep.QuitSafeguard),
        new SetupFeature(
            "coldturkey.keys", "0.1.106",
            $"Cold Turkey: let {AppBrand.Name} hold the keys",
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
        new SetupFeature(
            "overlay.hide", "0.1.120",
            "Ctrl+Alt+H: hide the overlay until you want it",
            "Press Ctrl+Alt+H in any app to hide the overlay (it stays hidden until you press it again, even after a restart; holding Ctrl+Alt still shows it). The Ctrl+Alt panel also has a button that turns the overlay off, which Today or Settings turn back on.",
            IsRelevant: () => true,
            IsSetUp: () => overlay.Current.HideShortcutSeen,
            Step: ViewModels.AssistantStep.PanelCtrlAlt),
        new SetupFeature(
            "calendar.rules", "0.1.123",
            "Calendar events: let them shape your guarded windows",
            "Give a calendar event, or a whole calendar by keywords or for every event, a day plan category, so the guards follow the real day. A looser change waits first; Today shows what came from the calendar.",
            IsRelevant: () => true,
            IsSetUp: () => calendarOverlay.Classifications.Count > 0 || calendarOverlay.PendingClassifications.Count > 0
                || !calendarOverlay.Rules.Active.IsEmpty || calendarOverlay.Rules.Pending is not null,
            Step: ViewModels.AssistantStep.CalendarRules),
    };
}
