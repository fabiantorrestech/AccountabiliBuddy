# Phase 10: added bugs — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** a PC test session (to test the rest), then the user's added bugs PC 12, 13, 14, 17, 19, 20, A 2, A 3, then the Alt+Tab DIAG clean-up.

**Spec:** `doc/plans/2026-10-26-feature-list-plan.md` ("Added items") and the user's answers of 2026-10-27 (below).

## Global Constraints
- Implementer rules: `.superpowers/sdd/implementer-rules.md`. No new dependencies. No tests (project rule); verify by building. Times 12-hour.
- Nothing may weaken a running focus session the user started (escape-hatch rules, `doc/hard-rules.md`).
- Material icons only (ADR 32).

## User decisions (2026-10-27)
- **PC test session** ends any time with "End test" (no wait, no reason). Otherwise it runs like a real session (enforcement, prompts, overlay).
- **PC 12:** the Ctrl+Alt details hug the anchored edge. Left edge for the left and centre columns of the 3×3 grid, right edge for the right column. Text inside stays left-aligned.
- **A 2:** Back does nothing on the Essentials screen. Home brings it back in about 3 s unless the user opened an essential app from it. The way out stays ending the session. With LockMyPhone-Extended set up, unlocking in a session starts LockMyPhone-Extended instead of the in-app screen. Date under the time; the time shows AM/PM.
- **A 3:** the start-session card keeps today's timing (it comes back at the next repeat), but it must reliably come back after Home. Back is already blocked.

## Review Focus
1. A test session leaks into History, focus time, the goal, Beeminder, the sync file, the link or the phone's running view.
2. A test session ends Pre-Work, satisfies the work-hours guard or lights out, or offers "start another".
3. After a restart, the checkpoint resumes a test session as a real one.
4. A 3×3 drop near a screen edge on a second monitor (or with a different DPI) lands off-screen or on the wrong monitor.
5. The Essentials screen re-opens over an essential app the user opened from it, or over the phone call screen.

---

### Task 1: PC test session
**Files:** `Core/Domain/SessionState.cs` (a test flag), `Services/SessionController.cs` (`StartTest(profileId, minutes)`), `Services/QuickSessionService.cs` + `Views/QuickStartPanel.axaml` (Today → More options: "Test session", 1 min by default, 1–60, like the phone's `ui/TestSessionCard.kt`), `Services/ActivityRecorder.cs:160-215` (no `SessionEvent`s for a test), `Services/Sync/SyncCoordinator.cs` (`RunningNow()` 256-265 and the records at ~200-223: a test is never sent), `Services/Link/LinkSessionHost.cs` (`BuildState()` ~369, `IsActive` 223/230: not active for a test), `Services/SessionCheckpointService.cs:66-70` (no checkpoint; a test never resumes after a restart), `Services/SessionTasksService.cs` (~262: don't archive), `QuickSessionService.OnSessionCompleted` (~332: no "start another"), and the Pre-Work / work-hours guard / lights out / goal / Beeminder consumers (a test never counts as work).
- Enforcement, prompts, cues and the overlay run as in a real session. The overlay's profile line reads "Test session".
- "End test" ends it at once; Today's session card shows it in place of End early. A test can't start while a real session runs, and the Start buttons hide during one.
- Record it in `doc/hard-rules.md` (the orchestrator does this after merge).
- [ ] Commit `PC: test session (never counted, never in History, never synced)`.

### Task 2: Overlay fixes (PC 12, 13, 14)
**Files:** `Views/SessionOverlayWindow.axaml` (+ `.axaml.cs`), `ViewModels/OnScreenOverlayViewModel.cs`, `ViewModels/SessionOverlayViewModel.cs`, `Services/SessionOverlayService.cs`.
- **PC 14 (cause confirmed):** `AccentBrush` (`SessionOverlayViewModel.cs:189-195`) is recomputed only on the 1 s tick. Raise it from `SetDragReady` / `ApplyQuiet` too.
- **PC 13:** first find and fix the snap-back.
  - Suspect: the 400 ms `MoveSettleDelay` fires during a mid-drag pause. That sets `_isTemporary`, the "Moved for now" row makes the window taller, and `SizeChanged` → `ApplyAnchor()` jumps back to the stale `_anchor` (`axaml.cs:122-187`).
  - Also check `SlideTowardTarget`, `MoveToDefaultCorner` and `UpdateTaskbarLift`, which run without a `_movedByUser` guard.
  - Then the anchoring. On drop, pick a 3×3 zone of the overlay's monitor work area from the overlay's centre. Corners anchor at that corner, edge-middles at the edge's middle, and the centre zone grows evenly both ways. Store the zone plus the offset from the anchor point, so the folded and expanded sizes share one anchor.
  - Always clamp to the monitor's work area. Per-monitor DPI is respected.
  - Keep today's snap-to-default-corner and the "Moved for now / Keep" flow.
