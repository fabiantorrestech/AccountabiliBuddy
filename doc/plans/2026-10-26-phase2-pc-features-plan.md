# Phase 2 (PC features) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** The seven PC features of Phase 2 of the user's list: a new spare key with friction (C 5), the work nudge rework (PC 1), Notebook edit/delete (PC 2.1), the Overlay settings group (PC 8.1), timeline opacity (PC 8.2), a simpler browser extension setup (PC 10) and the halo now line on the PC (C 9).

**Architecture:** App-layer changes in `src/AccountabiliBuddy.App` (services, view models, views), following existing patterns named per task. One new ADR (41) for the spare key. No schema change to the shared profile JSON; device-local documents only.

**Tech Stack:** .NET 10, C#, Avalonia 12.1.4, CommunityToolkit.Mvvm.

**Spec:** `doc/plans/2026-10-26-feature-list-plan.md` (decisions table). ADR 41 (`doc/adr/0041-get-a-new-spare-key-with-friction.md`).

## Global Constraints

- No tests (project rule). Verify: `dotnet build AccountabiliBuddy.sln` → Build succeeded, 0 errors, no new warnings in touched files. Manual checks go to the user's end-of-run list.
- Times shown 12-hour with AM/PM. Core has no UI/OS deps. One public type per file; file-scoped namespaces; match surrounding comment density and naming. No new dependencies. No refactoring outside the task.
- Reasons are DPAPI-encrypted (`IDataProtector`) and never leave the device. New device-local state lives in the `documents` table via `SqliteJsonDocumentStore`.
- Escape hatches: nothing here may weaken a running focus session; things refused during a focus session say so.
- Commit per task in the worktree branch, message ending with the session's two attribution lines.

## Review Focus

1. **Spare key request across a restart or clock change:** the 10-minute wait resumes where it was (trusted clock), never restarts at 0 and never finishes early. → Task 1.
2. **Work nudge when the user arrives late** (9:30 for a 9:00 part, the PC asleep or locked until then): nothing fires while away; arrival opens Today; the card comes 5 min after arrival, not after the part's start. → Task 2.
3. **Editing an open come-back that a phone shows:** the new text reaches the sync file; deleting removes it there too. → Task 3.
4. **Timeline opacity while a session runs:** the timer's existing opacity still applies in sessions; the new pair applies only when no session runs. → Task 4.
5. **Firefox path copy:** the copied path is the exact `manifest.json` file that `about:debugging` → Load Temporary Add-on accepts. → Task 5.

---

### Task 1: Get a new spare key (C 5, ADR 41)

**Files:** `src/AccountabiliBuddy.App/Services/ColdTurkeyKeysService.cs` (+ its state record, document "coldturkey.keys"), `ViewModels/ColdTurkeyKeyViewModel.cs`, `Views/ColdTurkeyKeyWindow.axaml(.cs)`, `App.axaml.cs` (`ShowColdTurkeyKey`, ~line 1266), `ViewModels/UpdatesViewModel.cs` + `Views/AboutView.axaml` (beside "Emergency release…"), the Cold Turkey card in Integrations (find it: grep `EmergencyRelease` / Cold Turkey in `IntegrationsViewModel` / its view).

**Decisions (ADR 41):**
- A third mode of the key window, `NewKey`: reason ≥ 40 characters (`PreWorkService.MinReasonLength` value), retype a 12-character code from `PreWorkService.NewUnlockCode()` (no pasting, like the existing spare-key box), then a 10-minute wait with a live countdown and Cancel; then `NewSpareKeyAsync()` → the existing "Your spare key" show-once-and-retype step.
- Refused with a clear message while `SessionController.IsEnforcing` (a focus session the user started) and when 2 requests completed in the last 7 days.
- State in the keys document: `NewKeyRequests` (time + DPAPI-protected reason, 90 days) and `PendingNewKeyAt` (DateTimeOffset?, when the wait started) so the wait survives a restart. Time from the app's `TrustedClock`-backed `TimeProvider`/`SessionController.Now`.
- Confirming the new key must re-lock blocks held with the old password using the new one (reuse the existing "new key after a spare key was used" path in `ColdTurkeyKeysService`, ~line 319, or the confirm path); nothing is released.
- Log line in the service's `Log` ("10:02 AM · New spare key requested", "… made").

- [ ] Steps: state + service methods (`RequestNewKeyAsync(reason, typed, shown)` → string? error; `CancelNewKeyAsync()`; `NewKeyReadyAt`); window mode; entry points; build; commit `Cold Turkey: get a new spare key, with a reason and a 10-minute wait (ADR 41)`.

### Task 2: Work nudge rework (PC 1)

**Files:** `Services/StartNudgeService.cs`, `Services/StartNudgeSettings.cs`, `Services/StartNudgeReason.cs`, new `Services/StartNudgeCard*` + `ViewModels/StartNudgeCardViewModel.cs` + `Views/StartNudgeCardWindow.axaml(.cs)` (model it on the work-hours gate: `WorkGateService`, its window and view model), `App.axaml.cs` (the `Due` handler from Phase 1 Task 5 and the gate window wiring as the pattern), Settings → Your day start-nudge section (`SettingsViewModel`/`SettingsView.axaml`).

