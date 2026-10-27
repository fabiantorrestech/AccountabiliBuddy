# 47. Day plan modes

Date: 2026-10-27

## Status

Accepted

Amends [18. Leave enforcement to the blockers](0018-leave-enforcement-to-the-blockers.md) (what the plan says about when the buddy watches), [28. Close the work-hours guard's free exits](0028-close-the-work-hours-guards-free-exits.md) (which plan changes free today's work), [31. Guard pre-work parts](0031-guard-pre-work-parts.md) and [33. Plan sleep by bedtime and wake by the alarm](0033-plan-sleep-by-bedtime-and-wake-by-alarm.md) (sleep per weekday).

## Context

The day plan has one workday template, one off-day template, a set of workdays and one-off dates (schema v11). Every guard (the work-hours guard, Pre-Work, lights out, the work nudge, the poll speed, the day bar) reads the plan's work parts directly. The user doesn't plan every day the same way. Some days have fixed hours, some only a target number of productive hours, some are free, and their weeks alternate. They also find a full timeline too much to fill in when all that matters is a work window (C 15). The user chose four modes on 2026-10-27:
- Schedule (today's timed parts, or just a work window);
- Budget (N productive hours from wake to bedtime, minus the non-productive windows they plan, with lunch kept);
- per-weekday plans with alternating A/B weeks (a start Monday plus a quick switch);
- Free day.

## Decision

The plan becomes schema v12:
- **Named templates.** The current Workday and Off day are migrated as the first two.
- **A week map** of weekday to template for week A, with an optional week B that alternates from a chosen Monday.
- **A mode on each template:** Schedule (parts, or a simple work window), Budget (a number of minutes, with its parts read only as non-productive windows) or Free.

One resolver picks a date's template, in order: one-off day, that week's map, then Off day. A single work-window engine gives every guard a day's guarded windows by mode:
- **Schedule:** the work parts or the work window.
- **Budget:** waking time minus the non-productive windows, until that day's counted focus reaches the budget, with a pace figure.
- **Free:** none.

All guard code reads only this engine, never the parts directly. A change that lowers today's guarded work waits as ADR 28 already says, now including:
- a lower budget;
- longer non-productive windows;
- switching to Free;
- re-mapping today's weekday;
- switching the A/B week.

More work applies at once.

## Consequences

The user can describe real weeks: alternating rotas, budget days and free days. A simple work window is enough to make the guards work, so onboarding gets lighter. Sleep gets per-weekday nights for free, because nights already come from each day's template.

It is a schema change on both the PC and the phone, with a migration and an identical plan-version hash on both sides. An older phone refuses a v12 plan and asks for an update. Every guard moves onto the new engine at once, so a mistake there shows up in all of them. The Budget mode ties enforcement to counted focus, so whatever counts as focus (never test sessions, side tasks or planning) now also decides when the guard eases off. Undoing the modes later would need a v13 migration back to two templates.

Amended 2026-10-27 (Setup DP Assistant, `doc/plans/2026-10-27-dp-assistant-plan.md`): schema v13 replaces the A/B week map with a repeating cycle of 1 to 8 weeks from a chosen Monday (A only becomes a 1-week cycle, A/B a 2-week cycle, every date resolving as before), and a lunch part can be a fixed period that starts by itself, with a prompt when a session would run into it.
