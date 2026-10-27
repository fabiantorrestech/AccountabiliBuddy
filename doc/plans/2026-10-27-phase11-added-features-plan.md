# Phase 11: added features — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** the user's added features PC 16, C 14, PC 18.1, PC 21, PC 15 (step away + side task) and C 12 + C 13 (planning stage).

**Spec:** `doc/plans/2026-10-26-feature-list-plan.md` ("Added items"). The orchestrator's choices for gaps the spec leaves open are marked *(choice)*.

## Global Constraints
- Implementer rules: `.superpowers/sdd/implementer-rules.md`. No new dependencies. No tests; verify by building. Times 12-hour.
- No escape hatches during focus (`doc/hard-rules.md`). Enforcement keeps running through step away, side tasks and planning. All time comes from `TrustedClock`.
- Material icons (ADR 32), the same icon for the same meaning on PC and phone.
- Anything new in the sync file or the watch snapshot is listed in `doc/hard-rules.md` by the orchestrator after merge. Never send task names beyond what ADR 24 allows.

## Review Focus
1. A side task or planning stretch counted as focus time, in History, the goal, Beeminder or the day bar.
2. Step away or a side task used as a way out: enforcement pauses, a wait gets shorter, or a session ends without the end-early flow.
3. The "Back to <task>?" card or the planning "Ready?" card stacks or repeats after it was answered, or shows outside a session.
4. Planning's extra time shifts the session end wrongly after a restart (checkpoint) or a step away during planning.
5. Numbering and the pin marker disagree between PC Today, the overlay, the phone and the watch for the same list.

---

