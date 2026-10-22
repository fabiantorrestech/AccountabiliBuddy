# Redesign phase 1: navigation, Today, Focus mode, quiet overlay, Step away

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** the desktop app opens on a page whose one job is starting; while a session runs it shows only the essentials; the on-screen window is quiet; a bathroom break is one key.

**Architecture:** Core gains the away state inside `SessionEngine` (pause semantics) and away events in the session history; the App gets a `StepAwayService` (input ends it), a `TodayView` with a `FocusView` swap, re-tiered tabs, and a quiet drawing mode for the on-screen window. No sync or schema-v6 work here (phases 3 and 4).

**Tech Stack:** .NET 10, Avalonia 12.1.4, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, CsWin32. Scratch checks in `%TEMP%\ab-sync-checks` (synccore, syncapp harnesses; `run-pc.sh`), never repo tests.

**Spec:** `doc/plans/2026-10-22-redesign-design.md`, sections A, B, C (incl. "quiet by default" and "Step away"), J "Interrupted".

## Global Constraints

- Core has no UI or OS dependencies (ADR 4); Windows code behind `Core/Ports` interfaces.
- No escape hatches: Step away ends on any input; it never shortens a wait; the countdown pauses (user, 2026-10-22).
- Away stretches are session events; DB schema 5 → 6 (ADR 20 pattern): a newer DB is refused by an older app; the migration adds nothing but the version.
- Existing data: `OverlaySettings` documents without `Quiet` read as quiet (the user's default); every other field keeps its value.
- Match surrounding code: explicit names, one public type per file, file-scoped namespaces, nullable on, comments as the files around.
- Commit per task with the trailers in the session; never commit `handoffContext/`.

## Review Focus

1. A session resumed at launch while away (`SessionCheckpoint` carries `Away`): the app must not end the away on its own startup input; expected: away continues until real input after launch.
2. A Pomodoro break ends while away: expected: the break's end also shifts; the focus cycle that follows starts when the user returns, not in the toilet.
3. Step away pressed with no session running: expected: nothing happens (no event, no badge).
4. The tray's "End session…" and the quick-session popup during Focus mode: expected: they still work and the wait excludes away time.
5. Search results that open a tab (`UiSearchSource`, "opens its tab, profile, date or Settings group"): expected: old indexes (Exceptions = 3, Setup = 6…) still open the right page after the re-tiering.

---

### Task 1: Core: Step away in the session engine

**Files:**
- Modify: `src/AccountabiliBuddy.Core/Domain/SessionState.cs`, `src/AccountabiliBuddy.Core/Engine/SessionEngine.cs`
- Create: `src/AccountabiliBuddy.Core/Domain/AwayKind.cs` (`Break`, `Interrupted`)
- Check: `%TEMP%\ab-sync-checks\synccore\Checks\StepAwayChecks.cs`

**Interfaces:**
- Produces: `SessionState` gains `AwayKind? Away = null, DateTimeOffset? AwaySince = null` (record positional, optional, so JSON checkpoints from before read as not away); `SessionState.IsAway`.
- Produces: `SessionEngine.StepAway(AwayKind kind)` → bool (false when idle or already away); `SessionEngine.Return()` → `TimeSpan?` (the away length; null when not away). On return every `PhaseEndsAt` in the session (manual end, pomodoro phase end) shifts by the away length; `Tick()` never ends a phase while away.

- [ ] **Step 1: Write the failing checks** in `StepAwayChecks.cs` (register in `synccore/Program.cs`):
  - start manual 25 min at T; `StepAway(Break)` at T+10; `Tick` at T+30 → still active; `Return` at T+12 → `PhaseEndsAt == T+27`, returns 2 min.
  - idle engine: `StepAway` → false. Away twice → second false.
  - pomodoro: away during a focus cycle, return → `PhaseEndsAt` shifted; `CompletedFocusCycles` unchanged.
  - `CreateCheckpoint()` → `Restore()` while away keeps `Away` and `AwaySince`.
- [ ] **Step 2: Run** `run-pc.sh` → expected: build errors (`StepAway` missing).
- [ ] **Step 3: Implement** in `SessionEngine`: keep `_awaySince`; `Tick` returns early when `State.IsAway`; `Return` sets `PhaseEndsAt = PhaseEndsAt + away` and clears `Away`; `SetState` carries the fields.
- [ ] **Step 4: Run** → expected: `Step away:` checks PASS, ALL PASS.
- [ ] **Step 5: Commit** `Core: Step away pauses the session (SessionEngine.StepAway/Return)`.

### Task 2: Core: away stretches in the history and the day bar

**Files:**
- Modify: `src/AccountabiliBuddy.Core/Domain/SessionEventKind.cs` (`AwayStarted`, `AwayEnded`), `src/AccountabiliBuddy.Core/Storage/LocalDatabase.cs` (`CurrentSchemaVersion = 6`, migration v6 = version only, backup before as today), `src/AccountabiliBuddy.Core/Engine/SessionHistoryBuilder.cs` (`SessionRecord` gains `IReadOnlyList<TimeRange> Away`), `src/AccountabiliBuddy.Core/Engine/FocusDayBuilder.cs` (counted minutes exclude away)
- Check: `synccore/Checks/StepAwayChecks.cs` (add), existing FocusDay checks still pass

**Interfaces:**
- Produces: `SessionRecord.Away` (pairs from `AwayStarted`/`AwayEnded`; an unmatched start closes at the session's end); `SessionEvent.Reason` carries the kind name ("Break"/"Interrupted") on `AwayStarted` (encrypted column, as today).
- Consumed by Task 5 (day bar) and phase 3 (sync files: times only).

- [ ] **Step 1: Checks:** events Started, AwayStarted(T+10), AwayEnded(T+14), Ended(T+30) → one record, `Away == [T+10..T+14]`, counted focus 16 min in `FocusDayBuilder`. Unmatched AwayStarted → closes at Ended.
- [ ] **Step 2: Run** → fails (kinds missing).
- [ ] **Step 3: Implement.** Schema bump: `CurrentSchemaVersion = 6`; the migration list gets a v6 entry that only records the version (comment why).
- [ ] **Step 4: Run** → PASS; also `dotnet build AccountabiliBuddy.sln`.
- [ ] **Step 5: Commit** `Core: away stretches are session events; DB schema 6`.

### Task 3: App: StepAwayService, hotkey, tray, and sleeping prompts

**Files:**
- Create: `src/AccountabiliBuddy.App/Services/StepAwayService.cs`
- Modify: `src/AccountabiliBuddy.App/Services/SessionController.cs` (`StepAway(AwayKind)`, `Return()` → record events via the existing session-event path), `src/AccountabiliBuddy.App/Services/EnforcementCoordinator.cs` (no decisions while `State.IsAway`), `src/AccountabiliBuddy.Core/Engine/BypassPolicy.cs` + `SessionChecklist.CanEndAtOnce` callers (elapsed excludes away: both take `TimeSpan awaySoFar`), `src/AccountabiliBuddy.App/App.axaml.cs` (wire: service, tray item "Step away / I'm back", hotkey from `QuickSessionPreferences`-style document key `"stepAway"`: hotkey text, `AutoAfterIdle` off by default, `IdleMinutes = 5`), `src/AccountabiliBuddy.App/Views/SettingsView.axaml` + `SettingsViewModel` (Focus sessions group: hotkey, auto-after-idle switch)
- Uses: `Win32UserIdleDetector` (exists; `IUserIdleDetector` port) to end away on input: on each `Ticked`, if `lastInput > AwaySince + 2 s` → `Return()`.
- Check: `syncapp/Checks/StepAwayAppChecks.cs` with a fake idle detector: away → fake input → returned, event pair written; away with no session → no-op; auto-after-idle off → idle 10 min does nothing; on → away starts at idle minute 5 with `AwaySince = now - 5 min`… no: **starts at the detection moment** (the design leaves retroactivity out; the minutes before count as focus).

- [ ] **Step 1: Checks** as above (RED).
- [ ] **Step 2: Implement.** The 2-second grace after `AwaySince` ignores the click that pressed the button. Resume at launch: `Return` only on input newer than the app's start.
- [ ] **Step 3: Run** syncapp → PASS; build.
- [ ] **Step 4: Commit** `Step away: button, hotkey, tray; ends on input; prompts sleep`.

### Task 4: App: tabs re-tiered (Today, Calendar, More) and the timeline only on Today and History

**Files:**
- Modify: `src/AccountabiliBuddy.App/MainWindow.axaml` (tab list and headers; the day bar `Border` gets `IsVisible="{Binding ShowsTimeline}"`), `src/AccountabiliBuddy.App/ViewModels/MainWindowViewModel.cs` (tab constants → an `enum MainTab { Today, Profiles, DayPlan, Calendar, History, Devices, More, Setup, Integrations, Settings, About, Activity }`; `ShowsTimeline => SelectedTab is Today or History`; `More` is a header-only item that expands four sub-items in the strip), `src/AccountabiliBuddy.App/Services/Search/UiSearchSource.cs` and every `SelectedTab = <const>` caller (grep `SelectedTab =`).
- Check: `syncapp/Checks/NavigationChecks.cs`: `MainTab` order; a search hit for "Exceptions"/"one-off day" opens `Calendar`; "Updates" opens `About`; `ShowsTimeline` false on Profiles.

- [ ] **Step 1: Checks** (RED).
- [ ] **Step 2: Implement.** Keep the Pages side-by-side pattern (the comment at `MainWindow.axaml:17`). "More" = a `TabItem` whose header is a toggle; when open, the four tabs show under it; when a sub-item is selected, More stays open. Rename headers only; the views keep their names (`ExceptionsView` stays a file; the header reads Calendar).
- [ ] **Step 3: Run** syncapp → PASS; UI Automation on the dev build: tab names read `Today, Profiles, Day plan, Calendar, History, Devices, More`; selecting Profiles hides the day bar.
- [ ] **Step 4: Commit** `Tabs in tiers: Today, Calendar, More; the timeline only on Today and History`.

### Task 5: App: the Today page (idle) and the day bar's away stretches

**Files:**
- Create: `src/AccountabiliBuddy.App/Views/TodayView.axaml(.cs)`, `src/AccountabiliBuddy.App/ViewModels/TodayViewModel.cs`
- Modify: `src/AccountabiliBuddy.App/Views/SessionView.axaml` → replaced by `TodayView` (delete `SessionView` only after the user's OK; until then it stays unused), `QuickStartPanel.axaml` (the task box moves to the top: "What are you doing first?" three lines = the Draft `ChecklistView` limited to 3 visible rows with "one more"; "More options" collapses profile/length/break), `ViewModels/DayBarViewModel.cs` + `Controls/DayBarControl.cs` (away stretches hatched in slate on the Focus row, tip "Away · 10:12 – 10:16 (4m)"; focused total excludes them)
- Check: `syncapp/Checks/FocusRowChecks.cs` (add: a session with an away stretch → spans: session, hatched away on top, focused total minus away)

**Interfaces:**
- Consumes: `SessionRecord.Away` (Task 2).
- Produces: `TodayViewModel { IsSessionActive, Start (QuickSessionViewModel), Focus (FocusViewModel, Task 6), DayBar }`.

- [ ] **Step 1: Check** (RED) for the away span.
- [ ] **Step 2: Implement** the view per the mockup "Today – idle": header (date, status line, "0 of … focused" until phase 4 adds the goal), start card, "Come back to" omitted (phase 5), timeline. The quick-session popup keeps its own layout but gains the task box on top too.
- [ ] **Step 3: Run** syncapp → PASS. Dev build on `seed3` data: screenshot matches the board's order (tasks → big teal → Start N min → More options).
- [ ] **Step 4: Commit** `Today page: start card first, timeline last; away stretches on the Focus row`.

### Task 6: App: Focus mode

**Files:**
- Create: `src/AccountabiliBuddy.App/Views/FocusView.axaml(.cs)`, `src/AccountabiliBuddy.App/ViewModels/FocusViewModel.cs`
- Modify: `TodayView.axaml` (swap: `FocusView` when `IsSessionActive`), `MainWindow.axaml` + `MainWindowViewModel` (`IsFocusMode => session active && !ShowEverything`; in focus mode the strip shows only Today and History as icons (tool tips), plus a "Show everything" link that sets `ShowEverything = true` for this session; it resets when the session ends), `App.axaml.cs` (`ShowMainWindow` lands on Today while a session runs).

**Interfaces:**
- Consumes: `SessionController` (state, Now), `SessionTasksService` (`ChecklistViewModel` Live), `StepAwayService`, `SessionEndFlow`.
- Produces: `FocusViewModel { TimeLeftText, PhaseText, Checklist, CurrentTaskText, CurrentSubtaskText, AwayText, StepAwayCommand, InterruptedCommand, EndSessionCommand, FocusedTodayText }`.

- [ ] **Step 1: Check** (syncapp): with a fake 25-min session at T+5 → `TimeLeftText == "20:00"`, `PhaseText == "left · Coding · until <T+25 local>"`; away → `AwayText == "Away · 0:00"` and the timer text frozen; `IsFocusMode` false once `ShowEverything` is set and true again after the next session starts.
- [ ] **Step 2: Implement** per the mockup "Focus mode": timer, phase line, End session…, the focused line (goal in phase 4), the task panel (big current task + subtask + the Live checklist + add box), Step away / Interrupted buttons (capture box in phase 5). The `ChecklistView` is reused; the big current task is `ChecklistViewModel.CurrentTaskText`/`CurrentSubtaskText`.
- [ ] **Step 3: Run** → PASS; dev build on `seed` data (running guided session): screenshot shows the collapsed rail and the panel; UI Automation presses "Step away" → "Away · 0:0x" appears; a synthetic input (`SendInput` mouse move via the probe script) ends it.
- [ ] **Step 4: Commit** `Focus mode: only the essentials while a session runs`.

### Task 7: On-screen window: quiet by default, gliding now line, Step away

**Files:**
- Modify: `src/AccountabiliBuddy.App/Services/OverlaySettings.cs` (`bool Quiet = true`), `ViewModels/OnScreenOverlayViewModel.cs` (+`IsQuiet`, `AwayText`, `ShowsAway`, `StepAwayCommand` in the Ctrl+Alt details; `PanelOpacity` 0.6 when quiet and not hovered/held/loud), `Controls/DayBarControl.cs` (`Quiet` styled property: every span in one muted slate, marks kept red, no labels; `NowFraction` set every second so the line glides), `ViewModels/DayBarViewModel.cs` (the compact timeline's `Fraction(now)` recomputed on each tick, not each redraw), `Views/SessionOverlayWindow.axaml` (the "Away · 4:12" badge; the loud moments: existing glow + chime, plus an amber edge while a classify prompt is open after plan-first (phase 2 adds plan-first; here: while any prompt is open)), `Views/SettingsView.axaml` + `SettingsViewModel` (On screen & sound: "Quiet" / "Full colour").
- Check: `syncapp/Checks/QuietOverlayChecks.cs`: default settings → `Quiet == true`; old JSON without the field → true; the view model's opacity 0.6 quiet idle, 1.0 held or loud; `DayBarControl` quiet brush for a plan span = the slate.

- [ ] **Step 1: Checks** (RED).
- [ ] **Step 2: Implement.** The glide: Avalonia redraws on `InvalidateVisual` each tick; the now line's X uses the fraction at the second, so at 200 px per 16 h it moves ~0.0035 px per second (sub-pixel; anti-aliasing makes it a slow fade between pixels). No animation API needed.
- [ ] **Step 3: Run** → PASS; dev build: capture the resting overlay (muted, 60%), the hover (bright), Ctrl+Alt (full colour, Step away button).
- [ ] **Step 4: Commit** `On-screen window: quiet by default, gliding now line, Step away`.

### Task 8: Docs, version, ship

**Files:**
- Modify: `CLAUDE.md` (project): the Roadmap paragraph for navigation, Today, Focus mode, quiet overlay; the hard rules gain Step away (pauses; ends on input; events; schema 6; auto-after-idle off by default); `AccountabiliBuddy.App.csproj` version.
- ADR: none (no store, protocol or dependency changes; Step away is a session rule the hard-rules line records).

- [ ] **Step 1:** `run-pc.sh` → ALL PASS; `dotnet build`.
- [ ] **Step 2:** package, check no session on this PC (DB copy), install, publish, push; Notepad note if the user asks.
- [ ] **Step 3: Commit** `0.1.63: redesign phase 1`.

## Not in this phase (and where it is)

Plan-first and the stacked prompt (phase 2); the review card and learn mode (phase 2); Drive sync, the link's removal, watch controls (phase 3); the goal ring, phone pickups, graph (phase 4); capture, come-backs, shutdown routine (phase 5); hyperfocus guard, comms windows (phase 2, after prompts).
