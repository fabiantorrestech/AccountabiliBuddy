namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The pages of the setup assistant, in order.</summary>
public enum AssistantStep
{
    Welcome,

    /// <summary>Link to your other devices first (ADR 22): a new device then gets their profiles and plan.</summary>
    LinkFirst,

    /// <summary>ADR 55: restore the history of a removed device from a backup (Drive or a file) once linked; skipped when this PC is in no group.</summary>
    Restore,

    /// <summary>What kind of computer this is: Personal, Work or Local only (each a starting set of switches).</summary>
    ComputerType,
    Import,
    Usage,
    Review,
    Browsers,
    Basics,

    /// <summary>ADR 47 (C 15): which days are workdays, and a work window or a budget of hours for them.</summary>
    Workdays,

    /// <summary>ADR 39: which Cold Turkey blocks the app holds, where, and Cold Turkey's settings for it.</summary>
    ColdTurkeyKeys,

    /// <summary>ADR 39: the spare key, shown once and typed back.</summary>
    SpareKey,

    /// <summary>ADR 46: the own relay, a pointer to Devices → Own relay (only in the migration assistant).</summary>
    OwnRelay,

    /// <summary>The Setup DP Assistant, a button on the Day plan tab: a pointer page with an "Open it now" button (only in the migration assistant).</summary>
    DpAssistant,

    /// <summary>The start-of-day assistant on the first arrival of a workday: on or off (only in the migration assistant).</summary>
    StartOfDay,

    /// <summary>ADR 50: the Tasks page's estimates, notes, Later list and Put in plan, a pointer to Tasks (only in the migration assistant).</summary>
    Tasks,

    /// <summary>The Plan page's helper: suggestions about a coming day's calendar, today's goal and next week's day types, a pointer to Plan (only in the migration assistant).</summary>
    PlanHelper,

    /// <summary>ADR 49: the look, a theme and light or dark, set here or later in Settings → Appearance (only in the migration assistant).</summary>
    Appearance,

    /// <summary>ADR 51: the session overlay docked over the taskbar, with its placement and look in Settings (only in the migration assistant).</summary>
    OverlayTaskbar,

    /// <summary>The Ctrl+Alt panel that rises from the overlay: hold to peek, Ctrl+Alt+Space to keep it open (only in the migration assistant).</summary>
    PanelCtrlAlt,

    /// <summary>ADR 58: calendar events shaping the day's guarded windows, a pointer to Settings › Connections (only in the migration assistant).</summary>
    CalendarRules,

    /// <summary>ADR 59: the quit safeguard, set up here or in Settings › Guards (only as a new feature, not in the first run).</summary>
    QuitSafeguard,

    /// <summary>Step 13: the shutdown routine, an optional end-of-day flow on Today, set in Settings › Your day (only as a new feature, not in the first run).</summary>
    ShutdownRoutine,

    /// <summary>ADR 76: the Tasks redesign, an introduction (only as a new feature); the view chooser follows it.</summary>
    TasksRedesign,

    /// <summary>
    /// ADR 76 (owner, 2026-11-11): where the account's Tasks view comes from: a backup file from this PC, the copy stored in the account,
    /// or a fresh start. In the first run (once the task list is in use) and as a new feature, after <see cref="TasksRedesign"/>.
    /// </summary>
    TaskViewChooser,

    /// <summary>
    /// "Which devices should ring for reminders?" (13s): the account's "Task reminders ring on" list. In the first run after joining a
    /// device group that has a phone, and as a new feature.
    /// </summary>
    TaskRingDevices,

    /// <summary>ADR 78: task reminders to Google Calendar need one more OK from Google (Allow), or Nowhere (only as a new feature).</summary>
    TaskCalendarExport,

    /// <summary>Round 2 R5 (Q440): Goals, what they are, Deep work built in, and the weekly review's day (only as a new feature).</summary>
    GoalsIntro,

    /// <summary>
    /// ADR 68: AccountabiliBuddy or ProductaBuddy, in the first run (ProductaBuddy preselected on a fresh install), as the full
    /// setup's last page, and as a new feature for upgraded installs.
    /// </summary>
    Guidance,

    /// <summary>The first run's last question: how much focus a day (redesign phase 2).</summary>
    Goal,
    Done,
}