### Task 1: Pinned-task marker and numbered tasks (PC 16, C 14)
**Files:**
- PC: `Views/ChecklistView.axaml:113-228` (the marker at 142-143 and 188-189), `ViewModels/ChecklistViewModel.cs` (~247-252, where `OpenGroups` is built), `ViewModels/TaskRowViewModel.cs`, `ViewModels/OnScreenOverlayViewModel.cs:462` (the folded bar's `▸`).
- Phone: `ui/TasksCard.kt:63-99`.
- Watch: `shared/WatchViews.kt:83-119` and `shared/WatchSnapshot.kt` (the running session's current-task index).

**What changes:**
- The pin marker becomes the Material `Pin` icon: `MaterialIconKind.Pin` 14×14 on the PC, `Icons.Filled.PushPin` on the phone. The watch's text line uses a 📌-free form: the number only (see below).
- Numbering *(choice)*: one sequence over the open tasks in the order shown, across category groups (1, 2, 3…). Done tasks and subtasks are not numbered. It is the same on PC Today, the Session tab, the overlay and the phone.
- The watch shows the current task as "3. <task> · done/total". This needs the current task's number in the snapshot (an int, no new text).
- [ ] Commit `PC, phone, watch: numbered tasks and a pin icon for the current task`.

### Task 2: Wrap up the day (PC 18.1)
**Files:** `Views/TodayView.axaml:50-81`, `ViewModels/ShutdownCardViewModel.cs`, `ViewModels/ShutdownTaskRow.cs`, `ViewModels/ShutdownComebackRow.cs`, `Services/ShutdownService.cs:96-131`.

**What changes:**
- **One rule for both sections:** checked = done, unchecked = carried over. Every row starts unchecked.
  - Come-backs: checked rows are ticked done, as today; unchecked rows stay open.
  - Left open: unchecked rows are carried into tomorrow's checklist; checked rows are marked done and not carried. This reverses today's `Carry`.
  - One instruction line covers both: "Tick what's done. The rest carries over."
- **Stand out more:** each section gets a header with its icon (`FlagOutline` for Come back to, `PlaylistPlus` for Left open; match `TodayView`'s 18×18 header style), a count ("3"), and a tinted card background, using an existing theme token. No new colours.
- **Categories:** Left open rows show under their category headers (the dot and name, as in `ChecklistView.axaml:132`, via `TaskCategoryBrushes.Of`), with rows that have no category last. Come-backs have no category and stay a flat list.
- [ ] Commit `PC: Wrap up the day: one tick rule, categories, sections stand out`.

### Task 3: Timeline slides away in a session (PC 21)
**Files:** `MainWindow.axaml:130-206` (`Panel.timeline`, the `.calm` style), `ViewModels/MainWindowViewModel.cs` (`IsFocusMode`).

**What changes:**
- In focus mode the timeline panel slides up out of view and gives its height back to the page.
  - Animate a `TranslateTransform` Y plus the host height, about 250 ms.
  - Leave a 6 px hover strip at the top; hovering it slides the timeline down **over** the page *(choice: it overlays, no reflow)*, and it slides away again 400 ms after the pointer leaves.
  - Outside focus mode it is as today.
- [ ] Commit `PC: the timeline slides out of the way in a session and shows on hover`.

### Task 4: Step away waits for "Back on track"; side task (PC 15)
**Files:**
- Core: `Core/Engine/SessionEngine.cs:106-141`, `Core/Domain/AwayKind.cs`, `Core/Engine/SessionHistoryBuilder.cs:53-66`.
- Services: `Services/StepAwayService.cs` (`CheckNow` 120-165, `IsBack` 169-195), `Services/ActivityRecorder.cs:177-178`.
- UI: `ViewModels/FocusViewModel.cs:156-181`, `Views/FocusView.axaml:34-47`, `ViewModels/OnScreenOverlayViewModel.cs:284-350`, `Services/SessionOverlayService.cs:279-282`, `ViewModels/HistoryViewModel.cs:565-594`, `ViewModels/DayBarViewModel.cs:832`.
- Reuse: `Core/Engine/BypassPolicy.cs` and `ViewModels/EndSessionDialogViewModel.cs`'s wait.

**Step away:**
- Input never ends a step away anymore. `IsBack` no longer calls `Return()`.
- The first input after a step away shows a "Back to <task>?" card (the current task's name, or "Back to your session?"), with **Back on track** (calls `Return()`) and **Still away**. Unanswered or "Still away", it shows again 2 minutes later.
- Focus time stays paused until Back on track. Lunch's `HoldsAway` and the phone's `BACK` action keep working (`BACK` = Back on track).

**Side task:**
- A new **Side task** button next to Step away, at the desk.
- It asks for a length: 5 / 10 / 15 min, default 5. It is a new `AwayKind.SideTask`; enforcement keeps running (it isn't a break).
- At the end: the session's chime (`SessionCueService.Play` with the focus-end sound) plus a card "Back to <task>?" with **Back on track** / **+5 min** / **End session** (the normal end-early flow, no shortcut).
- Each +5 needs a reason of at least 10 characters *(choice)*. From the 2nd +5 on, there is also a 30 s wait *(choice)* via `BypassPolicy` with `new BypassSettings(TimeSpan.FromSeconds(30), 10)`.
- Unanswered, the card stays on top and the side time keeps growing.

**Shown and recorded:**
- The overlay and Today show the side time growing in amber next to the paused focus time ("Side task · 6:12").
- History records it as an away stretch of kind SideTask, shown as "Side task" in the breakdown, never as focus.
- The reasons are DPAPI-encrypted like end-early reasons and never leave the PC.
- [ ] Commit `PC: step away waits for Back on track; side task with its own timer`.

### Task 5: Planning stage with its own bar; timers count down (C 12, C 13)
**Files:**
- PC: `Services/PlanFirstService.cs`, `ViewModels/FocusViewModel.cs:62-106,210-222`, `ViewModels/SessionOverlayViewModel.cs:96-161`, `ViewModels/OnScreenOverlayViewModel.cs:301-320`, `Services/SessionOverlayService.cs:284-289`, `Services/SessionCueService.cs`, the checkpoint.
- Sync and phone: the sync file's `running` block (`SyncCoordinator.RunningNow`, Kotlin `SyncRunning`), `ui/PcCard.kt:109-129`. Not the link (deleted in phase 4b).
- Watch: `shared/WatchSnapshot.kt`, `shared/WatchViews.kt`, `shared/PhaseAlerts.kt` and `wear/.../alerts/PhaseAlertScheduler.kt`.

**Planning:**
- Planning becomes a stage before the focus countdown, not part of it. Planning time isn't focus time.
  - The focus phase's start and end move later by the planning time used, so the "fresh countdown bar for the full session length" starts when planning ends.
  - It is kept in the checkpoint, so a restart resumes the right stage with the right ends.
  - It is recorded in History as a "Planning" stretch, not focus.
- Planning shows its own countdown bar on Today and in the overlay: the bar and timer in the planning colour, with the label "Planning".
- When planning ends, its ends are the same as today (time up, "I'm ready", first task ticked).
  - On time up: the chime and the overlay glow, plus a card "Ready?" with **Start** / **+2** / **+5** / **+15** min.
  - "I'm ready" and ticking the first task start the focus at once, with no card.
  - Unanswered, the card stays and planning time keeps counting (still not focus).
- Its length stays 7 minutes *(choice: no new setting)*.

**C 13: counting down:**
- Find every case where a session with a set length shows a count-up timer (during planning; the first "learning" session; a resumed session), and make it count down from the set length.
- A session with no set end still counts up; there's nothing to count down to.

**Phone and watch:**
- The PC's planning stage (planning ends-at) goes to the phone in the sync file's `running` block only. The link is deleted in phase 4b, so leave it out.
- The phone's PC card shows "Planning · 3 min left" with its own bar.
- The watch shows the planning countdown, and buzzes plus plays a sound at its end (a new `PhaseAlert.Kind.PlanningEnd`).
- The "Ready?" answer happens on the PC *(choice; phone and watch display only)*.
- [ ] Commit `PC, phone, watch: planning has its own bar and a Ready? card; timers count down`.

## Execution
- Task 2 and Task 3 can run together with phase 10's Task 2: their files don't overlap.
- Task 1 runs after phase 10's Task 2 merges (both edit `OnScreenOverlayViewModel.cs`).
- Then Task 4, then Task 5. Both change the session engine and the overlay, so they run one after the other.

| Task | Implementer | Reviewer |
|---|---|---|
| 1 | Sonnet medium | Sonnet high |
| 2 | Sonnet medium | Sonnet high |
| 3 | Sonnet medium | Sonnet high |
| 4 | Sonnet high | Opus medium (focus session) |
| 5 | Sonnet high | Opus medium (focus session, sync) |
