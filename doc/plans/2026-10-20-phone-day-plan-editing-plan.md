# Phone Day Plan Editing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Edit the whole day plan on the phone (Workdays/Other days plans, workday chips, one-off days, categories). Edits wait when the PC is out of reach. When both sides changed, ask with both edit times, the newer highlighted and the changes as bullet points.

**Architecture:**
- **PC (Core + App):** One new link request, `dayPlanEdit`. A pure Core decision (`DayPlanEditDecision`) says applied, locked, conflict or invalid; the App's link host saves an applied plan. The PC's settings push gains the plan's version (a hash) and its last edit time.
- **Phone:** Ports of the editor's rules: snap, add, move, check, and list changes. A JSON writer for the plan. A pending edit kept in the phone's store. A Compose editor (drag bar + list, like the PC) and a conflict dialog.

**Tech Stack:** .NET 10 C# (Core, App), System.Text.Json, SHA-256; Kotlin, Compose Material 3, kotlinx.serialization JSON objects.

**Spec:**
- `doc/adr/0019-let-paired-phones-change-the-day-plan.md` (Accepted 2026-10-20 with this plan).
- The user's answers on 2026-10-20: edit everything; edits wait and send; on a conflict, ask with both edit times, the newer highlighted, the changes as bullets.
- The day plan rules in `doc/plans/2026-10-20-body-double-design.md` part B and the project's `CLAUDE.md`.

## Global Constraints

- Core has no UI or OS dependencies; one public type per file; file-scoped namespaces; nullable on.
- No tests in the repo (user rule):
  - C# checks are scratch console programs (`scratchpad/phoneplancheck`, the existing `scratchpad/linkcheck`).
  - Kotlin checks run in a scratch Gradle JVM project (`scratchpad/ktcheck`) that compiles the repo's pure Kotlin files.
- No new dependencies. Commit and push when finished (user OK); stage new files as they're created.
- The plan's JSON on the wire is exactly the profile document's `dayPlan` object (schema 5): camelCase, enums as camelCase strings, times "HH:mm:ss", dates "yyyy-MM-dd". No schema bump.
- PC rules apply to phone edits:
  - locked during a focus session the user started;
  - checked like the Day plan tab (overlaps, 15-minute minimum, categories exist);
  - applied at once.
- Only the day plan travels; nothing else on the phone becomes editable.

## Decisions made here (my calls, for the user to check)

1. **Editor:** opened from an "Edit plan" button on the phone's Today header. Tabs: Workdays · Other days · One-off days · Categories.
2. **While waiting to send:** the phone's timeline and notification show the edited plan, with a "Waiting to send to <PC>" line. Discard is available.
3. **Locked:** the phone keeps the edit, says "Sends when your focus session on the PC ends", and sends it on the first state message with no session running.
4. **Conflict choices:** "Keep the PC's" and "Use this phone's". Each card shows its edit time ("edited 4:12 PM") and the newer one has a teal border. The bullets list the changes from the shared starting point, e.g. "Workdays: Work 9:00 AM–12:00 PM → 8:30 AM–12:00 PM", "Friday is no longer a workday", "Added a one-off day: Fri 10/23", "New category: Gym".
5. **Invalid:** the phone shows the PC's reason. The edit stays open on the phone so you can fix it.
6. **Older PC app:** if it answers "Unknown request", the phone shows "Update AccountabiliBuddy on <PC> to edit the plan from here".

## Review Focus

1. A night part (crossing midnight) edited on the phone arrives on the PC the same: start after end, an end of 00:00 = midnight of its own day.
2. Two quick edits on the phone before it reconnects are sent as one (the latest). The base stays the plan the first edit started from.
3. Applying the phone's edit while the PC's Day plan tab has unsaved changes: the tab doesn't overwrite the phone's plan silently.
4. A conflict where both sides made the same change: no conflict is shown, the edit applies.
5. The PC is locked, then the session ends while the phone is connected: the edit is sent without reopening the app.

---

### Task 1: The plan as JSON, its version, and checking a whole plan (Core)

**Files:**
- Create: `src/AccountabiliBuddy.Core/Engine/DayPlanVersion.cs`
- Modify: `src/AccountabiliBuddy.Core/Storage/ProfileDocumentSerializer.cs`, `src/AccountabiliBuddy.Core/Engine/DayPlanValidator.cs`
- Test: `scratchpad/phoneplancheck/Program.cs`

**Interfaces:**
- Produces:
  - `public static JsonElement ProfileDocumentSerializer.SerializeDayPlan(DayPlan plan)`
  - `public static DayPlan ProfileDocumentSerializer.DeserializeDayPlan(JsonElement json)`; throws `JsonException` on a bad shape
  - `public static string DayPlanVersion.Of(DayPlan plan)`: hex SHA-256 of the serialized plan with parts ordered by start then id and one-off days by date, so the same plan always gets the same version
  - `public static string? DayPlanValidator.ValidatePlan(DayPlan plan)`: null when fine. It checks:
    - at least one category; category ids unique, names not empty, colours "#RRGGBB";
    - each template and each one-off day valid by the existing rules;
    - one-off dates unique.

