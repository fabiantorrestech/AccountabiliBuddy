# 40. Estimate the wake time from past alarms

Date: 2026-10-26

## Status

Accepted

Amends [23. Use the phone's next alarm as the wake time](0023-use-the-phones-next-alarm-as-the-wake-time.md) (what a paired phone shares) and [33. Plan sleep by bedtime and wake by the alarm](0033-plan-sleep-by-bedtime-and-wake-by-alarm.md) (the wake of a night with no alarm)

## Context

With ADR 33 a Sleep part can wake "by the alarm": the morning's wake time is the phone's next alarm, and a morning without one ends at bedtime plus the sleep goal. Android only tells an app the soonest alarm, so every morning past the next one, and every morning the user sets no alarm, falls back to that sum. The user (2026-10-26) saw a fixed 8 AM wake (a midnight bedtime plus 8 hours) that has nothing to do with when they really get up, and it differs between workdays and days off. The phone already keeps what it has seen of alarms (ADR 23's sightings, to learn the wake-up app) and reads Health Connect sleep (ADR 34), so it holds the data for a better guess. The PC doesn't, and the hard rules only let a phone share named, derived numbers.

## Decision

A morning woken by the alarm with no alarm known for it takes the phone's estimate for that kind of morning (Workdays or Off-Days, by whether the day plan lists the morning's weekday as a workday; one-off days follow their weekday). The estimate is the median wake of the last 8 mornings of that kind on which the wake-up app's alarm (pinned, else learned) fell in the morning window, the earliest alarm of each morning counting, when there are at least 3 such mornings; else the median of Health Connect's measured wake times (sleep session ends) of that kind under the same rule; else there is none and the wake stays bedtime plus the goal. An estimate that falls before the night's bedtime, or at noon or later, is ignored. The phone works it out and uses it itself; only the two times leave it, as `wakeEstimate: { workdays: "HH:mm", offDays: "HH:mm" }` (each optional) in its sealed sync file, and the PCs use the newest phone file's. The phone keeps 60 alarm sightings instead of 30 so that eight mornings of each kind fit beside calendar reminders.

## Consequences

Days ahead and alarm-free mornings show a wake near when the user really gets up, on the PC and the phone alike, and the PC alarm (ADR 35) rings at it. The estimate trails a change of habit by up to eight mornings of that kind. A PC with no paired phone, or a phone with too little history, behaves exactly as before. The sync file carries two more derived times, never alarm lists, sleep sessions or app names.
