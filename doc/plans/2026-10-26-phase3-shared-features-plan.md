# Phase 3 (phone, watch and shared features) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** A 1, C 2, C 8, C 10 of the user's list: a phone test session, the noise player's headphone lock and stop-on-disconnect, task categories / parking / dragging, and the flexible lunch additions.

**Architecture:** PC work in `src/AccountabiliBuddy.App` (+ Core records where the sync file changes); phone work in `android/app`, shared format in `android/shared`, watch in `android/wear`. The sync file grows two optional fields (lunch `editedAt`, checklist categories); old files still read.

**Tech Stack:** .NET 10 / Avalonia 12.1.4; Kotlin, Compose, Wear Compose, Wearable Data Layer.

**Spec:** `doc/plans/2026-10-26-feature-list-plan.md` (decisions table).

## Global Constraints

- Binding rules for implementers: `.superpowers/sdd/implementer-rules.md`.
- Privacy: task names, their categories (name + colour) and tick times go only to the user's paired devices (ADR 24); the test session never leaves the phone; noise lock state is device-local (the watch only shows it).
- Sync file changes are optional fields; a file without them reads as before; both sides (C# `Core/Sync`, Kotlin `android/shared/.../sync`) use the same camelCase keys.
- Times 12-hour on PC, phone and watch. No new dependencies.

## Review Focus

1. **Lunch edited on two devices** (+5 min on the watch while the PC also changes it): the newest edit wins everywhere; an older file never undoes a newer edit. → Tasks 7, 8.
2. **Noise lock with the player already playing when headphones disconnect** (and stop-on-disconnect off): locked means it pauses and can't resume until headphones are back. → Tasks 2, 3.
3. **Test session vs a real PC session:** a real session on another device always wins; ending the test never ends a real one; nothing about the test is counted, shown in History or written to the sync file. → Task 1.
4. **A category deleted while tasks use it:** those tasks fall back to "No category"; nothing is lost. → Task 4.
5. **Parking the current task:** the bar moves to the next open task; the come-back text is the task title plus " (+N steps)" when it had open subtasks. → Task 6.

---

### Task 1: Phone test session (A 1)

**Files:** `android/app/.../essentials/EssentialsLaunch.kt` (`runningSessionStart`, the one source every session reaction reads: essentials screen, LockMyPhone-Extended hardcore, `CurboxHardMode`), new `android/app/.../session/TestSession.kt` (prefs "testSession": start, end), the phone's Today "More options" UI (find the Today composable that shows More options / start buttons), the notification/Today header (`DayHeader`) so a running test shows "Test session · 0:45 left".
- Decisions: 1 minute by default, adjustable (1-60 min, stepper or slider). It runs like a session on another device for the phone's own reactions only. **Never counted, never in History, never in the sync file, never sent to the watch.** End early at any time (no wait: it's a test). A real session on another device wins: while one runs, Start test is disabled and `runningSessionStart` returns the real one.
- [ ] Commit `Phone: a test session (1 min by default) to try your settings`.

### Task 2: Noise player lock + stop on disconnect, PC (C 2)

**Files:** `Services/NoisePlayerService.cs`, `Services/NoiseSettings.cs` / `NoiseSettingsService.cs` (document "noise"), `Services/HeadphoneAutoPlayService.cs` + `IHeadphoneWatcher` (headphones present?), `ViewModels/NoisePlayerViewModel.cs` + `Views/NoisePlayerView.axaml` (sidebar) and the on-screen window's Ctrl+Alt noise row, Settings → Noise player.
- **Lock** (a lock icon toggle on the player, Material icon, per device, saved in "noise"): while locked and no headphones are connected (wired or any Bluetooth audio output the watcher reports), Play does nothing and the player says "Connect headphones" briefly; media keys and global shortcuts obey it; if headphones disconnect while playing, it pauses and stays paused until headphones are back and Play is pressed.
- **Stop when headphones disconnect** (Settings → Noise player, off by default, separate from headphone auto-play): pauses the player when headphones go, locked or not.
- If the headphone watcher only covers devices chosen for auto-play, extend it to report "any headphones connected" without changing auto-play's device choice.
- [ ] Commit `Noise player: lock to headphones; stop when they disconnect`.

### Task 3: Noise player lock + stop on disconnect, phone (C 2)

**Files:** `android/app/.../noise/` (`NoisePlayer`, `NoiseService`, `NoiseSettings(Store)`, `NoiseState`, `HeadphoneWatcher`, `HeadphoneStore`), the noise bar composable, Settings → Noise player, the widget's play/pause, the watch commands path (`watch/` on the phone and `android/wear/.../ui/NoisePage.kt`).
- Same behaviour as Task 2 on the phone. Watch play commands obey the lock; the watch's Noise page shows a small lock when locked and no headphones (add `locked: Boolean` to the noise state the phone sends; default false when absent).
- [ ] Commit `Phone: noise player lock to headphones; stop when they disconnect`.