- [ ] **Step 1: Failing checks:**
  - `DeserializeDayPlan(SerializeDayPlan(DayPlan.Default)) == DayPlan.Default` (by version);
  - a night part 23:45–06:45 and a 22:00–00:00 part round-trip unchanged;
  - two plans differing only in part order have the same version, and any time change gives a different one;
  - `ValidatePlan` refuses an overlap, an unknown category, a duplicate one-off date and an empty category name, and accepts `DayPlan.Default`.
- [ ] **Step 2:** Run. Expected: build errors (members missing).
- [ ] **Step 3:** Implement. Serialize with the same options and enum handling as the profile document, by writing a document and taking its `dayPlan` property, so the shape can't drift.
- [ ] **Step 4:** Run. Expected: all PASS.

### Task 2: The link request and the PC's decision (Core)

**Files:**
- Create: `src/AccountabiliBuddy.Core/Link/LinkDayPlanEdit.cs`, `LinkDayPlanAnswer.cs`, `LinkDayPlanStatus.cs`, `src/AccountabiliBuddy.Core/Engine/DayPlanEditDecision.cs`
- Modify: `src/AccountabiliBuddy.Core/Link/LinkMessages.cs`, `ILinkSessionHost.cs`, `LinkServer.cs`
- Test: `scratchpad/phoneplancheck`, `scratchpad/linkcheck`

**Interfaces:**
- Produces:
  - `LinkMessages.DayPlanEdit = "dayPlanEdit"` and `LinkMessages.DayPlanAnswer = "dayPlanAnswer"`
  - `public sealed record LinkDayPlanEdit(string BaseVersion, JsonElement DayPlan, DateTimeOffset EditedAt);`
  - `public sealed record LinkDayPlanAnswer(string Status, string Message, string? CurrentVersion = null, JsonElement? CurrentDayPlan = null, DateTimeOffset? CurrentEditedAt = null);`
  - `public static class LinkDayPlanStatus { Applied = "applied", Locked = "locked", Conflict = "conflict", Invalid = "invalid" }`
  - `public static (LinkDayPlanAnswer Answer, DayPlan? ToSave) DayPlanEditDecision.Decide(DayPlan current, DateTimeOffset? currentEditedAt, LinkDayPlanEdit edit, bool locked)`, checked in this order:
    1. **locked** → Locked;
    2. **unreadable JSON** → Invalid;
    3. **same plan as the PC's** → Applied, nothing to save (Review Focus 4);
    4. **base ≠ the PC's version** → Conflict, carrying the PC's version, plan and edit time;
    5. **`ValidatePlan` fails** → Invalid with its reason;
    6. **otherwise** → Applied with the plan to save.
  - `Task<LinkDayPlanAnswer> ILinkSessionHost.ApplyDayPlanAsync(LinkDayPlanEdit edit)`. `LinkServer` answers `dayPlanEdit` with `dayPlanAnswer`.

- [ ] **Step 1: Failing checks** for each branch of `Decide`, including Review Focus 4. Add a `linkcheck` line: a fake host receives the edit over the real TLS link and the device gets `dayPlanAnswer`.
- [ ] **Step 2:** Run. Expected: build errors.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: all PASS; the solution builds after Task 3's host implementation (do Tasks 2 and 3 back to back).

### Task 3: The PC saves phone edits and pushes the plan's version and edit time (App)

**Files:**
- Create: `src/AccountabiliBuddy.App/Services/DayPlanEditTimeService.cs`
- Modify:
  - `src/AccountabiliBuddy.App/Services/Link/LinkSessionHost.cs`
  - `src/AccountabiliBuddy.App/App.axaml.cs`
  - `src/AccountabiliBuddy.App/ViewModels/DayPlanViewModel.cs` (Review Focus 3)

**Interfaces:**
- Produces:
  - `DayPlanEditTimeService`: document "dayplan.editedAt"; `DateTimeOffset? Current`. It records the trusted now whenever the configuration's `DayPlanVersion` changes (any source), or the phone's `EditedAt` (not later than now) when the change came from the phone.
  - The settings content gains `"dayPlanVersion"` and `"dayPlanEditedAt"` (ISO, may be null).
  - `LinkSessionHost.ApplyDayPlanAsync` runs on the UI thread. `locked` is the same check the Day plan tab uses; on Applied it calls `configuration.UpdateAsync(doc => doc with { DayPlan = toSave })`.
  - The Day plan tab with unsaved edits, when the saved plan changes underneath it: it shows "The plan was changed from <device>; your unsaved edits are kept — Save replaces it, Revert shows the new one" and doesn't reload silently.

- [ ] **Step 1:** Implement.
- [ ] **Step 2:** Verify:
  - **Build:** the solution builds.
  - **linkcheck:** a real `LinkSessionHost` path is covered by a temp-DB run of `ApplyDayPlanAsync`, which saves; reading the document back gives the edited plan and "dayplan.editedAt" is set.
  - **phoneplancheck:** still passes.

