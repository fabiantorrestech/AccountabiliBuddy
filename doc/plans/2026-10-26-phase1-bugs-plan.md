# Phase 1 (bugs) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Tasks 3 and 4 also use superpowers:systematic-debugging.

**Goal:** Fix the eight bugs in Phase 1 of the user's feature & bugfix list.

**Architecture:** Small, independent fixes in the Avalonia app (`src/AccountabiliBuddy.App`) and the Win32 adapters
(`src/AccountabiliBuddy.Platform.Windows`). The wake-time estimate also touches Core (`SleepSchedule`, the sync file)
and the Android phone app, with a new ADR.

**Tech Stack:** .NET 10, C#, Avalonia 12.1.4, CsWin32, CommunityToolkit.Mvvm; Kotlin (Android phone app, `android/shared`).

**Spec:** `doc/plans/2026-10-26-feature-list-plan.md` (decisions table, Phase 1). Item numbers (PC 3.1, …) are the
user's note's numbering.

## Global Constraints

- No tests (project rule: "No tests unless asked"). Verification = the build plus the manual checks listed per task.
- Build: `dotnet build AccountabiliBuddy.sln` → `Build succeeded`, 0 errors, and no new warnings in touched files.
- Android build (Task 8 only): `cd android && JAVA_HOME="C:/Program Files/Android/Android Studio/jbr" ./gradlew assembleDebug` → `BUILD SUCCESSFUL`.
- Manual checks run on the dev app: `dotnet run --project src/AccountabiliBuddy.App` (dev data, title shows "(dev data)").
- Times shown to the user: 12-hour with AM/PM.
- Core has no UI or OS dependencies; Windows-only code goes in Platform.Windows behind a `Core/Ports` interface.
- One public type per file; file-scoped namespaces; match the surrounding comment density and naming.
- No new dependencies. No refactoring outside what each task names.
- Stage new files with `git add`. Commit only when the user has OKed commits for this phase; message ends with the
  attribution lines from the session's system reminder.
- When a task's checks pass: tick that item's checkbox in the user's note (see the spec's Source line) and add a row
  to the AI_Agents_Hub project log's Timeline.

## Review Focus

1. **Spare key page reached another way** (Back from Done, reopening the migration assistant with a key pending):
   it must show the *pending* key, never make a second one, so the copy on paper stays right. → Task 3, Step 4.
2. **Session ends while the window is hidden in the tray:** nothing may cover the app being used. A taskbar button
   appears and flashes, and clicking it opens Today. → Task 5, Step 5.
3. **Wake estimate earlier than bedtime or too little data** (a 5:30 AM estimate after a 6:00 AM bedtime; 2 alarms
   on record): use bedtime + goal. → Task 8b, Step 3.
4. **Overlay hint left behind:** Ctrl+Alt released, the pointer leaves, or the overlay hides → the hint goes at once and
   never takes focus. → Task 7, Step 5.
5. **Alt+Tab fix over-filters:** a real switch (Alt+Tab released on another app, a click on another window) is still
   judged within about 1 s. → Task 4, Step 6.

---

### Task 1: Session slider in 1-minute steps, Shift for 5 (PC 3.1)

**Files:**
- Modify: `src/AccountabiliBuddy.App/ViewModels/QuickSessionViewModel.cs` (`FineAdjust`, `SliderStep`, `SliderMinutes` setter, `NudgeFocus`)
- Modify: `src/AccountabiliBuddy.App/Views/QuickStartPanel.axaml.cs`, `src/AccountabiliBuddy.App/Views/QuickStartPanel.axaml:73-77`
- Modify: `src/AccountabiliBuddy.App/Views/QuickSessionWindow.axaml.cs` (doc comment, Shift and arrow handling, pointer handler)

**Interfaces:**
- Produces: `QuickSessionViewModel.ShiftHeld` (bool, replaces `FineAdjust`); `SliderStep => ShiftHeld ? CoarseStep : FineStep`; `NudgeFocus(int direction, bool coarse)`.

Root cause: Shift is tracked only by the quick-session popup's window, so the Today page's copy of the panel never sees it. The default step is also the wrong way round.

