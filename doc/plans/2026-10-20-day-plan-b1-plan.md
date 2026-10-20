# Day Plan (B1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The day plan (categories, a Workdays and an Other-days template, one-off days) replaces the weekly schedule of profiles: stored in the profile document v5, edited in a "Day plan" tab (drag bar + list) and in Exceptions, feeding the day bar's planned lane and sleep mode; AccountabiliBuddy stops running scheduled sessions; the phone reads v5.

**Architecture:** Pure model and resolution in Core (`Domain/DayPlan*`, no OS types), migrated by `ProfileDocumentSerializer` (v4 → v5 drops `schedule`, seeds a starting plan). Every reader of `WeeklySchedule` switches to `DayPlan` or is removed (ADR 18). The App gets one reusable editor (`DayPlanBar` control + `DayPlanEditorViewModel`) used by the Day plan tab and Exceptions. Sleep mode becomes a projection of the plan's Sleep parts. The four-row timeline is plan B2; phone screens are B3.

**Tech Stack:** .NET 10, C#, Avalonia 12, CommunityToolkit.Mvvm, System.Text.Json; Android Kotlin (kotlinx.serialization JSON objects, Compose).

**Spec:** `doc/plans/2026-10-20-body-double-design.md` (part B), `doc/adr/0018-leave-enforcement-to-the-blockers.md`.

## Global Constraints

- Core has no UI or OS dependencies (ADR 4); one public type per file; file-scoped namespaces; nullable on.
- No tests in the repo (user rule): each check is a throwaway console program in the session scratchpad referencing the projects, as used so far (`scratchpad/<name>check`), printing PASS/FAIL lines.
- Commit and push only when the user says so; stage new files as they're created.
- Never delete files without the user's OK: approving this plan is that OK for exactly the files listed under "Delete".
- Profile document: camelCase JSON, enums as camelCase strings, times "HH:mm:ss", dates "yyyy-MM-dd", `schemaVersion` 5.
- Categories *(user)*: Work and Extra Work = Watch; Lunch, Evenings, Free time = Rest; Sleep = Sleep. Behaviours: Watch, Rest, Sleep.
- Workdays default Mon–Fri; editor snaps to 15 minutes; Day plan tab locked during a focus session the user started (ADR 18).
- Day plan changes apply at once (no looser-change delay for the plan, ADR 18).
- Real-data checks run on copies of `%LocalAppData%\AccountabiliBuddy\accountabilibuddy.db`, never on the file itself.

## Decisions made here (my calls, for the user to check)