### Task 4: The editor's rules on the phone (Kotlin, pure)

**Files:**
- Create in `android/app/src/main/java/com/accountabilibuddy/companion/`:
  - `data/DayPlanJson.kt`: the writer; the parser already exists in `ProfileDocumentParser`
  - `engine/DayTemplateEditing.kt`: port of the Core class: `snap`, `addAt`, `move(…, crossesMidnight)`, `ranges`
  - `engine/DayPlanValidator.kt`: port, including `validatePlan`
  - `engine/DayPlanChanges.kt`: `describe(base: DayPlan, edited: DayPlan): List<String>`, with bullets like Decision 4
- Test: `scratchpad/ktcheck` (Gradle Kotlin JVM app compiling those files plus `model/*` and the parser via `srcDir`s; run with the Android project's wrapper)

- [ ] **Step 1: Failing checks**, mirroring the C# checks:
  - snap and move, including a night part staying a night part;
  - the validator's refusals;
  - JSON written by the phone and read back by `ProfileDocumentParser`, identical for `DayPlan.Default` and a night part;
  - `describe` for: a changed time, a workday chip, an added one-off day, a new category; and "No changes" for equal plans.
- [ ] **Step 2:** Run. Expected: compile errors.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: all PASS. If the scratch Gradle project can't be made to run, record a ruling and check on the device instead.

### Task 5: Sending, waiting and conflicts on the phone (Kotlin)

**Files:**
- Modify:
  - `link/LinkClient.kt`: `suspend fun sendDayPlanEdit(edit): DayPlanAnswer?`
  - `link/LinkMessages.kt`
  - `data/CompanionStore.kt`: "dayplan-pending.json" holding base version, base plan, edited plan, edited time
  - `data/Importer.kt` / `SettingsJson.kt`: read `dayPlanVersion` and `dayPlanEditedAt`
  - `ui/CompanionViewModel.kt`
- Create: `link/DayPlanAnswer.kt`, `model/PendingDayPlan.kt`

**Interfaces:**
- **Saving an edit:** `CompanionViewModel.saveDayPlan(edited: DayPlan)` keeps the first base when an edit is already pending (Review Focus 2), stores it, then tries to send.
- **Sending:** it sends on connect, after any settings message, and on a state message with no session running (Review Focus 5).
- **Answers:**
  - **Applied:** clears the pending edit.
  - **Locked:** keeps it, with the status line.
  - **Conflict:** `state.dayPlanConflict = DayPlanConflict(pcPlan, pcVersion, pcEditedAt, phonePlan, phoneEditedAt, pcChanges, phoneChanges)`.
  - **Invalid:** keeps the edit open, with the reason.
- **Resolving a conflict:** `useMine()` resends with base = the PC's version; `keepPc()` drops the edit.
- **Timeline:** while pending, the phone's timeline uses the edited plan.

- [ ] **Step 1:** Implement.
- [ ] **Step 2:** Verify:
  - **Build:** `assembleDebug` succeeds.
  - **Device:** the AVD or Pixel 7, with the dev PC build not running alongside the installed app, so use the installed 0.1.49 after Task 7. Check: apply, locked (start a quick session on the PC), conflict (edit on both, then reconnect).

### Task 6: The phone's editor and conflict dialog (Compose)

**Files:**
- Create in `android/.../ui/`:
  - `DayPlanEditorScreen.kt`: tabs, chips, part list with Material 3 time pickers, category dropdown, delete, add
  - `DayPlanBarEditor.kt`: Canvas with drag: tap empty space adds, tap selects, drag edges resizes, drag the middle moves; crossing decided at drag start, via `DayTemplateEditing`
  - `CategoriesEditor.kt`: name, colour palette, behaviour, add, delete when unused
  - `OneOffDaysEditor.kt`: date picker, the same editor, back to the usual plan
  - `DayPlanConflictDialog.kt`
- Modify: `ui/TodayScreen.kt` ("Edit plan" button and the waiting/locked line), `ui/MainScreen.kt` (navigation to the editor)

- [ ] **Step 1:** Implement.
- [ ] **Step 2:** Verify:
  - **Build:** `assembleDebug` + `assembleRelease` succeed.
  - **Pixel 7:** screenshot the editor (drag a part, change a time, add a one-off day and a category), save, and the PC's Day plan tab shows it. Then a conflict: change Work on the PC and on the phone (phone offline), reconnect. Expected: the dialog shows both times, the newer highlighted, and bullets.

### Task 7: Docs, version, review, ship

- [ ] ADR 19 → Accepted. Project `CLAUDE.md`:
  - hard rules: paired phones can change the day plan (ADR 19);
  - "dayplan.editedAt" in the documents list;
  - roadmap.
- [ ] Version 0.1.49; rerun phoneplancheck, linkcheck, ktcheck, b2check, dayplancheck, editorcheck, sleepcheck; fresh opus reviewer with Review Focus; one fix pass.
- [ ] Commit, push, install 0.1.49 and the APK (user OK'd committing and pushing finished features).
