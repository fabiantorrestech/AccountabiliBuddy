# 33. Plan sleep by bedtime and wake by the alarm

Date: 2026-10-24

## Status

Accepted

Amends [23. Use the phone's next alarm as the wake time](0023-use-the-phones-next-alarm-as-the-wake-time.md)

## Context

The day plan's Sleep part has a bedtime and a wake time. Since ADR 23 the phone's next alarm already overrides the wake time when one is set, but the plan still asks for a wake time, and on a morning with no alarm the night ends at that hand-set time. The user wakes by an alarm app they choose on the phone and changes it from day to day, so the planned wake time is a number they keep fixing for nothing. On 2026-10-24 they asked to plan sleep the other way round: say when to be in bed by, and let the chosen alarm clock app set the end of the night, so that later the app can reward going to bed in time to reach a sleep goal. They chose:

- with no alarm that morning, the night ends at bedtime plus the sleep goal;
- the alarm is the wake-up alarm app already chosen on the phone (Devices: pinned, or learned), not a separate choice in the plan;
- it's a switch per template (Workdays and Other days separately).

## Decision

A Sleep part gets an optional flag, `wakeByAlarm` (profile schema v9; written only when true; an older plan has none, so nothing migrates). With it on:

- the morning's wake time is the wake-up alarm when one falls between bedtime and noon, as in ADR 23; a one-night change confirmed over the alarm still wins;
- with no alarm, a one-night change counts, else the night ends at bedtime plus the sleep goal ("sleep" settings, 8 hours by default);
- only the bedtime is the user's. The part's stored end is kept at bedtime plus the goal by both editors (the PC's Day plan tab and the phone's Edit plan), so everything that draws or validates the plan by its parts keeps working unchanged.

The flag travels with the day plan wherever it already goes: the shared profile JSON, the sealed sync files and the ADR 14 link. The PC and the phone apply the same rule in their `SleepSchedule`. Off is the old behaviour.

## Consequences

The user plans one time per night, and the sleep line, the day bar's night and the countdown follow the real alarm, falling back to a full night's sleep instead of a stale hand-set wake time. Bumping the schema means an older app refuses a v9 plan: the PC and the phone have to be updated together, and a phone build from before this can't read a v9 plan until it is updated. The stored end only follows the goal when a part is edited, so after the goal changes, the Plan row draws the old end until the part is saved again; the night itself always uses the current goal. Rewards for growing the time in bed toward the goal are left for later and need their own design.
