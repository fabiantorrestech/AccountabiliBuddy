# Phase 8: day plan modes — Implementation Plan (draft, build after phase 6)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** the day plan gets four modes the user can mix per day:
- **Schedule:** today's timed parts. Per C 15 it can be just a **work window** (e.g. 9:00 AM–5:00 PM); the full timeline becomes optional.
- **Budget:** N productive hours, plus the non-productive windows the user plans.
- **Per weekday + alternating A/B weeks.**
- **Free day:** no guard.

**Spec:** feature-list plan, "Day plan rework" row and the user's answers of 2026-10-27; the user's note C 15.

**ADR:** a new ADR "Day plan modes", written before Task 1. It amends ADR 18 (the plan only says when the buddy watches), 27/28 (the work-hours guard follows the day's mode; budget holds), 31 (Pre-Work on a budget or free day) and 33 (sleep per weekday).

## Global Constraints
- Profiles/plan schema **v12** (ADR 7), with a migration in `ProfileDocumentSerializer`.
  - The current `Workday`/`OtherDay` become two named templates.
  - `Workdays` becomes the week-A weekday map, and week B copies A.
  - Nothing changes for an existing user until they edit.
- The phone mirror (`model/DayPlan.kt`, `data/DayPlanJson.kt`, `DayPlanVersion` normalisation on both sides, `DayPlanChanges.Describe` wording on both) changes in the same task as the PC model, so plan edits and clash checks keep working.
- Stricter now, looser waits (ADR 28's hold, generalised; see Task 3). Never during a session.
- Times 12-hour. Material icons.

## Model (v12)
- `DayPlan.Templates`: a list of `{ Id, Name, Template }`. The defaults are "Workday" and "Off day".
- `DayPlan.Week`: `{ A: DayOfWeek → templateId, B?: DayOfWeek → templateId, BStartsOn?: date (a Monday) }`. With B set, weeks alternate from `BStartsOn`.
- `DayTemplate.Mode`: `Schedule | Budget | Free`. The default is Schedule.
  - `DayTemplate.WorkWindow?` `{start, end}` holds a Schedule day's simple work window (C 15). When it is set and there are no work parts, the window *is* the work part.
  - `DayTemplate.BudgetMinutes?` is for Budget days. Budget days use their `Parts` only as the **non-productive windows** (rest, sleep and free categories) plus Pre-Work.
- The resolver `TemplateFor(date)` goes, in order:
  1. a one-off day;
  2. that week's (A/B) template for the weekday;
  3. "Off day".
- It is the single resolver on both sides (`DayPlan.cs` `TemplateFor`, `model/DayPlan.kt` `templateFor`/`isWorkday`).

## Tasks

### Task 1: Model, migration, resolver (PC Core + phone mirror)
**Files:** `Core/Domain/DayPlan.cs`, `DayTemplate.cs`, new `PlanTemplate.cs`/`PlanWeek.cs`/`DayMode.cs`, `Storage/ProfileDocument.cs` (v12), `ProfileDocumentSerializer.cs` (+ a `DayModesMigration.cs`), `Engine/DayPlanVersion.cs`, `Engine/DayPlanValidator.cs`, `Engine/DayPlanChanges.cs`, and the phone's `model/DayPlan.kt`, `data/DayPlanJson.kt`, `data/ProfileDocumentParser.kt`, `engine/DayPlanValidator.kt`, `engine/DayPlanChanges.kt`.
- [ ] Commit `Day plan v12: named templates, A/B weeks, modes (Schedule/Budget/Free), work window`.

### Task 2: One work-window engine for every consumer
- A new `Core/Engine/DayWorkWindows.cs`: `WorkWindows(plan, date, focusedSoFar, now)` gives a day's guarded windows by mode:
  - **Schedule:** work parts, or the work window.
  - **Budget:** waking time minus the non-productive windows, until `focusedSoFar ≥ budget`.
  - **Free:** none.
- Plus `Pace(…)`: the budget left and the available time left, with a "behind" flag when available < left.
- Replace every direct `DayTimeline.Planned` / `kind == "watch"` use with it:
  - `WorkHoursGuard` (WorkLeftToday, GuardingUntil, DescribeCut, earned time), `StartNudgeService.CurrentWorkPart`, `ShutdownService`, `SyncDriveService.InWorkHours`, `DayBarViewModel`, `CommsWindows`, `LunchService`, `QrSyncService`, `FocusDayBuilder` plan parts (Budget days send their guarded windows as "watch" parts, so the phone and watch need no new logic) and the goal's `GoalLowerWaits`.
- Pre-Work runs only before a Schedule day's first work part, or a Budget day's first guarded window. A Free day has none.
- [ ] Commit `One work-window engine: Schedule, Budget (with pace), Free`.

### Task 3: Looser changes wait (ADR 28 generalised)
- `ConfigurationService.DayPlanHold` and `DayPlanCut`: a change that lowers **today's guarded work** is held while the guard has work left and the goal isn't met.
- What lowers it: switching today to Free or Budget→Schedule-with-less, lowering the budget, adding or lengthening non-productive windows, re-mapping today's weekday or A/B week, or moving `BStartsOn`.
- More hours, shorter windows, Free→Budget and adding work apply at once.
- [ ] Commit `Plan changes that free today's work wait, also for budgets and week maps`.

### Task 4: PC UI
- Day plan tab:
  - template list (add, rename, duplicate, delete);
  - a week grid of Mon–Sun × A/B with a template picker per cell, plus an "Alternate weeks" switch and the start date;
  - per template a mode picker:
    - Schedule: "Just a work window" with two times, or the full timeline;
    - Budget: hours stepper and the non-productive windows editor (the existing parts editor, limited to rest/sleep/free categories);
    - Free.
- Today: the Budget pace line ("2h 10m left · 5h of day left", amber when behind) on the goal card and the overlay.
- The setup assistant gets a "Your workdays" step: the work window (C 15), or Budget hours.
- [ ] Commit `PC: day plan templates, week grid, modes, work window; Budget pace on Today`.

### Task 5: Phone UI (+ watch display)
- The phone's day plan editor mirrors Task 4: templates, week grid, mode, work window and budget.
- The phone shows the pace line on the PC card.
- The watch shows nothing new; Budget windows arrive as plan parts.
- [ ] Commit `Phone: day plan templates, week grid and modes`.

## Review Focus
1. Migration: a v11 plan becomes v12 with the same guarded hours every day of the week; an old phone reading a v12 plan refuses cleanly ("update").
2. Budget: the guard eases off exactly when the budget is met, never before. Focus from a test session or a side task never counts toward it.
3. A plan change that frees today's work applying at once, through any of the new fields.
4. Sleep: per-weekday templates give per-night bedtimes; the sleep settings' weeknight/weekend fallbacks stay.
5. Clash detection: `DayPlanVersion` on PC and phone hashes the new fields identically.

## Execution
After phase 6. Tasks 1 → 2 → 3 run one after the other (each builds on the last); 4 and 5 run in parallel after 3. Implementers: Sonnet high; reviewers: Opus medium (schema, guard).

## Open questions for the user (ask before building)
- Budget's "waking time": from the night's wake time to bedtime (sleep plan), or a fixed day window (e.g. 8 AM–10 PM)?
- Does a Budget day keep a lunch (flexible break) like a Schedule day?
- A/B weeks: is a custom start week enough, or do you also need "this week is B" quick-switch on Today?