- **PC 12:** the details panel and its children (Goal, profile, timer line, timers, task list, noise, footer) align to the anchored edge: Left for the left and centre columns, Right for the right column. The text stays left-aligned.
  - Cause: fixed-width (200) children in a wider stretched panel (the footer is wider), and the timer line's `HorizontalAlignment="Center"`.
  - **Use the width (user, 2026-10-27):** the details items (Goal bar, profile, timers, task list, noise controls) fill the details panel's width instead of a fixed 200. Their minimum is the bar length (200) and the whole panel's maximum about 360 DIPs, so long task names wrap or trim and never stretch the popup across the screen. The folded bar keeps its length. Margins/padding stay even on both sides.
- [ ] Commit `PC: overlay keeps its place (3x3 anchoring), details hug its edge, timer colour follows Ctrl+Alt at once`.

### Task 3: Sidebar and category picker (PC 17, 19, 20)
**Files:** `Views/MainWindow.axaml` (+ `.axaml.cs`), `ViewModels/MainWindowViewModel.cs:123-145`, `Views/ChecklistView.axaml:235-242`, `ViewModels/ChecklistViewModel.cs:273-308`.
- **PC 19:** "More" toggles on every click. Suspected cause: `OnSelectedTabChanged` resets `SelectedTab` inside the change callback, so the TabControl keeps "More" selected and a second click raises nothing. Fix it by posting the reset through `Dispatcher.UIThread.Post` (as `MayLeaveTab` does, `MainWindow.axaml.cs:167`) or by handling the More item's click; pick one and note why.
- **PC 20:** the sidebar's tab strip scrolls (a `ScrollViewer`, vertical only, visible only when needed) when the window is short. The logo stays docked at the top, and the noise player and focus toggle at the bottom.
- **PC 17:** the new-task picker shows every category (all five and more) at once. Reproduce it from the code path first; the suspect is the long-lived `MenuFlyout` whose `ItemsSource` is replaced. If so, rebuild its items when it opens (the per-row flyouts work).
- [ ] Commit `PC: sidebar More toggles every click and scrolls when short; new-task picker shows every category`.

### Task 4: Phone Essentials and start card (A 2, A 3)
**Files:** `essentials/EssentialsActivity.kt`, `essentials/EssentialsLaunch.kt`, `essentials/EssentialsStore.kt` (read only), `nudge/NudgeActivity.kt`, `nudge/Takeover.kt`.
- **A 2:**
  - Back does nothing during a session: replace `BackHandler(onBack = onCancel)` (~277) and the IME effect (~280) where they cancel.
  - On `onStop` without an essential app having been opened from the screen, re-open it after about 3 s while the session still runs. Use `Takeover.startFromBackground` and the same skip rules: phone locked or screen off, an emergency, an allowed app in front, a call (`TelephonyManager` call state, no new permission if avoidable; otherwise skip the call check and note it).
  - With `useLockMyPhone` on, `onUnlock` (`EssentialsLaunch.kt:34-47`) sends the existing LockMyPhone-Extended start broadcast instead of the in-app screen.
  - Clock: `h:mm a` with the date under it (`EEEE, MMMM d`), `Locale.US`, 12-hour always.
- **A 3:**
  - After Home, the unanswered card must come back at the next repeat. Check that `Takeover.maybeShow` (`lastShown`, line ~63) and `NudgeRules.due` re-show a card that was left without an answer.
  - Also check `singleTask`: a backgrounded, unfinished NudgeActivity is brought back to the front, not left behind.
  - Find which prompt the user means. There are no "last warning" tiers in the code. Check the work-start card, the goal-bonus card and `notify/WorkStartNudge.kt`, and fix every one that can be lost after Home. Report what "sometimes" was.
- Record the firmer Essentials rule in `doc/hard-rules.md` (the orchestrator does this after merge).
- [ ] Commit `Phone: Essentials can't be swiped away in a session and uses LockMyPhone-Extended; start card always comes back`.

### Task 5: Remove the Alt+Tab DIAG logging (after the user confirms PC 7 with the test session)
**Files:** `Platform.Windows/Activity/DesktopScanner.cs` (72, 87, 95, 104, 119, 127, 153-159) and `WinEventActivityWatcher.cs` (136, 151, 197, 244, 251, 279, 295, 324-333): every `// DIAG (Task 4, remove)` line, `DiagWrite`, `LastDiagScan` and `_diag*`.
- Done by the orchestrator, inline.
- [ ] Commit `PC: remove the Alt+Tab diagnostics`.

## Execution
Tasks 1, 3 and 4 touch disjoint files and run in parallel. Task 2 runs after Task 1 merges, because Task 1 sets the overlay's "Test session" line. Task 5 waits for the user.

| Task | Implementer | Reviewer |
|---|---|---|
| 1 | Sonnet high (many files, session logic) | Opus medium (focus session) |
| 2 | Sonnet high (window maths) | Sonnet high |
| 3 | Sonnet medium | Sonnet high |
| 4 | Sonnet medium | Opus medium (enforcement) |
