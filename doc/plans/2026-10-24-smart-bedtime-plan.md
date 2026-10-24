# Smart bedtime: plan

Date: 2026-10-24 · Status: proposed (awaiting the user's OK)

## Goal

Know the wake time (alarm or plan), set a minimum, goal and maximum sleep, and let the app pick tonight's bedtime from
how the user really sleeps, moving it earlier as it's earned: first to reliably get the minimum, then toward the goal.

## Decisions (user, 2026-10-24)

| Question | Answer |
|---|---|
| Pace | 15 min earlier per earned week (made it on most nights); stays otherwise; 15 min back after two missed weeks |
| Control | Automatic, shown with its reasons; the hand-set bedtime is the latest allowed |
| Evidence | Watch sleep (Health Connect) + the phone's last pickup before it (Usage access) |
| Nights | Workdays and Off-Days learn and set apart |

## How it works

**Settings ("sleep", already synced between devices):** goal (8 h, exists), new minimum (6 h 30 m) and maximum (9 h),
and a "Smart bedtime" switch per template (Workdays, Off-Days; off by default, then turned on for the user). No
profile schema change: the switches live in the sleep settings, not the plan.

**The level is time in bed before waking, not a clock time**, so a different alarm each morning still works:
tonight's bedtime = tomorrow's wake time − level, never later than the hand-set bedtime.

**Learning, per template, on the phone** (`SmartBedtime`, pure Kotlin, with checks):
- Each night (named by its evening, as `SleepWeek`): fell asleep (first Health Connect session), slept (sessions
  merged), last pickup before falling asleep (within 3 h), the bedtime that was asked.
- Time to fall asleep = median of (fell asleep − last pickup), 5 to 60 min, 15 min until known.
- Start (needs 5 nights of that template in the last 21 days; until then the hand-set bedtime and "learning, 3 of 5
  nights"): level = median of (wake − last pickup), capped at goal + time to fall asleep.
- Made it = phone down (last pickup) by the bedtime + 10 min; without pickup data, asleep by bedtime + time to fall
  asleep + 10 min.
- Each Monday: last week's nights of that template made on at least 80% (4 of 5 workdays; both Off-Days) → level
  +15 min, up to goal + time to fall asleep (never past maximum + time to fall asleep). Under 50% two weeks running
  → −15 min, never below where it started. Kept in prefs "smartBedtime" with each night's result (60 days).

**Where tonight's bedtime is used:** the phone's and PC's sleep schedule take it as that evening's bedtime (like the
alarm sets the wake): the day bar's night, the "Sleep: 7h 10m • Wake: 6:45 AM" header, the bedtime reminder and the
countdown. Shown with its reasons in phone Settings → Sleep and PC Settings → Sleep, e.g. "Workdays: 7 h 15 m in bed
→ tonight 11:30 PM · last 2 weeks asleep 11:48 PM avg, 6 h 52 m · this week 3 of 4 · next step Monday", and on the
Sleep part in both day plan editors ("Smart: 11:30 PM tonight").

**To the PCs:** the phone's sealed sync file gets an optional `smartBedtime`: per template the level, tonight's bedtime
and the line above's numbers (averages and counts only; no sleep sessions, no pickup times). Needs ADR 34 (data from
Health Connect leaving the phone, derived only), amending ADR 22/23.

## Tasks

1. ADR 34 + project CLAUDE.md lines (sleep settings, sync field, phone prefs).
2. Sleep settings min/max + smart switches, PC and phone, synced in the "sleep" document; Settings → Sleep controls.
3. Phone: Health Connect read back 21 days; pickups per night from Usage access; `SmartBedtime` engine + Monday step;
   ktcheck checks (start, made it, step up/down, caps, a different alarm each night, learning state).
4. Phone + PC sleep schedule take tonight's smart bedtime; header, reminder, countdown follow; checks both sides.
5. Sync field `smartBedtime` (C# and Kotlin, optional, cross-language sample check); the PC shows the status.
6. Day plan editors show "Smart: … tonight" on Sleep parts; turn it on for the user's Workdays and Off-Days; ship
   PC + phone together.

## Left for later

Rewards for making bedtime (streaks, bonus framing) and a weekly sleep report card.
