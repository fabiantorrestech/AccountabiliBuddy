# Redesign phase 4: the daily goal and growth

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** the daily deep-work goal (asked on the first run, document "goal") becomes visible and useful: progress on Today and the on-screen window, a "Goal met" moment, History's deep work against the goal over days, the personal PC's work hours lifting once the goal is met, and the goal on the phone and watch.

**Architecture:** Core gains `DeepWork` (focused time per day across this PC's counted sessions and the other devices' synced sessions, the union so overlap counts once; longest unbroken stretch) and `GoalProgress` (focused vs goal, texts). The App gets `DeepWorkService` (cached session records refreshed every minute and on session changes, received devices, the goal; `Changed`, `GoalReached` once a day), a goal card on Today, a line in the on-screen window's Ctrl+Alt details, `WorkHoursGuard.GoalMet`, and a History chart control. The link's `focusDay` and the watch snapshot carry the goal (ADR 29).

**Tech Stack:** .NET 10, Avalonia 12.1.4, CommunityToolkit.Mvvm; Kotlin for the phone and watch (Task 5). Scratch checks in `%TEMP%\ab-sync-checks`, never repo tests.

**Spec:** `doc/plans/2026-10-22-redesign-design.md` sections G (goal and growth) and K (earned time: "When the work PC reports today's deep-work goal met, the personal PC's Work-hours profile lifts for the rest of the day"). Phone pickups (G) and pickups across PCs (K) move to phase 3: they need the sync file fields ADR 26 amends.

**Research step (G asks for it):** the y-axis is **focused minutes per day** with the goal as a line. Newport's working range for deep work is 1 to 4 hours a day, which the first-run choices (30 min to 4 h) already span; interruption-recovery research (Gloria Mark, UC Irvine) makes **longest unbroken stretch** the second number shown, not a second axis. Drifts per focused hour stay on the day bar.

## Global Constraints

- Core has no UI or OS dependencies (ADR 4).
- Focus time counts only sessions the user starts and sessions added by hand (ADR 20); stepped-away time doesn't count; omitted sessions and old scheduled blocks don't count.
- Time two devices both had in focus counts once (as the day bar already does).
- The goal unlock only relaxes the guard (K: "Sync only relaxes it").
- Commit per task with the session's trailers; never commit `handoffContext/`.

## Review Focus

1. A session running across midnight: today's total counts only today's part.
2. The goal met by sessions on another device only (work PC → personal PC through the sync files): the personal PC's guard lifts.
3. The goal changed lower after it was met / higher after: "Goal met" isn't announced twice the same day; raising it above today's total starts guarding again.
4. A day with no sessions: the chart shows an empty day, not a gap that shifts the others.
5. The phone on an older PC version (no goal in `focusDay`): it shows no goal, nothing breaks.

---

### Task 1: Deep work totals and goal progress (Core)

**Files:** Create `Core/Engine/DeepWork.cs`, `Core/Engine/GoalProgress.cs`, `Core/Engine/DeepWorkDay.cs`. Check: synccore `DeepWorkChecks.cs`.

**Interfaces:**
- `DeepWork.Days(IReadOnlyList<SessionRecord> records, IEnumerable<SyncSession> others, Func<DateTimeOffset,bool> isOmitted, bool sessionRunning, DateOnly from, DateOnly to, DateTimeOffset now, TimeZoneInfo zone) → IReadOnlyList<DeepWorkDay>` (`DeepWorkDay(DateOnly Date, TimeSpan Focused, TimeSpan Longest)`), one per date, empty days included.
- `GoalProgress(TimeSpan Focused, int GoalMinutes)`: `Goal`, `Met`, `Fraction` (0..1), `Text` ("40m of 1h", "Goal met · 1h 05m").

- [ ] Checks: across midnight; two devices overlapping count once; away time left out; omitted left out; empty day present; longest stretch; texts.
- [ ] RED → implement → GREEN; commit `Deep work totals and goal progress`.

### Task 2: Goal on Today, the on-screen window and "Goal met"

**Files:** Create `App/Services/DeepWorkService.cs`, `App/ViewModels/GoalCardViewModel.cs`; modify `Views/TodayView.axaml`, `ViewModels/OnScreenOverlayViewModel.cs` + `Views/SessionOverlayWindow.axaml` (Ctrl+Alt details line), `App.axaml.cs` (reminder "Goal met · 1h 05m", teal). Check: syncapp `DeepWorkServiceChecks.cs`.

- [ ] Checks: progress follows a running session minute by minute; `GoalReached` once a day, again the next day; a received device's session counts.
- [ ] RED → implement → GREEN; dev screenshot of Today; commit `Today shows the daily goal; "Goal met" once a day`.

### Task 3: Earned time on the personal PC

**Files:** modify `Services/WorkHoursGuard.cs` (`GoalMet` Func<bool>: guarding, `HasWorkLeftToday` and `DescribeCut` all off once met; one notice "Goal met · 2h 10m · this computer is open"), `App.axaml.cs`. Check: syncapp `WorkHoursChecks` additions.

- [ ] Checks: goal met → not guarding, turning off is free; goal raised above today's total → guarding again.
- [ ] RED → implement → GREEN; commit `Earned time: the goal met opens the personal PC`. Ship 0.1.72 (project `CLAUDE.md`).

### Task 4: History: deep work against the goal

**Files:** Create `App/Controls/DeepWorkChart.cs` (bars per day, goal line, today highlighted), `App/ViewModels/DeepWorkHistoryViewModel.cs`; modify `Views/HistoryView.axaml` (a card at the top: last 7 / 30 days, week total, days the goal was met, longest stretch).

- [ ] Check (syncapp): the 7-day model (labels, goal line height, met count) from fixed records.
- [ ] Dev screenshot; commit `History: deep work against the goal`.

### Task 5: The goal on the phone and watch (ADR 29)

**Files:** ADR `doc/adr/0029-share-the-daily-goal-with-paired-devices.md`; `Core/Link/LinkFocusDay.cs` (+ optional `GoalMinutes`), `FocusDayBuilder` caller; `android/shared` `FocusDay`/`FocusDayJson`/`WatchSnapshot`/`SnapshotBuilder` (+ `goalMinutes`, optional); phone `DayHeader` summary ("40m of 1h goal"), widget; watch ring outer = goal, buzz once when the goal is reached. Checks: linkcheck round-trip, ktcheck `GoalChecks.kt`.

- [ ] RED → implement → GREEN; `./gradlew assembleRelease`; commit `The daily goal on the phone and watch`. Ship 0.1.73 + APKs.
