# Setup DP Assistant — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** the user's "Setup DP Assistant" (spec: `doc/plans/2026-10-27-dp-assistant-spec.md`): a guided day-plan assistant with sensible defaults, opened from a button at the top right of the PC Day plan tab, next to the refresh button. It needs two foundations first:
- a repeating cycle of up to 8 weeks;
- a fixed lunch period.

**ADR:** amend ADR 47 (day plan modes) with the N-week cycle (schema v13) and fixed lunch.

## Global Constraints
- Implementer rules: `.superpowers/sdd/implementer-rules.md`.
- Profile schema **v13**, with a migration in `ProfileDocumentSerializer`:
  - v12 week A only → a 1-week cycle;
  - A + B → a 2-week cycle;
  - every date resolves exactly as before.
- Keep writing the legacy v11 fields (projected from week 1) for older apps. The sync-file profiles copy stays stamped 11 (the release fix).
- `DayPlanVersion` hashes the same on PC and phone.
- Looser waits (ADR 28/47 hold); locked during a session; 12-hour times; Material icons; no new dependencies.

## Task 1: Model v13 — N-week cycle and the fixed-lunch flag (PC Core + phone mirror)
- `DayPlan.Week` becomes `Cycle`: `{ Weeks: [map1 … mapN] (1–8), StartsOn: Monday }`.
  - The resolver picks week `((weeksSince(StartsOn)) mod N) + 1` with floor division, as the A/B parity does.
  - Validator: 1–8 weeks; every map's template ids exist; StartsOn is a Monday.
- The lunch part gets `AutoStart` (bool, default false). True means a **fixed lunch period**: lunch starts by itself at the part's start. The flexible lunch stays as today.
- Both mirrored on the phone (model, parser, JSON writer, validator, `DayPlanChanges` wording on both sides).
- "This week is A/B" generalises to "This is week N of M". It moves `StartsOn` by whole weeks; that's a looser-change hold candidate, as now.
- PC and phone week grids show N columns, with an add/remove-week control.
- [ ] Commit `Day plan v13: repeating cycle of up to 8 weeks; fixed lunch flag`.

## Task 2: Fixed lunch period — behaviour (PC; phone displays)
- **At the part's start**, when `AutoStart` is on and it's a workday, `LunchService` starts lunch by itself, as if the user pressed Start lunch. Its length is the part's length, and it syncs like any lunch.
  - It doesn't start while the user is away or in a test session.
  - A session running then is held by lunch, as a started lunch already does.
- **Run-into-lunch prompt.** When a real session would overlap or end within 1 minute of a fixed lunch, the prompt offers:
  - **Keep:** schedule it against lunch;
  - **Adjust:** shorten the session to end at lunch's start;
  - **Flexible this time:** today's lunch doesn't auto-start, and the user must press Start lunch before going. The note says so.
- **When the prompt shows:**
  - at session start (Quick Start / Start / phone start, for the length chosen);
  - again 5 minutes before lunch, if the session still runs into it (a front card; one per session).
- **Phone:** Today's lunch line says "Lunch starts at 12:00 PM by itself" for a fixed lunch. The phone doesn't prompt.
- [ ] Commit `Fixed lunch: starts by itself; a session that runs into it asks first`.

## Task 3: The Setup DP Assistant (PC)
- **Button:** "Setup DP Assistant" at the top right of the Day plan tab, next to the refresh button. It can be re-run at any time; it's locked during a session like the editor.
- **Steps** (each prefilled from the current plan, else the defaults):
  1. **Workdays:** any days; Mon–Fri by default.
  2. **Work time:** a work window (default 9:00 AM–5:00 PM) or a Budget of hours. The text explains it is the time at work, or the window to stay on task, not the focused hours.
  3. **Deep work goal:** default 1 h, setting the daily goal. Cal Newport guidance: most top out around 4–4.5 h of deep work after long practice; start low if new.
  4. **Lunch:** a window (flexible) or a fixed period (Task 2), with times; default flexible, 1 h, hint 12:00 PM.
  5. **Sleep:** the bedtime setup's options (bedtime, wake from the alarm, smart bedtime…), or Skip with "sleep features won't work".
  6. **Weeks:** the same every week, A/B, or a custom cycle of 2–8 weeks. Per week, which template each weekday uses.
  7. **Review:** plain-language bullets of how AccountabiliBuddy behaves through each kind of day, generated from `DayWorkWindows` and the plan. For example:
     - "Before 9:00 AM: Pre-Work…"
     - "9:00 AM–5:00 PM: work hours guarded until 1 h of focus…"
     - "12:00 PM: lunch…"
     - "After 5:00 PM: stays out of your way"
     - "10:30 PM: bedtime reminder"
     - plus anything held because it's looser.
  - Then **Save** (through `ConfigurationService`) or **Back**.
- It reuses the setup assistant's "Your workdays" step where it fits. SetupFeatures entry (0.1.112).
- [ ] Commit `PC: Setup DP Assistant (guided day plan with sensible defaults)`.

## Review Focus
1. The v12→v13 migration changes a date's template, or the PC and phone pick different cycle weeks for a date.
2. A fixed lunch auto-starting during a session that the user chose to Keep, or not starting when it should; or "Flexible this time" leaving lunch never started (it's the user's job then, and the note must say so).
3. The assistant saving a looser plan at once; the hold must apply.
4. Prompts stacking with Ready? / Pick / the start-of-day assistant (FrontCardHost ordering).

## Execution
Task 1, then Task 2 (it uses the flag), then Task 3 (it uses both). Sonnet high implementers; Opus medium reviewers for Tasks 1–2 (schema, guard), Sonnet high for Task 3.