- [ ] **Step 1:** In `QuickSessionViewModel`, rename `FineAdjust` → `ShiftHeld` ("True while Shift is held: the slider and arrow keys move in 5-minute steps; 1-minute otherwise"). Make `SliderStep`, the `SliderMinutes` setter and `NudgeFocus` use `ShiftHeld ? CoarseStep : FineStep`. Rename `NudgeFocus`'s `fine` parameter to `coarse`.
- [ ] **Step 2:** In the `QuickStartPanel` constructor, add tunnel handlers so every host gets Shift: `PointerPressed` and `PointerMoved` set `ShiftHeld` from `e.KeyModifiers`; `KeyDown`/`KeyUp` of `Key.LeftShift`/`Key.RightShift` set and clear it. Same idiom as the popup's current handlers.
- [ ] **Step 3:** In `QuickSessionWindow.axaml.cs`, remove the pointer handlers (now in the panel). Keep the Shift key handling, now setting `ShiftHeld`. Arrows: `NudgeFocus(±1, coarse: shift)`. Update the class doc ("←/→ change the length by 1 minute (Shift: 5) … Holding Shift makes the slider move in 5-minute steps").
- [ ] **Step 4:** In `QuickStartPanel.axaml`, the comment becomes "1-minute steps; 5-minute while Shift is held", and the slider tooltip becomes "Hold Shift for 5-minute steps".
- [ ] **Step 5: Build.** Expected: succeeded.
- [ ] **Step 6: Manual check** on Today → More options *and* in the quick-session popup (Ctrl+Alt+… shortcut). A plain drag moves 25 → 26 → 27. Holding Shift while dragging snaps to 25 → 30 → 35, also when Shift was pressed before the window had focus. ← / → change by 1, Shift+← / → by 5.
- [ ] **Step 7: Commit** (`Session slider: 1-minute steps, Shift for 5, on Today too`).

### Task 2: Enter answers the distraction prompt (PC 4)

**Files:**
- Modify: `src/AccountabiliBuddy.App/ViewModels/ClassifyPromptViewModel.cs` (new command)
- Modify: `src/AccountabiliBuddy.App/Views/ClassifyPrompt.axaml:36`, `src/AccountabiliBuddy.App/Views/ClassifyPrompt.axaml.cs`
- Modify: `src/AccountabiliBuddy.App/Views/ClassifyStackPrompt.axaml:38`, `src/AccountabiliBuddy.App/Views/ClassifyStackPrompt.axaml.cs`

