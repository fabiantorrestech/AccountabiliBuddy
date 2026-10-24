# 34. Learn a smart bedtime on the phone

Date: 2026-10-24

## Status

Accepted

Amends [22. Share progress between your own devices through sealed files](0022-share-progress-between-your-own-devices-through-sealed-files.md) and [23. Use the phone's next alarm as the wake time](0023-use-the-phones-next-alarm-as-the-wake-time.md)

## Context

With ADR 33 the user sets only a bedtime and wakes by the alarm. They asked (2026-10-24) for the app to pick that bedtime itself from how they really sleep: knowing the wake time, a minimum and a goal (and a maximum), it should start from what they actually do and move earlier as it's earned, first until they reliably get the minimum, then toward the goal. They chose 15 minutes earlier per week in which they made it on most nights (and 15 minutes back after two missed weeks), automatic changes shown with their reasons with the hand-set bedtime as the latest allowed, evidence from the watch's measured sleep and the phone's last use before it, and Workdays and Off-Days learned apart.

The evidence lives on the phone: Health Connect sleep (from the Pixel Watch) and Usage access. Neither may leave it raw, and the phone keeps its own settings (it receives the PC's sleep settings but never sends its own back).

## Decision

The phone learns and keeps the smart bedtime (prefs "smartBedtime", in its settings backup): the switches per template (Workdays, Off-Days; off by default), the minimum (6 h 30 m) and maximum (9 h), each night's record (fell asleep, slept, phone down, the bedtime asked, made it; 60 days) and per template the level. The level is time in bed before waking, not a clock time, so tonight's bedtime is tomorrow's wake time (alarm, one-night change or plan) minus the level, never later than the hand-set bedtime. It starts after five nights of that template from the median of the real time in bed, and each Monday moves by the user's rule, capped at the goal plus the usual time to fall asleep and never below where it started.

The phone's sealed sync file gets an optional `smartBedtime`: per template the level in minutes and the numbers its status line shows (nights learned, usual time to fall asleep, average time asleep and sleep, made it this week). Only these derived numbers leave the phone, never sleep sessions, times of phone use or app names. A PC with "Use my phone's smart bedtime" on (default, Settings → Sleep) applies the newest phone's level the same way, so the day bar, the on-screen sleep line and the countdown agree with the phone.

## Consequences

The bedtime the user is asked for follows their real habits and is moved by earned steps instead of a target they keep missing. Health-derived numbers now reach the user's PCs, inside the sealed files only, and only as averages and counts. The learning needs Health Connect and Usage access; without them the hand-set bedtime stays. A PC can't learn by itself (it has no sleep data), and with no phone file the PC keeps the plan's bedtime. Rewards for making bedtime are left for later.
