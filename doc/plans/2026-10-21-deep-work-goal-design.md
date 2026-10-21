# Deep-work goal: design

Date: 2026-10-21. Status: **decided by the user on 2026-10-21; build after the device sync batch** (the goal changes the day plan's saved format, which the sync work reads and writes). Needs an ADR (plan schema v6).

## Why

The legend under the day bar (built 2026-10-21, `d1f4dbc`) shows "Plan ● Work 7h 00m ● Extra Work 2h 00m" and "Focus ● 3h 10m focused". The user wants focus measured against how many deep-work hours they aim for, with a default they can change per day, and to see the weekly deep work in History.

## Decisions (the user's)

- **A goal to reach.** The Focus line reads "● 3h 10m of 4h 00m deep-work goal" and turns green once reached. Going over is fine; no ceiling or warning.
- **A bar that fills toward the goal** *(user, 2026-10-21: "it helps humans … if you can see the results of something grow")*: on the legend's Focus line (the gray panel under the day bar), a progress bar in the Focus colour fills with the focused time; full and green once the goal is reached. Only when a goal is set; without one, the line stays text only.
- **A default in Settings**, plus overrides **per day type and per one-off day** in the day plan: Workdays and Other days can each set their own goal, and so can any one-off day in Exceptions. Anything unset uses the default.
- **Weekly deep work in History**, for stats: how much deep work each week, against the week's goals.
- **When:** after the device sync batch.

## My calls (to confirm when building)

- **No goal anywhere:** the Focus line stays "● 3h 10m focused".
- **Default goal:** none until the user sets one (Settings → Your day).
- **Where the overrides live:** in the day plan JSON as optional fields on the Workdays / Other days templates and on one-off days. The goal travels with the plan to the phone and through sync.
  - This bumps the plan schema to **v6**, with a migration where missing = use the default.
  - The phone must read v6 (ADR 7 rule).
  - Needs a new ADR.
- **History:** a "Deep work" line per week (sum of counted focus sessions) next to the week's total goal (sum of each day's goal), in the week view.
- **Phone and watch:** later (the phone shows the goal once it reads v6).