### Task 4: Task categories, PC (C 8.1)

**Files:** Core `Domain/SessionTask.cs` (+ optional `CategoryId`), new `Domain/TaskCategory.cs` (Id, Name, Colour hex), `Services/SessionTasksService.cs` (categories document "session.categories", DPAPI like "session.tasks"; CRUD), `ViewModels/*Checklist*` + `Views/ChecklistView.axaml` (pick a category per task; the list grouped under category headers in the category's colour, "No category" last; manage categories: add, rename, recolour (a fixed palette of 8), delete), the on-screen bar's task line (category colour dot), Notebook (category shown).
- Sync: the checklist sent to paired devices (link state and sync file, ADR 24) carries each task's category name and colour (new optional fields on the synced task: `category`, `categoryColor`). Add them to the C# sync/link records only; Task 5 reads them on the phone.
- CLAUDE.md: in the ADR 24 privacy line add "and their categories (name, colour)".
- [ ] Commit `Tasks: categories with colours, grouped in the checklist`.

### Task 5: Task categories on the phone (C 8.1)

**Files:** `android/shared` checklist model + JSON (read `category`, `categoryColor`), the phone's read-only Today checklist card (group under category headers with colour dots).
- Runs after Task 4 merges (needs the field names it chose: read them from Task 4's commit).
- [ ] Commit `Phone: the checklist card groups tasks by category`.

### Task 6: Park tasks ↔ come-backs, edit come-backs in place, drag to reorder, PC (C 8.2-8.4)

Runs after Task 4 (same checklist files).
**Files:** `Services/SessionTasksService.cs`, `Services/ComebackStore.cs` (edit exists after Phase 2), `ViewModels`/`Views` of the checklist (Today, Focus mode, quick-session popup share `ChecklistView`), Today's "Come back to" list and Focus mode's come-backs.
- **Park:** a task's row gets ⚑ "Park" (moves it to come-backs: text = task title, plus " (+N steps)" when it had open subtasks; it leaves the checklist). A come-back's row gets "Add to tasks" (becomes a task at the end of the checklist, the come-back is ticked). Both allowed during a session (they don't loosen anything).
- **Edit come-backs in place** on Today and in Focus mode (✎, Enter or click away saves, Esc cancels — same as task rename).
- **Drag to reorder** tasks (and subtasks within their task) with the pointer; keep ↑ ↓. Avalonia: pointer-press + move threshold + drop position from the row under the pointer; no new library.
- [ ] Commit `Tasks: park to come-backs and back, edit come-backs, drag to reorder`.

### Task 7: Flexible lunch additions, PC (C 10)

**Files:** `Services/LunchService.cs`, `ViewModels/LunchCardViewModel.cs` + its view on Today, Core `Sync/SyncLunch.cs` (+ optional `DateTimeOffset? EditedAt`), the sync builder/merge where lunch is taken (`TakeFromAsync` or similar in `LunchService`), Settings → Your day ("Lunch length on workdays", 15-120 min, step 5, default 60; device-local document "lunch" or the day bar settings: choose the existing "lunch" document).
- **Start lunch on any workday** (the plan's workdays, or a one-off workday), with or without a flexible lunch part; the part (if any) only gives the hint time and the window shown on the day bar. Length = the setting; a flexible part's own length still wins when it has one? No: the setting wins (the user asked for a lunch length setting); the part's window is only a hint.
- **+5 / −5 min** on the running lunch (Today card and the on-screen window's Ctrl+Alt details if lunch shows there); never before now + 1 min; each change sets `EditedAt`.
- Merge: take another device's lunch for today when its `EditedAt ?? Start` is newer than ours.
- CLAUDE.md: "today's flexible lunch (start and end; every device takes the newest edit for the day)".
- [ ] Commit `Lunch: start on any workday, change its length while on it, a length setting`.

### Task 8: Flexible lunch on the phone and the watch (C 10)

**Files:** `android/shared` `SyncLunch` (+ `editedAt`, JSON), `android/app/.../lunch/LunchStore.kt` + the Today lunch card + Settings (same length setting, phone-local prefs), the watch snapshot (`android/shared` `SnapshotBuilder` + its model: lunch start/end), `android/wear` a Lunch page (Start lunch / I'm back / +5 / −5 and a live countdown ring), phone ↔ watch messages ("/lunch" with action start/back/plus/minus) handled on the phone (`watch/` listener), which updates `LunchStore` and so the sync file.
- Same rules as Task 7 (any workday, length setting, newest edit wins via `editedAt ?? start`).
- The watch page shows only when today is a workday or lunch runs.
- [ ] Commit `Phone and watch: lunch any workday, change its length, a Lunch page on the watch`.

## Execution waves

| Wave | In parallel |
|---|---|
| A (after Phase 2 wave A is merged; they touch Settings) | Task 1, Task 2, Task 3, Task 4, Task 7 |
| B | Task 5, Task 6, Task 8 |