User (2026-10-26): "On track, done, exemption should be a 1 option pill style toggle." The single prompt's answer
becomes one segmented pill toggle, **Distracting | On track (this session) | Exempt (always)** (one selected, like the
stacked prompt's rows), with one **Done** button. Enter in the reason box = Done.

**Interfaces:**
- Produces: `public enum ClassifyChoice { Distracting, OnTrack, Exempt }` (`src/AccountabiliBuddy.App/ViewModels/ClassifyChoice.cs`); `ClassifyPromptViewModel.Choice` (observable, default `OnTrack`), bool helpers `IsDistracting`/`IsOnTrack`/`IsExempt` for the toggle bindings (same pattern as `ClassifyStackRowViewModel`), and `DoneCommand` (`[RelayCommand] private void Done()`: Distracting → `MarkDistracting()`, OnTrack → `MarkOnTrack()`, Exempt → `AddExemption()`).
- Removed: the "Distracting" / "On track (this session)" buttons, the "Add exemption to profile…" link and `IsExemptionOpen`/`ToggleExemptionCommand` (the profile checkboxes show while Exempt is selected).

- [ ] **Step 1:** View model as above; the reason placeholder reads "Why? Needed for On track and Exempt (one word is enough)", as in the stacked prompt.
- [ ] **Step 2:** `ClassifyPrompt.axaml`: three `ToggleButton`s in a pill group (rounded outer corners, the selected one in the accent colour), bound to the helpers; the "also in" profile checkboxes visible while `IsExempt`; one `Done` button (accent). Match `ClassifyStackPrompt.axaml`'s toggles so both prompts look alike.
- [ ] **Step 3:** Name the reason boxes (`x:Name="ReasonBox"` in both prompts). In each code-behind, handle `ReasonBox.KeyDown`: on `Key.Enter`, run `DoneCommand` if it can execute and set `e.Handled = true` (follow `CapturePopup.axaml.cs`'s Enter idiom). Only the reason box.
- [ ] **Step 4: Build.** Expected: succeeded.
- [ ] **Step 5: Manual check.** Start a session, open an unknown site, click into the prompt: On track is preselected; type a reason, press Enter → answered On track. Pick Exempt: profile checkboxes appear; reason + Enter → exemption added. Pick Distracting + Enter (no reason) → blocked. On track or Exempt with an empty reason: Enter shows the existing "reason needed" error and the prompt stays. In the stacked prompt, Enter = Done.
- [ ] **Step 5: Commit** (`Enter answers the distraction prompt`).

### Task 3: Spare key page shows the key (PC 5.1, the bug part of C 5)

**Files:**
- Modify: `src/AccountabiliBuddy.App/ViewModels/SetupAssistantViewModel.cs` (`OnStepChanged` or the `Step` property's change hook; `NextAsync` case `ColdTurkeyKeys`; `VisibleSteps`)

**Interfaces:**
- Consumes: `ColdTurkeyKeysService.PendingSpareKey` (string?), `NewSpareKeyAsync()` (Task<string>), `HasSpareKey`.

Likely cause (confirm first): `SpareKey` is set only in `NextAsync` when leaving `ColdTurkeyKeys` with a block ticked. The migration assistant always inserts the `SpareKey` page into `VisibleSteps()`, so the page can be reached with `SpareKey == null` (Back from Done after "Finish", or no block ticked), and it shows an empty key.

- [ ] **Step 1: Reproduce.** Dev app with Cold Turkey installed and no spare key confirmed: 5 taps on the About logo → migration assistant → "Cold Turkey: let AccountabiliBuddy hold the keys" → Next/Finish, and Back. Note which path shows an empty key. If none does, follow systematic-debugging before changing anything, and report.
- [ ] **Step 2: Fix.** When `Step` becomes `AssistantStep.SpareKey` and `SpareKey` is null, set `SpareKey = Keys.PendingSpareKey ?? await Keys.NewSpareKeyAsync()` (async from the property-change hook via a fire-and-forget helper that matches the file's existing pattern). Keep `NextAsync`'s own assignment.
- [ ] **Step 3: Fix the page list.** In `VisibleSteps()` for the migration assistant, insert `SpareKey` only when a Cold Turkey row is ticked (`ColdTurkeyRows.Any(row => row.IsUsed)`), as `NextAsync` does. Then the step count matches where Next really goes.
- [ ] **Step 4: Manual check (Review Focus 1).** Each path from Step 1 now shows a key. Closing and reopening the migration assistant shows the *same* key (pending), not a new one. Typing it back confirms it, and the Today card disappears.
- [ ] **Step 5: Build, commit** (`Migration assistant: the spare key page always shows the key`).

### Task 4: Alt+Tab no longer judges a window behind the current one (PC 7)

**Files:**
- Investigate: `src/AccountabiliBuddy.Platform.Windows/Activity/DesktopScanner.cs` (`IsEligible`, `ShellWindowClasses`), `Activity/WinEventActivityWatcher.cs` (`Rescan`)
- Likely modify: `DesktopScanner.cs`

User's report: the browser (on an unknown site's tab) was behind other windows, not minimized and not visible. Alt+Tab brought up the distraction prompt for it without switching to it.

Hypotheses to test in order:
1. While the switcher is up, the scanner's front-to-back walk takes a Windows switcher surface as the top window (classes such as `XamlExplorerHostIslandWindow`, `MultitaskingViewFrame`, `ForegroundStaging`, `TaskSwitcherWnd`; process explorer / ShellExperienceHost). It then takes the browser when that surface goes, or Windows' peek briefly reorders windows.
2. A foreground flicker during the switch puts the browser in front for under a second.

- [ ] **Step 1: Diagnose.** Temporarily log each `Rescan` snapshot (time, monitor, window id, process, class name; no titles) to the dev log behind a `#if DEBUG` block.
- [ ] **Step 2: Reproduce with the user.** Ask the user to Alt+Tab past the browser and release on another app, on the dev app. Collect the log.
- [ ] **Step 3: Root cause** from the log; write it in the task report.
- [ ] **Step 4: Fix at the cause.** If hypothesis 1: add the switcher classes to `ShellWindowClasses` and, while one of them is the foreground window, skip the scan, keeping the last snapshots (`Win32OverlayWindowControl.ShellProcesses` lists the shell processes already known). If hypothesis 2: judge a new top window only after it has stayed on top for 400 ms (one timer in `WinEventActivityWatcher`, reset on every relevant event).
- [ ] **Step 5:** Remove the diagnostic logging.
- [ ] **Step 6: Manual check (Review Focus 5).** Alt+Tab past the browser and release elsewhere → no prompt. Alt+Tab *to* the browser → prompt as before, within about 1 s. A click on another window is judged as before.
- [ ] **Step 7: Build, commit** (`Alt+Tab no longer judges the windows it passes over`).

### Task 5: Session end doesn't steal focus; the taskbar icon flashes (PC 9)

**Files:**
- Create: `src/AccountabiliBuddy.Core/Ports/IWindowAttention.cs`
- Create: `src/AccountabiliBuddy.Platform.Windows/Display/WindowsWindowAttention.cs`
- Create: `src/AccountabiliBuddy.App/Services/StartNudgeReason.cs`
- Modify: `src/AccountabiliBuddy.Platform.Windows/NativeMethods.txt` (add `FlashWindowEx`)
- Modify: `src/AccountabiliBuddy.App/Services/StartNudgeService.cs` (`Due` → `EventHandler<StartNudgeReason>`, `CheckNow`, `Nudge`)
- Modify: `src/AccountabiliBuddy.App/App.axaml.cs` (the `_startNudge.Due` handler near line 983; new `ShowMainWindowQuietly()` next to `ShowMainWindow()`)

**Interfaces:**
- Produces: `public interface IWindowAttention { void Flash(nint windowHandle); }` ("Asks for attention without taking focus: the taskbar button flashes until the window is brought forward.")
- Produces: `public enum StartNudgeReason { WorkStarted, AppStarted, Back, SessionEnded, Repeat }`.
- Produces: `StartNudgeService.Due` is `event EventHandler<StartNudgeReason>?`.

Root cause: when a session ends inside a work part, `StartNudgeService.CheckNow` sees work "start" again (`_wasInWork` was reset while the session ran) and raises `Due`. The app then calls `ShowMainWindow()`, which calls `Activate()`. The repeat nudge (every few minutes) does the same while you type.

- [ ] **Step 1:** `WindowsWindowAttention.Flash`: `FlashWindowEx` with `FLASHW_TRAY | FLASHW_TIMERNOFG`. Windows flashes the button orange until the window comes to the front.
- [ ] **Step 2:** In `StartNudgeService`, remember whether the last tick had a session running. When work becomes current right after a session, raise `SessionEnded`. The other occasions raise `WorkStarted`, `AppStarted` (the first check after launch), `Back` and `Repeat`.
- [ ] **Step 3:** In `App.axaml.cs`, create `new WindowsWindowAttention()` where the other Win32 adapters are made, and add `ShowMainWindowQuietly()`. It selects the Today tab. If the window isn't visible, it shows it **minimized** (`WindowState = Minimized` before `Show()`); if it's visible (behind other windows or minimized), its place stays as it is. It never calls `Activate()` or `RequestFocus()`, then calls `IWindowAttention.Flash` with the window's platform handle. `SessionEnded` and `Repeat` use it; the other reasons keep `ShowMainWindow()` plus the task box focus. Phase 2 (PC 1) reworks the nudge.
- [ ] **Step 4: Build.** Expected: succeeded. *Uncertain:* whether Avalonia 12 shows a window minimized without a flash of it on screen. If it does show, try `ShowActivated = false` with `Minimized` and report what was seen.
- [ ] **Step 5: Manual check (Review Focus 2).** During a work part, start a 1-minute session from Today. Type in Notepad, with the app's window (a) minimized and (b) hidden in the tray. When the session ends, nothing appears over Notepad and typing continues. The taskbar button flashes orange; clicking it opens Today. Launching the app during a work part still opens Today with the task box as before.
- [ ] **Step 6: Commit** (`Session end: flash the taskbar instead of taking focus`).

### Task 6: The overlay's current task is no longer cut off (PC 6.1.1)

**Files:**
- Modify: `src/AccountabiliBuddy.App/ViewModels/OnScreenOverlayViewModel.cs` (next to `SubtaskMaxLines`, line ~401; its change notification at ~153)
- Modify: `src/AccountabiliBuddy.App/Views/SessionOverlayWindow.axaml:61-62`

**Interfaces:**
- Produces: `public int TaskMaxLines => ShowsDetails ? 0 : 1;` (0 = no limit), notified wherever `SubtaskMaxLines` is.

- [ ] **Step 1:** Add `TaskMaxLines`. The task line's TextBlock gets `TextWrapping="Wrap" MaxLines="{Binding TaskMaxLines}"`, keeping `TextTrimming="CharacterEllipsis"`. At rest it stays one line with "…"; while Ctrl+Alt is held all of it shows, as CLAUDE.md already describes.
- [ ] **Step 2: Build.**
- [ ] **Step 3: Manual check.** A task named with 80+ characters: at rest one line with "…"; with Ctrl+Alt held the whole name wraps under "1/3 ▸"; the checklist below still wraps its titles; the window grows and shrinks without jumping corners.
- [ ] **Step 4: Commit** (`On-screen window: the whole task while Ctrl+Alt is held`).

### Task 7: Overlay hints drawn above the overlay (PC 6.1.2)

Run after Task 6 (same XAML file).

**Files:**
- Create: `src/AccountabiliBuddy.App/Views/OverlayHint.cs` (attached property `Text`)
- Create: `src/AccountabiliBuddy.App/Views/OverlayHintWindow.axaml`, `.axaml.cs`
- Modify: `src/AccountabiliBuddy.App/Views/SessionOverlayWindow.axaml` (every `ToolTip.Tip` → `views:OverlayHint.Text`, 12 uses), `SessionOverlayWindow.axaml.cs`
- Modify: `src/AccountabiliBuddy.App/Services/SessionOverlayService.cs:305` (keep the hint on top after the overlay)

**Interfaces:**
- Produces: `public static class OverlayHint { public static readonly AttachedProperty<string?> TextProperty; GetText/SetText }`.
- Produces: `OverlayHintWindow.ShowAt(string text, PixelPoint screenPoint)`, `Hide()`, `KeepOnTop()`.
- Consumes: `IOverlayWindowControl.SetClickThrough(nint, bool)`, `KeepOnTop(nint)`.

Root cause: `SessionOverlayService` calls `KeepOnTop()` every tick (`SetWindowPos(HWND_TOPMOST)`). That puts the overlay above its own tooltip popups, so they always end up behind it.

- [ ] **Step 1:** `OverlayHintWindow`: `Topmost`, `ShowActivated="False"`, `ShowInTaskbar="False"`, no decorations, `SizeToContent`, dark rounded border, white 11 px text, max width 260, wraps. Click-through once opened (`SetClickThrough(handle, true)`).
- [ ] **Step 2:** `SessionOverlayWindow` owns one hint window. A pointer resting 400 ms over an element with `OverlayHint.Text` shows the hint at the cursor + (16, 20) px, kept on the cursor's screen, and it follows the pointer while it stays over that element. Pointer exit, pointer press, Ctrl+Alt released (`_expanded` false) and the overlay hiding or closing all hide it.
- [ ] **Step 3:** In `SessionOverlayService` (line ~305), after `_window.KeepOnTop()`, keep the hint on top when it's visible.
- [ ] **Step 4:** Replace the overlay's 12 `ToolTip.Tip` with `views:OverlayHint.Text`; `ChecklistView` inside the overlay keeps its tooltips (they are the main window's too). *Note:* if `ChecklistView`'s tooltips also hide behind the overlay, report it rather than changing `ChecklistView`.
- [ ] **Step 5: Manual check (Review Focus 4).** Hold Ctrl+Alt, hover Step away, the QR button and the volume slider: the hint shows above the overlay, next to the cursor, and follows it. Releasing Ctrl+Alt, moving off or clicking hides it. Typing in another app is never interrupted. No hint stays on screen after the overlay hides.
- [ ] **Step 6: Build, commit** (`On-screen window: our own hints, above it`).

### Task 8: Estimated wake time from past alarms, then measured sleep (PC 11.1)

Three parts in order: 8a by the orchestrator, then 8b and 8c (in parallel once 8a's shapes are set).

#### 8a: ADR 40 and CLAUDE.md

**Files:**
- Create: `doc/adr/0040-estimate-the-wake-time-from-past-alarms.md` (with the `adr` skill; regenerate the index)
- Modify: `CLAUDE.md` (the device sync list in Hard rules and the sleep wake rule in the schema/prefs text)

- [ ] **Step 1:** ADR 40, Accepted, amends ADR 23 and 33. With "wake by alarm" and no alarm that morning: the phone's estimate for that kind of morning (Workdays / Off-Days, by `plan.Workdays.Contains(morning.DayOfWeek)`). The estimate is the median of the wake-up app's past alarms that fell in the morning window (the last 8 mornings of that kind, at least 3), else the median of Health Connect's measured wake times (same rule), else bedtime + goal. Only the two times leave the phone, in its sealed sync file as `wakeEstimate: { workdays: "HH:mm"?, offDays: "HH:mm"? }`. One-off days follow their weekday.
- [ ] **Step 2:** CLAUDE.md: add "a phone's estimated wake time per kind of night (ADR 40: only the two times)" after "a phone's next alarm, ADR 23". Change "wake = the wake-up alarm, else bedtime + the sleep goal" to "wake = the wake-up alarm, else the phone's estimate for that kind of morning (ADR 40), else bedtime + the sleep goal".
- [ ] **Step 3: Commit** (`ADR 40: estimate the wake time from past alarms`).

#### 8b: PC side

**Files:**
- Create: `src/AccountabiliBuddy.Core/Sync/SyncWakeEstimate.cs`
- Create: `src/AccountabiliBuddy.App/Services/Sync/PhoneWake.cs` (same shape as `PhoneBedtime.Of`)
- Modify: `src/AccountabiliBuddy.Core/Sync/SyncFile.cs` (optional `SyncWakeEstimate? WakeEstimate = null` after `SmartBedtime`, `WhenWritingNull`)
- Modify: `src/AccountabiliBuddy.Core/Engine/SleepSchedule.cs` (ctor param, `WakeOf`, class doc)
- Modify: `src/AccountabiliBuddy.App/Services/SleepSettings.cs` (`ToSchedule`), `Services/SleepSettingsService.cs` (`PhoneWake`, `SetPhoneWake`), `App.axaml.cs:499` (set it beside `SetPhoneBedtime`), `ViewModels/TonightWakeViewModel.cs:118,127,128` (pass it)

**Interfaces:**
- Produces: `public sealed record SyncWakeEstimate(string? Workdays, string? OffDays);` (`"HH:mm"`, invariant; null = not enough data).
- Produces: `SleepSchedule(..., Func<DateOnly, TimeOnly?>? wakeGuess = null)`, the estimated wake for a morning.
- Produces: `SleepSettings.ToSchedule(DayPlan? plan = null, DateTime? phoneAlarm = null, SyncSmartBedtime? phoneBedtime = null, SyncWakeEstimate? phoneWake = null)`.
- Produces: `SleepSettingsService.PhoneWake` (SyncWakeEstimate?), `SetPhoneWake(SyncWakeEstimate?)`, which raises `PhoneAlarmChanged` like `SetPhoneBedtime`.

- [ ] **Step 1:** Add `SyncWakeEstimate` and the `SyncFile` field. Older files without it still read.
- [ ] **Step 2:** `SleepSchedule.WakeOf`: in the `once is null && times.WakeByAlarm` branch, use `_wakeGuess?.Invoke(morning)` when it's after `bed` and before noon, else `bed + _goal`.
- [ ] **Step 3 (Review Focus 3):** `ToSchedule` builds `wakeGuess` from `phoneWake` by `plan.Workdays.Contains(morning.DayOfWeek)`, parsing `"HH:mm"`; null/unparsable → null. Expected: bedtime 12:00 AM, Workdays estimate 6:45 AM → wake 6:45 AM; estimate 5:30 AM with bedtime 6:00 AM → bedtime + goal; no phone → bedtime + goal (unchanged).
- [ ] **Step 4:** Wire `PhoneWake.Of(receivedDevices.Devices)` next to `PhoneBedtime.Of` in `App.axaml.cs`, and pass `_sleep.PhoneWake` in `SleepSettingsService` and `TonightWakeViewModel`.
- [ ] **Step 5: Build.**

#### 8c: Phone side

**Files:**
- Create: `android/app/src/main/java/com/accountabilibuddy/companion/sync/WakeEstimate.kt`
- Modify: `android/shared/src/main/java/com/accountabilibuddy/shared/sync/SyncFile.kt` (`wakeEstimate: SyncWakeEstimate? = null`, new `data class SyncWakeEstimate(val workdays: String?, val offDays: String?)`), `SyncFileJson.kt` (write/read `"wakeEstimate"` like `"smartBedtime"`)
- Modify: `android/app/src/main/java/com/accountabilibuddy/companion/sync/WakeAlarm.kt` (`KEPT` 30 → 60)
- Modify: `android/app/src/main/java/com/accountabilibuddy/companion/sync/SyncCoordinator.kt` (a `wakeEstimate` provider beside `smartBedtime`), `CompanionApp.kt` (wire it), `engine/SleepSchedule.kt` (same rule as 8b, so the phone shows the same wake)

**Interfaces:**
- Produces: `object WakeEstimate { fun of(alarms: List<LocalDateTime>, measuredWakes: List<LocalDateTime>, kindOf: (LocalDate) -> NightKind): Map<NightKind, LocalTime> }`, with the median of the last 8 mornings per kind and at least 3 needed; alarms first, then measured wakes.

- [ ] **Step 1:** `WakeEstimate.of` as above. Alarms = the wake-up app's (pinned, else learned) past sightings in the morning window (`WakeAlarm.inMorningWindow`), the earliest per morning. Measured wakes = the ends of `state.slept` sessions. `kindOf` = `SmartBedtime.kindOf(morning, plan.workdays)`.
- [ ] **Step 2:** Raise `WakeAlarm.KEPT` to 60, so 8 mornings of each kind fit beside calendar sightings.
- [ ] **Step 3:** Put the estimate in the phone's sync file (`"HH:mm"`), and use it in the phone's `SleepSchedule.wakeOf` the same way as 8b Step 2.
- [ ] **Step 4: Android build.** Expected: `BUILD SUCCESSFUL`.
- [ ] **Step 5: Manual check (AVD or the user's Pixel 7, ask first).** With no alarm set for tomorrow and 3+ past weekday alarms around 6:45 AM, the phone's and the PC's Today show wake ≈ 6:45 AM on the workday morning, not bedtime + 8 h. With the phone's file absent, the PC shows bedtime + goal.
- [ ] **Step 6: Commit** (`Wake time: estimated from past alarms, then measured sleep (ADR 40)`).

### Task 9: Wrap-up (orchestrator)

- [ ] **Step 1:** Full build of both apps; spot-check the dev app's Today, a session, the overlay and the migration assistant.
- [ ] **Step 2:** Tick PC 3.1, PC 4, PC 5.1, PC 7, PC 9, PC 6.1.1, PC 6.1.2, PC 11.1 in the user's note (C 5 stays open until the Phase 2 "new spare key" part). Update the AI_Agents_Hub project log (Phase 1 → `[[~finished]]`, Timeline row). Update `handoffContext/handoff.md`.
- [ ] **Step 3:** Ask about releasing (PC version bump + `publish-apks.ps1`) and pushing.

## Execution waves (subagents: orchestrator Opus 5.5 high, implementers Opus 5.5 medium)

Each implementer gets its own git worktree (`isolation: "worktree"`), so parallel `dotnet build`s don't lock each
other's `obj/`. The orchestrator merges each finished branch into `main` in order and rebuilds.

| Wave | In parallel | Why together |
|---|---|---|
| A | Task 1, Task 2, Task 3, Task 6 → Task 7 (one agent) | No shared files |
| B | Task 5; Task 8a (orchestrator) | Task 5 and 8b both touch `App.axaml.cs` |
| C | Task 8b, Task 8c | After 8a; PC and phone share only the JSON shape |
| — | Task 4 | Orchestrator with the user (needs a live Alt+Tab reproduction) |
| D | Task 9 | Last |

Manual checks are collected per wave and given to the user in one list.
