# Block re-arm and the day planner walkthrough: plan

Date: 2026-10-25 · Status: proposed (awaiting the user's OK)

## Goals

1. **Re-arm blocks.** Notice when a Curbox block (phone) or a Cold Turkey block (PC) is off and turn it back on at the
   day's re-enable time, with rules that differ by day (Workdays, Off-Days, one-off days) because they live in the day
   plan.
2. **Day planner walkthrough** on the PC and the phone: three questions that make one-off days (an adjusted workday or
   a day off), with their sleep and block rules.

## Decisions (user, 2026-10-25)

| Question | Answer |
|---|---|
| When a turned-off block comes back on | At the day's re-enable time (midnight by default); a heads-up notification on that device first ("Blocks come back on in 5 min (10:31 PM): Social, Reels"), on by default, can be turned off |
| Lift windows | Two kinds: **Allow** (default: you may turn the blocks off yourself in the window) and **Auto** (the app turns them off at its start); both come back on when the window ends |
| "Adjusted workday" | A one-off day copied from Workdays with the answers applied (Off-day: from Off-Days); templates unchanged |
| Where rules live | In the day plan: Workdays, Off-Days and each one-off day carry their block rules (schema v10, ADR 37) |

## What the blockers allow

- **Curbox (phone), API v2:** `list` gives app blocker, keyword and grayscale groups with `isActive`, and the keyword
  and reel blockers' state; `execute` turns each on or off (`SET_APP_BLOCKER_GROUP`, `SET_KEYWORD_GROUP`,
  `SET_KEYWORD_BLOCKER`, `SET_REEL_BLOCKER`, `SET_GRAYSCALE_GROUP`). It doesn't expose a group's own schedule:
  rules are per group, Curbox's schedule still runs inside it.
- **Cold Turkey (PC):** which blocks are on comes from the copy of its settings we already read; its command line
  `-start "<block>"` turns one on and `-stop "<block>"` off (only when it isn't locked). Its settings file is never
  touched (existing rule).

## Block rules (schema v10)

A day template (and a one-off day) gets optional `blocks`:

| Field | Meaning | Default |
|---|---|---|
| `guarded` | The blocks this day keeps on: refs `{device, kind, id, name}` | Every block that was on when rules were first set (all of them) |
| `reenableAt` | When anything guarded that's off comes back on | 00:00 (midnight, end of the day) |
| `warnMinutes` | Heads-up before it does, on the blocker's device; null = off | 5 |
| `lifts` | Windows `{start, end, mode: allow/auto, blocks}` | none on Workdays; Off-Days: none (the walkthrough adds them) |

Behaviour, on the device that has the blocker (the phone for Curbox, each PC for its Cold Turkey):

- **Off outside a lift window:** stays off; at `reenableAt` it's turned back on. Heads-up `warnMinutes` before.
- **Allow window:** you may turn its blocks off; when the window ends they're turned back on (heads-up before).
- **Auto window:** its blocks are turned off at the start and back on at the end (heads-up before the end).
- A block that's off for its own reasons at re-enable time (Curbox's schedule, a locked Cold Turkey block) is left to
  its blocker: only "on/off" is ever set, never its settings or schedule.
- The day whose rules apply is the plan date the time belongs to (a window past midnight belongs to the day it
  started); a re-enable at 00:00 is the end of that day.
- Each re-enable or lift is logged in a small history ("Turned Social back on, 12:00 AM").

**To the other devices:** each device's sync card gets an optional `blockers` list (names, kinds, ids and on/off of
its Curbox groups or Cold Turkey blocks), so the walkthrough and the rules editor on any device can list every
device's blocks. Names only; no apps, sites or keywords inside them.

## Day planner walkthrough (PC and phone, same steps)

Opened from Today (and the Day plan tab / phone's Edit plan): "Plan a day".

1. **What kind of day(s)?** Adjusted workday(s) · Off-day(s), and the date(s) (tomorrow by default; several allowed).
2. **Sleep:** bed by, wake at, hours of sleep (shown as you change them), and "Follow my alarm that morning"
   (wake by alarm, ADR 33; on if the template has it). Defaults from the template.
3. **Blocks:** each device's blocks (phone: Curbox groups; each PC: Cold Turkey blocks), tick the ones to lift; for
   each lift: the window (default: all day until the re-enable time), Allow or Auto (default Allow), the re-enable
   time (default midnight) and the heads-up (default on, 5 min).
4. **Review and save:** a summary per date; Save makes the one-off days (an existing one-off day for a date asks
   first). Locked during a focus session, like the Day plan tab.

## Tasks

1. ADR 37 (block rules in the plan, re-arming through Curbox's API and Cold Turkey's command line, `blockers` in the
   sync card) + project CLAUDE.md lines.
2. Schema v10 (C# Core + Kotlin model/parser/writer, migration: none), the `blockers` sync field; cross-language checks.
3. Re-arm engine (pure, both sides): given the plan, the time and the blockers' states, what to turn on/off and when
   to warn; checks (midnight, windows over midnight, allow vs auto, one-off days, warn timing).
4. Phone: Curbox control (`execute`), the guard loop (on refresh + exact alarms at warn/re-enable times), the
   heads-up notification, history; Settings → Phone guard → "Block rules" (per day type).
5. PC: Cold Turkey `-start`/`-stop`, the same loop and a toast heads-up; Day plan tab → "Block rules" per template;
   Settings switch for the heads-up.
6. Walkthrough on the PC (a wizard window) and the phone (a full-screen flow), sharing the plan model.
7. Test on the Pixel 5 (Curbox installed there with throwaway groups) and the PC; ship PC + phone.

## Left for later

Curbox per-group schedules (needs Curbox to expose them), LockMyPhone-Extended locks in the rules, a weekly view of
lifts.
