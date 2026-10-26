# Phase 5: timers and alarms V1 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** C 6.2, C 6.3 and W 2 of the user's list: named, editable timers and alarms on each device (PC, phone, watch), local to that device (V1; synced timers come with the sync server, C 6.1), shown like phase ends, plus a "Timers" watch complication.

**Spec:** `doc/plans/2026-10-26-feature-list-plan.md` (decisions: "V1 local per device: PC glow + chime + a bar per timer in the overlay; phone one notification per timer with a live bar and an alarm sound; watch buzz + its own page. Named, editable." and "Two complications: the existing session one, and a new 'Timers' one (soonest ending)").

## Global Constraints
- Implementer rules: `.superpowers/sdd/implementer-rules.md`. No new dependencies. Times 12-hour.
- Timers never touch focus sessions, enforcement or History; they are reminders only. They work during a session (a running session never blocks them).
- Device-local: PC document "timers" (`SqliteJsonDocumentStore`), phone prefs "timers", watch its own storage. Nothing leaves the device (V1).
- Survive restarts: a timer stores its end time (trusted clock on the PC), not a remaining count; an alarm stores its wall-clock time and repeat days.
- Material icons on PC/phone (ADR 32), the same icon for the same meaning (timer: `Timer`, alarm: `Alarm`).

## Model (same on every device)
- **Timer:** id, name (default "Timer"), length, end time when running, paused remaining when paused, state (running/paused/done). Actions: start, pause, resume, +1 min, reset, rename, delete. Several at once.
- **Alarm:** id, name (default "Alarm"), time of day (12-hour UI), days (none = once), on/off. Actions: add, edit, toggle, delete, snooze 9 min / dismiss when ringing.
- When one ends: the device's "time is up" signal (PC: white glow + the chosen chime, like a phase end, plus a small window "Timer · <name> is up" with Dismiss and +1 min; phone: an alarm-style notification with sound + vibration, full-screen if the screen is off like `BedtimeAlarmActivity`; watch: buzz pattern like the phase-end alerts + its page).

## Tasks

### Task 1: PC timers and alarms
**Files:** new `src/AccountabiliBuddy.App/Services/Timers/` (`TimerItem`, `AlarmItem`, `TimersService` with a 1 s tick from the app's tick, document "timers"), `ViewModels/TimersViewModel.cs` + a Timers card on Today (add timer with presets 5/10/15/25 + custom, add alarm), the on-screen window: one thin bar per running timer under the timer/timeline (name + time left, 12-hour for alarms), Ctrl+Alt details show the list with pause/+1/dismiss; the ring window reuses the glow/chime path (`PcAlarmService` / `BedtimeAlarmRinger` pattern for the window, the phase-end glow + `Sounds when time is up` chime); a Windows wake timer is NOT needed for V1 timers (only alarms that should wake the PC — skip in V1, note it).
- [ ] Commit `PC: timers and alarms (local), bars on the on-screen window`.

### Task 2: Phone timers and alarms
**Files:** new `android/app/.../timers/` (store, exact alarms via `AlarmManager.setAlarmClock`/`setExactAndAllowWhileIdle` like `BedtimeReminders`, receiver, full-screen ring activity like `BedtimeAlarmActivity`), a Timers section on Today, **one notification per running timer** with a live countdown (`setUsesChronometer` + `setChronometerCountDown`) and a progress bar, actions Pause / +1 min / Dismiss; alarms in the same list.
- [ ] Commit `Phone: timers and alarms (local), a notification per timer`.

### Task 3: Watch timers page + Timers complication
**Files:** `android/wear/.../ui/TimersPage.kt` (start a preset/custom timer, list with countdown rings, pause/dismiss), local scheduling (exact alarm + the existing alert buzz in `alerts/`), a second complication service `TimersComplicationService` (RANGED_VALUE + SHORT_TEXT: the soonest-ending running timer, its time left; empty/"No timer" when none) declared in the wear manifest beside `FocusComplicationService`.
- Watch timers are the watch's own (V1); the phone's timers are not mirrored.
- [ ] Commit `Watch: timers page and a Timers complication`.

## Execution
Tasks 1, 2, 3 in parallel (no shared files).