**Decisions (user, 2026-10-26):**
- Timing starts on **arrival**, not at the part's start: at a work part's start, if the user is away (idle ≥ 2 min), wait; arrival = the first input after ≥ 10 min idle, or the first input of a work part. Arrival during a work part with no session → Today with the task box (as now, `ShowMainWindow` + focus).
- 5 minutes after arrival with still no session: a **card in front of everything** (Topmost, like the gate): "No session yet · <time>", **Start** (opens Today with the task box, card closes) and **Not now** (needs a reason, ≥ 1 word; DPAPI-encrypted log, 14 days). The card comes back every 15 minutes (the existing repeat setting becomes "Ask again every 5 / 10 / 15 min", default 15; "once" = one card) while the user is present and still no session.
- Never during a session, a calendar event (reuse how `LightsOutService` decides "in a calendar event"), a day off on this computer, or once today's goal is met (`DeepWorkService.Progress` / goal met as used by the work-hours guard). Never on a Local-only computer? Not required — the card is a notice-level UI, allowed under every enforcement limit.
- Phase 1's quiet behaviour stays for `SessionEnded` (flash only) and the old `Repeat` reason is replaced by the card.
- [ ] Steps: service state machine (arrival, card due), card window + VM, Settings text, wiring; build; commit `Start nudge: from when you arrive, then a card every 15 minutes`.

### Task 3: Notebook: edit and delete come-backs and notes (PC 2.1)

**Files:** `Services/ComebackStore.cs` (`EditAsync(Guid id, string text)`, `DeleteAsync(Guid id)`), `Services/SessionNotesStore.cs` (notes need an id if they lack one: check `SessionNote`; add `EditAsync`/`DeleteAsync` by id, migrating stored notes without an id by assigning one on load), `ViewModels/NotebookViewModel.cs` (pass `rename:`/`delete:` like tasks), `ViewModels/NotebookItemViewModel.cs`, `Views/NotebookView.axaml` if the buttons are only shown for tasks.
- Both stores raise `Changed`; open come-backs reach paired devices through the existing sync path (verify the edited text goes out; no new sync field).
- Delete asks no confirmation if tasks' delete doesn't; follow tasks' existing UX.
- [ ] Steps; build; commit `Notebook: edit and delete come-backs and notes`.

### Task 4: Settings "Overlay" group + timeline opacity (PC 8.1, 8.2)

**Files:** `ViewModels/SettingsViewModel.cs` (`Groups` ~line 871; overlay settings load/save ~1314, ~1426), `Views/SettingsView.axaml` (the On-screen timer ~527 and On-screen timeline ~644 cards), `Services/OverlaySettings.cs` (new fields), `ViewModels/OnScreenOverlayViewModel.cs` / `Views/SessionOverlayWindow.axaml(.cs)` (where the timer's opacity and held opacity are applied), `Services/Search/*` (if section → group mapping lives there, e.g. `UiSearchSource`).
- New side group **"Overlay"** after "On screen & sound", holding "On-screen timer" and "On-screen timeline"; "On screen & sound" keeps Theme, Animations, Sounds when time is up, Noise player. Search results for those sections open the new group.
- Timeline gets **"Opacity"** (20-100%, step 5, default = the timer's current default) and **"Opacity while Ctrl+Alt is held"** (20-100%, default 100), used only while no session runs (timeline-only display); in a session the timer's existing pair still applies. Saved in the same settings document as the timer's (optional fields; old documents keep defaults).
- [ ] Steps; build; commit `Settings: an Overlay group; the timeline gets its own opacity`.

### Task 5: Simpler browser extension setup (PC 10)

**Files:** `ViewModels/BrowserSetupRowViewModel.cs` (`OpenFolder` uses ~173, ~183), `ViewModels/SetupViewModel.cs`, the Setup view's browser section (grep `BrowserSetupRow` in `Views/`).
- Less text: keep the status light and "installed" line per browser; drop repeated explanations (one short line above the list at most).
- Replace "open folder" buttons with **Copy path**: Chromium browsers copy the unpacked extension folder; Firefox copies the full path to its `manifest.json` (what Load Temporary Add-on asks for). Brief "Copied" feedback. Clipboard via Avalonia `TopLevel.Clipboard`.
- A "Browser extensions" heading with the browsers indented under it.
- [ ] Steps; build; commit `Setup: browser extensions in one short list with copy-path buttons`.

### Task 6: The halo now line on the PC (C 9)

**Files:** `Controls/DayBarControl.cs` (draws the day bar and, if shared, the on-screen timeline), `ViewModels/DayBarViewModel.cs`, `ViewModels/OnScreenOverlayViewModel.cs` / overlay timeline drawing if separate. Reference: the phone's `android/app/src/main/java/com/accountabilibuddy/companion/ui/DayBarPainter.kt` (commit a377dcd: the now line with a halo in the opposite colour).
- The red now line gets a halo (outline) in the opposite colour of what it sits on, on Today/History's day bar and the on-screen timeline, so it stands out from red drift marks. Same widths/ratios as the phone's painter, scaled to the PC's sizes.
- [ ] Steps; build; commit `Day bar: the now line gets a halo, like the phone's`.

## Execution waves

| Wave | In parallel (worktrees) |
|---|---|
| A | Task 1, Task 3, Task 4, Task 5 |
| B | Task 2, Task 6 |
| — | Phase 2 review with Phase 1 at the end of the run |
