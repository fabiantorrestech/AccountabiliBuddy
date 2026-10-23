# Redesign phase 5: capture and the shutdown routine

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** thoughts during a session go somewhere without breaking focus: a line in Focus mode keeps a **note** (shown once on the session's review card, kept with the session in History) or, with ⚑ or a leading `!`, a **come-back** (listed on Today until ticked). Come-backs reach the phone (ADR 30). At the end of the day's work, a short **shutdown** card settles open come-backs, unfinished tasks and tomorrow's first task.

**Architecture:** App: `SessionNotesStore` (document "capture.notes", one DPAPI-sealed list, 90 days, like `ComebackStore`), `CaptureBoxViewModel` in Focus mode, notes on `SessionReviewViewModel` and History's `SessionRowViewModel`; `ShutdownService` + `ShutdownCardViewModel` on Today (document "shutdown": the last day shown, tomorrow's first task). Core/Link + Android: come-backs (open ones: text and when) in the PC's link state and its sync file (ADR 30), read-only on the phone's Today.

**Spec:** `doc/plans/2026-10-22-redesign-design.md` sections F (capture) and J (shutdown routine). The phone's own capture box and ticking a come-back from the phone or watch wait for phase 3's action list (settled reviewer question 3); notes never leave the PC.

## Global Constraints

- Typed text (notes, come-backs, tomorrow's task) is DPAPI-encrypted on the PC (`IDataProtector`), like checklists (ADR 24).
- Notes never leave the PC; come-backs go only to the user's paired devices and sealed sync files (ADR 30).
- The shutdown card is skippable in one click and never shows twice the same day.
- Commit per task with the session's trailers; never commit `handoffContext/`.

## Review Focus

1. A note typed during a session that then ends early with a reason: the card shows the note; the reason stays in History, encrypted as before.
2. A session with no unknowns and no repeats but a note: the review card still appears, with just the notes.
3. A note typed between sessions (Focus mode isn't shown then): there is no capture box outside a session; Ctrl+Alt+N keeps working for come-backs.
4. The day's last Watch part ending while a session runs: the shutdown card waits until the session ends.
5. A phone on an older version: unknown `comebacks` fields are ignored.

---

### Task 1: Notes and the capture box (Focus mode)
Files: `Services/SessionNote.cs`, `Services/SessionNotesStore.cs`, `ViewModels/CaptureBoxViewModel.cs`, `ViewModels/FocusViewModel.cs`, `Views/FocusView.axaml`, `App.axaml.cs`. Check: syncapp `CaptureNotesChecks.cs` (store sealed and pruned; `!` and ⚑ route to come-backs, a plain line to notes).

### Task 2: Notes on the review card and in History
Files: `Services/SessionReviewService.cs` (`NotesBetween`; a card with only notes still shows), `ViewModels/SessionReviewViewModel.cs` (`Notes`), `Views/SessionReviewView.axaml`, `ViewModels/SessionRowViewModel.cs` (`Notes`), `ViewModels/HistoryViewModel.cs`, `Views/HistoryView.axaml`. Check: the same file.

### Task 3: Come-backs on the phone (ADR 30)
Files: `doc/adr/0030-carry-come-backs-to-the-users-devices.md`; `Core/Link` state (+ `Comebacks`), `Core/Sync/SyncFile` (+ optional `Comebacks`), the PC's builders; `android/shared` sync file + link parsing, phone Today "Come back to" (read-only). Checks: synccore/ktcheck round trips across languages; linkcheck.

### Task 4: The shutdown routine
Files: `Services/ShutdownService.cs`, `Services/ShutdownState.cs`, `ViewModels/ShutdownCardViewModel.cs`, `Views/TodayView.axaml`, `Services/SessionTasksService.cs` (tomorrow's first task becomes the next session's first task). Check: syncapp `ShutdownChecks.cs` (shows once after the last Watch part, waits for a running session, skip, tomorrow's task carried).

Ship 0.1.74 after Tasks 1-2, 0.1.75 + APKs after Tasks 3-4.