- **Midnight rule:** a part whose End ≤ Start (e.g. Sleep 23:45–06:45) belongs to the day it **ends** on and starts the evening before. So the Workday template's Sleep is the night before a workday, matching today's "weeknights" (Sun–Thu nights) and "weekends" (Fri, Sat nights).
- **Overlaps across days:** where yesterday's last part runs past the start of today's midnight-crossing part, today's part wins from its start.
- **Migration v4 → v5:** `schedule` (20 weekly blocks, 0 time off, 0 one-off blocks in the user's data) is dropped (Cold Turkey keeps its own schedules). Seeded plan: Workday = Work 09:00–12:00, Lunch 12:00–13:00, Work 13:00–17:00, Extra Work 18:00–20:00, Sleep 23:00–07:00; Other days = Free time 10:00–22:00, Sleep 00:30–08:30. Then the App replaces both Sleep parts once with the user's sleep times (23:45–06:45 and 01:30–08:30).
- **Sleep settings** become derived: the "sleep" document is rewritten from the plan whenever the plan changes (its `enabled` stays the user's toggle), so the bedtime reminder, the day bar's night and the phone keep working unchanged. Settings keeps the on/off toggle and gets "Edit sleep times in the Day plan".
- **A saved scheduled session** (checkpoint from before the update) is ended at launch instead of resumed.

## Review Focus

1. **Daylight-saving days** (23 or 25 hours): parts resolve on local wall-clock times; a part across the 2 AM jump keeps its wall-clock start and end. Pinned in Task 1's check.
2. **Sunday evening into Monday's sleep:** an Other-days part ending after 23:45 on Sunday is cut at Monday's Sleep start, never overlapping. Pinned in Task 1.
3. **An old phone app receives a v5 document** over the link: it shows "Update the phone app" and keeps the link up instead of crashing. Pinned in Task 9.
4. **Editing today's plan while a Watch part is running:** the day bar's planned lane and sleep line update at once after Save. Pinned in Task 7's check.
5. **A one-off day for a date whose template has a midnight-crossing Sleep part:** the one-off day's own parts replace that day's template entirely, including the night before it. Pinned in Task 1.

---

### Task 1: Day plan model and resolution (Core)

**Files:**
- Create: `src/AccountabiliBuddy.Core/Domain/CategoryBehavior.cs`, `DayCategory.cs`, `PlanPart.cs`, `DayTemplate.cs`, `OneOffDay.cs`, `DayPlan.cs`, `PlannedPart.cs`
- Create: `src/AccountabiliBuddy.Core/Engine/DayPlanValidator.cs`
- Check: `scratchpad/dayplancheck/Program.cs`

**Interfaces:**
- Produces:
  - `enum CategoryBehavior { Watch, Rest, Sleep }`
  - `record DayCategory(Guid Id, string Name, string Color, CategoryBehavior Behavior)`; Color is "#RRGGBB".
  - `record PlanPart(Guid Id, Guid CategoryId, TimeOnly Start, TimeOnly End, string? Label = null)`; `[JsonIgnore] bool CrossesMidnight => End <= Start`.
  - `record DayTemplate(IReadOnlyList<PlanPart> Parts)`; `static DayTemplate Empty`.
  - `record OneOffDay(Guid Id, DateOnly Date, DayTemplate Template, string? Note = null)`.
  - `record DayPlan(IReadOnlyList<DayCategory> Categories, IReadOnlyList<DayOfWeek> Workdays, DayTemplate Workday, DayTemplate OtherDay, IReadOnlyList<OneOffDay> OneOffDays)` with:
    - `static DayPlan Default` (the seeded plan and six categories above; fixed GUIDs so seeds are stable).
    - `DayTemplate TemplateFor(DateOnly date)`: the one-off day's template, else Workday when `Workdays` contains the date's day, else OtherDay.
    - `IReadOnlyList<PlannedPart> PartsIn(LocalWindow range)`: every part of every date touching the range, resolved with the midnight rule and the cross-day overlap rule, clipped to the range, ordered by start.
    - `PlannedPart? PartAt(DateTime localTime)`.
    - `DayCategory? Category(Guid id)`.
  - `record PlannedPart(PlanPart Part, DayCategory Category, LocalWindow Window)`; `string Name => Part.Label ?? Category.Name`.
  - `static class DayPlanValidator`: `string? Validate(DayTemplate template, IReadOnlyList<DayCategory> categories)` (null = fine; otherwise e.g. "Work 12:00–13:00 overlaps Lunch 12:30–13:30."): no overlapping parts within a template (a midnight-crossing part counts as 00:00–End plus Start–24:00), every part ≥ 15 minutes, every CategoryId exists. `string? Validate(DayPlan plan)` checks both templates, every one-off day, unique category names, at least one category.

- [ ] **Step 1: Write the check** `scratchpad/dayplancheck/Program.cs` asserting, for `DayPlan.Default` with workdays Mon–Fri:
  - `PartAt(Mon 2026-10-19 10:00)` is Work; `PartAt(Mon 12:30)` is Lunch; `PartAt(Sat 2026-10-24 11:00)` is Free time.
  - `PartAt(Sun 2026-10-18 23:30)` is Sleep (the Workday Sleep 23:00–07:00 starts Sunday evening); `PartAt(Fri 2026-10-23 23:30)` is not Sleep (Saturday's Sleep starts 00:30).
  - An Other-days template with Free time 10:00–23:59 is cut at 23:00 on Sunday (Review Focus 2).
  - A one-off day on Mon 2026-10-26 with only Free time 10:00–18:00: `PartAt(Sun 2026-10-25 23:30)` is null and `PartAt(Mon 11:00)` is Free time (Review Focus 5).
  - DST: with `TimeZoneInfo` "Central Standard Time", Sun 2026-11-01 parts keep wall-clock 00:30–08:30 (Review Focus 1).
  - `Validate` rejects overlapping Work 12:00–13:00 and Lunch 12:30–13:30, a 10-minute part, an unknown category; accepts `DayPlan.Default`.
- [ ] **Step 2: Run it**, expect build errors (types missing): `cd scratchpad/dayplancheck && dotnet run`.
- [ ] **Step 3: Implement the types and `DayPlanValidator`** as in Interfaces. `PartsIn` walks dates from `range.Start.Date - 1 day` to `range.End.Date`, resolves each template's parts (midnight-crossing parts start at `date - 1 day + Start`), applies the cross-day rule (a part ends no later than the next resolved part's start), then clips.
- [ ] **Step 4: Run the check**: all PASS.
- [ ] **Step 5: Stage** the new files.

### Task 2: Profile document v5 and migration (Core)

**Files:**
- Modify: `src/AccountabiliBuddy.Core/Storage/ProfileDocument.cs` (remove `Schedule`, add `DayPlan DayPlan { get; init; } = DayPlan.Default`, `CurrentSchemaVersion = 5`, version history comment)
- Modify: `src/AccountabiliBuddy.Core/Storage/ProfileDocumentSerializer.cs` (`DayPlanSchemaVersion = 5`; `MigrateScheduleToDayPlan(string json)`: removes `schedule`; the default plan comes from the init default)
- Check: `scratchpad/dayplancheck` (extend)

**Interfaces:**
- Consumes: Task 1 types.
- Produces: `ProfileDocument.DayPlan`; `ProfileDocument` no longer has `Schedule` (constructor is `(SchemaVersion, Profiles, PomodoroDefaults, Escalation, Bypass)`).

- [ ] **Step 1: Extend the check**: deserialize a copy of the user's real "profiles" document (v4, 20 blocks) → `SchemaVersion == 5`, `DayPlan == DayPlan.Default`, six profiles kept, serialized JSON has `"dayPlan"` and no `"schedule"`; a v3 document with `offDays` still loads; round trip v5 → JSON → v5 is equal; a v6 document throws `UnsupportedSchemaVersionException`.
- [ ] **Step 2: Run it**: fails (no `DayPlan` on the document).
- [ ] **Step 3: Implement** the document change and the migration. The build breaks everywhere `Schedule` was used; Tasks 3 and 4 fix those (do Tasks 2–4 before building the solution).
- [ ] **Step 4: Run the check** (after Task 4 builds): all PASS.
- [ ] **Step 5: Stage.**

### Task 3: Remove the weekly schedule and scheduled sessions (ADR 18)

**Files:**
- Modify: `Core/Engine/SessionEngine.cs` (remove `SetSchedule`, `HoldSchedule`, `_schedule`, `_scheduleSuppressedUntil`, `_scheduleHeldUntil`, the scheduled start in `Tick`; `EndSession()` loses `suppressSchedule`; a restored checkpoint with `Mode == Scheduled` is ended, not resumed), `Core/Engine/SessionCheckpoint` (drop the suppression field), `App/Services/SessionController.cs` (no `SetSchedule`, `HoldSchedule`; `EndSession(string? earlyEndReason)`), `App/Services/UnlockService.cs` (no hold call)
- Modify: `Core/Engine/LooseningDetector.cs` and `ConfigurationChangeGuard.cs` (remove the schedule section and the "profile deleted while the schedule uses it" rule)
- Modify: `Core/Import/ImportPlan.cs` (remove `Blocks`), `Core/Import/ImportPlanner.cs` (no schedule blocks, no clash notes; imported schedules are left to the blocker), `App/ViewModels/DetectedSourceRowViewModel.cs` (say "its schedule stays in <blocker>"), `App/ViewModels/ProfilesViewModel.cs` (delete check only for the running session), `App/Services/Backup/BackupService.cs` (whatever referenced `Schedule`)
- Delete: `Core/Domain/WeeklySchedule.cs`, `ScheduleBlock.cs`, `ActiveScheduleBlock.cs`, `TimeOff.cs`, `OneOffBlock.cs`; `App/Controls/WeekScheduleGrid.cs`, `ScheduleBlockItem.cs`; `App/ViewModels/ScheduleViewModel.cs`; `App/Views/ScheduleView.axaml(.cs)` (replaced in Task 7)
- Check: `scratchpad/dayplancheck` (extend)

**Interfaces:**
- Produces: `SessionEngine` only starts sessions when asked; `SessionMode.Scheduled` stays (old history and session events keep their meaning).

- [ ] **Step 1: Extend the check**: a `SessionEngine` ticked for 24 simulated hours never starts a session; restoring a checkpoint whose state is `Scheduled` leaves the engine idle and raises `SessionEnded`.
- [ ] **Step 2: Remove** as listed; keep `SessionMode.Scheduled`, `BypassPolicy.EndsOnCommand` and History's scheduled leave-out (old records).
- [ ] **Step 3: Build** (with Task 4): `dotnet build AccountabiliBuddy.sln` → `Build succeeded`, no warnings added.
- [ ] **Step 4: Run the check**: PASS.
- [ ] **Step 5: Stage** (deletions included).

### Task 4: Planned focus comes from the plan's Watch parts

**Files:**
- Modify: `Core/Domain/PlannedFocus.cs` → `record PlannedFocus(string Name, string Color, LocalWindow Window)`
- Modify: `Core/Engine/DayTimeline.cs` → `static IReadOnlyList<PlannedFocus> Planned(DayPlan plan, LocalWindow range)` (Watch parts only)
- Modify: `App/ViewModels/DayBarViewModel.cs`, `App/Services/SessionOverlayService.cs` (colour from `PlannedFocus.Color`, tooltip uses `Name`; "next block" wording becomes "next: Work at 1:00 PM"; nothing else changes until B2)

**Interfaces:**
- Consumes: `DayPlan.PartsIn` (Task 1).
- Produces: `DayTimeline.Planned(DayPlan, LocalWindow)` used by B2.

- [ ] **Step 1: Extend the check**: `DayTimeline.Planned(DayPlan.Default, Mon 06:00–24:00)` returns Work 09–12, Work 13–17, Extra Work 18–20, in order, with the categories' colours.
- [ ] **Step 2: Implement**; build passes.
- [ ] **Step 3: Run the check**: PASS.
- [ ] **Step 4: Stage.**

### Task 5: Sleep mode follows the plan

**Files:**
- Create: `Core/Engine/DayPlanSleep.cs`: `static SleepTimes? SleepOf(DayTemplate template, DayPlan plan)` (the template's Sleep part's start and end, or null) and `static DayPlan WithSleep(DayPlan plan, SleepTimes workdayNight, SleepTimes otherNight)` (replaces or adds the Sleep part in each template, keeping its id when present)
- Modify: `App/Services/SleepSettingsService.cs` (`Task SaveFromPlanAsync(DayPlan plan)`: weeknights = Workday Sleep, weekends = OtherDay Sleep, `SameEveryNight = false`, `Enabled` unchanged)
- Modify: `App/App.axaml.cs` (once, documents "dayplan.sleepSeeded": `WithSleep` from the current sleep settings, save the document; then on every `ConfigurationService.Changed`, `SaveFromPlanAsync`)
- Modify: `App/ViewModels/SettingsViewModel.cs`, `App/Views/SettingsView.axaml` (sleep section: on/off toggle and a "Edit sleep times in the Day plan" button that opens that tab; the time pickers go)

**Interfaces:**
- Consumes: `DayPlan` (Task 1), `ProfileDocument.DayPlan` (Task 2).
- Produces: `DayPlanSleep.SleepOf`, `DayPlanSleep.WithSleep`.

- [ ] **Step 1: Extend the check**: `WithSleep(DayPlan.Default, 23:45–06:45, 01:30–08:30)` gives Workday Sleep 23:45–06:45 and OtherDay Sleep 01:30–08:30; `SleepOf` gives them back; `PartAt(Fri 2026-10-23 23:50)` is not Sleep and `PartAt(Sat 2026-10-24 02:00)` is Sleep (Friday night uses the weekend time, as today).
- [ ] **Step 2: Implement**; build passes.
- [ ] **Step 3: Run the check**: PASS.
- [ ] **Step 4: Stage.**

### Task 6: The day plan editor (control + view model)

**Files:**
- Create: `App/Controls/DayPlanBar.cs` (custom-drawn like `DayBarControl`: 24-hour bar, parts coloured by category with names, drag a part's edge to resize or its middle to move, snap 15 min, clamp so parts never overlap, click empty space to add, click a part to select it; a midnight-crossing part is drawn at both ends with "from the night before" on hover)
- Create: `App/ViewModels/DayPlanEditorViewModel.cs`, `App/ViewModels/PlanPartRowViewModel.cs`
- Create: `App/Views/DayPlanEditorView.axaml(.cs)` (bar on top; under it the selected part's row, or all rows when none is selected: name/label, category picker, start and end time pickers, delete)

**Interfaces:**
- Consumes: Task 1 types, `DayPlanValidator`.
- Produces:
  - `DayPlanEditorViewModel(DayTemplate template, IReadOnlyList<DayCategory> categories)`; `ObservableCollection<PlanPartRowViewModel> Parts`; `PlanPartRowViewModel? Selected`; `void AddAt(TimeOnly start)` (1 hour, first Rest category, shortened to fit); `void Move(Guid partId, TimeOnly start, TimeOnly end)` (snaps, clamps); `void Delete(Guid partId)`; `DayTemplate ToTemplate()`; `string? Error` (from the validator); `event EventHandler? Changed`.
  - `DayPlanBar` styled properties: `Parts` (IReadOnlyList<PlanPartRowViewModel>), `SelectedPartId` (Guid?), and routed callbacks into the view model's `AddAt`/`Move`/select.

- [ ] **Step 1: Write the check** on the view model: `AddAt(10:07)` adds 10:00–11:00 Free time; moving Work to 11:30–13:10 snaps to 11:30–13:15 and stops at Lunch's start; deleting a part removes it from `ToTemplate()`; an invalid edit sets `Error`.
- [ ] **Step 2: Run it**: fails (types missing).
- [ ] **Step 3: Implement** the view model, then the control and view.
- [ ] **Step 4: Run the check**: PASS; open the dev build (`dotnet run --project src/AccountabiliBuddy.App`) and drag a part by hand: it snaps, never overlaps, and the list follows.
- [ ] **Step 5: Stage.**

### Task 7: The Day plan tab

**Files:**
- Create: `App/ViewModels/DayPlanViewModel.cs`, `App/ViewModels/CategoryRowViewModel.cs`, `App/ViewModels/WorkdayChip.cs` (`partial class WorkdayChip(DayOfWeek day)`: `Day`, `Label` "Mon"…, observable `IsOn`), `App/Views/DayPlanView.axaml(.cs)`
- Modify: `App/MainWindow.axaml` (tab "Schedule" → "Day plan", same index), `App/ViewModels/MainWindowViewModel.cs`, `App/App.axaml.cs`

**Interfaces:**
- Consumes: Task 6 editor, `ConfigurationService.UpdateAsync`.
- Produces: `DayPlanViewModel(ConfigurationService configuration, SessionController sessions)` with `Workdays`/`Other days` tabs (one `DayPlanEditorViewModel` each), day chips (`ObservableCollection<WorkdayChip>`), a Categories list (add, rename, colour, behaviour Watch/Rest/Sleep, delete: refused while a part uses it), `Save`/`Revert`, `IsLocked` = a focus session the user started is running.

- [ ] **Step 1: Implement**; Save writes `document with { DayPlan = … }` (applies at once), refused with the validator's message when invalid.
- [ ] **Step 2: Check by hand** in the dev build: change today's Work end and Save; the day bar's planned lane updates at once (Review Focus 4); start a manual session: the tab locks; Revert restores.
- [ ] **Step 3: Stage.**

### Task 8: One-off days in Exceptions

**Files:**
- Modify: `App/ViewModels/ExceptionsViewModel.cs`, `App/Views/ExceptionsView.axaml` (the Time off and One-off blocks sections become **One-off days**: pick a date, it starts as that date's usual template, edit it with `DayPlanEditorView`, optional note, Save, Delete; past days are listed greyed and removable)

**Interfaces:**
- Consumes: Task 6 editor; `DayPlan.OneOffDays`.

- [ ] **Step 1: Implement**; saving stores `OneOffDay(Guid, date, template, note)` in the document.
- [ ] **Step 2: Check by hand**: a one-off day for tomorrow with only Free time; the dayplancheck program pointed at a copy of the dev data shows `PartAt(tomorrow 11:00)` = Free time and no Sleep the night before (Review Focus 5).
- [ ] **Step 3: Stage.**

### Task 9: The phone reads v5

**Files:**
- Create: `android/.../model/DayPlan.kt` (DayCategory, CategoryBehavior, PlanPart, DayTemplate, OneOffDay, DayPlan with `partsIn(range)` / `partAt(time)` mirroring Task 1, same midnight and cross-day rules)
- Modify: `android/.../model/ProfileDocument.kt` (`CURRENT_SCHEMA_VERSION = 5`; `schedule` replaced by `dayPlan`), `data/ProfileDocumentParser.kt` (parses `dayPlan`; a v4 document gets the default plan), `model/PlannedFocus.kt` (`PlannedFocus(name: String, color: Long, window: LocalWindow)`), `engine/DayTimeline.kt` (`planned(plan, range)` = Watch parts), and every user of the old schedule: `engine/DayBarBuilder.kt`, `engine/ProfileColors.kt`, `notify/Refresher.kt`, `ui/CompanionViewModel.kt`, `ui/ProfilesScreen.kt` (schedule lines go), `ui/TodayScreen.kt`, `usage/UsageIntervals.kt`, `widget/Upcoming.kt`, `link/PcState.kt`
- Delete: `android/.../model/WeeklySchedule.kt`, `model/TimeOff.kt`

**Interfaces:**
- Consumes: the v5 JSON from Task 2.
- Produces: Kotlin `DayPlan` used by B3.

- [ ] **Step 1: Implement**; `./gradlew assembleDebug assembleRelease` → `BUILD SUCCESSFUL`.
- [ ] **Step 2: Check on the AVD** (emulator-5554) with the PC dev build linked: Today shows Work/Extra Work as the planned blocks; ProfilesScreen shows no schedule lines; an old release APK (0.1.44's) linked to the v5 PC shows "These profiles come from a newer AccountabiliBuddy (schema 5). Update the phone app first." and the PC card stays connected (Review Focus 3).
- [ ] **Step 3: Stage.**

### Task 10: Docs, install, verify

**Files:**
- Modify: project `CLAUDE.md` (schema v5 with day plan; no scheduled sessions; sleep from the plan; Exceptions = one-off days; documents key "dayplan.sleepSeeded"; roadmap), `doc/plans/2026-10-20-body-double-design.md` (B1 done)

- [ ] **Step 1: Update the docs.**
- [ ] **Step 2: Package and install** `pwsh scripts/package.ps1 -Version 0.1.45` (the install interrupts a running session briefly); the phone gets the new release APK.
- [ ] **Step 3: Verify on the real data**: the app starts on schema 5 (backup `before-database…`/`before-version-0.1.45-*` exists), the Day plan tab shows the seeded plan with Sleep 23:45–06:45 / 01:30–08:30, the day bar shows Work parts as planned, no session starts by itself at a former block time, the phone's Today shows the plan's Work parts.
- [ ] **Step 4: Commit and push on the user's OK** (with ADR 18, staged earlier).
