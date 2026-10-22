# 23. Use the phone's next alarm as the wake time

Date: 2026-10-22

## Status

Accepted

## Context

Sleep mode on the PC takes tomorrow's wake time from the day plan's Sleep part, or from a one-night change made in the on-screen window's Ctrl+Alt details (`WakeOnce`). The wake time sets the night on the day bar, the evening's "sleep if you go now" countdown and when it starts. The user's real wake-up time comes from an alarm on their phone (an alarm app such as QR Alarm or the Clock app), which they change from day to day, so the plan's fixed time is often wrong and keeping the PC in step by hand is a chore. The user asked for the PC to follow the phone's alarm, carried by what already links the devices: the Drive sync folder or the Wi-Fi link.

Android gives any app the next alarm that alarm apps have set as an alarm clock (`AlarmManager.getNextAlarmClock()`), without a permission, and announces changes with a broadcast. Devices already exchange sealed sync files (ADR 22) over the folder, QR codes, shared files and the ADR 14 link, and the PC already merges what other devices send. Any data that leaves a device has to be named in the project's local-only rule.

## Decision

We will let the phone put its next alarm time in its own sealed sync file, as an optional `nextAlarm` field on its device card that older apps ignore, so it travels by every ADR 22 method without a new connection or format version. The phone shares it while its "Share my next alarm" switch is on, which it is by default, and writes or sends its file again when the alarm changes.

On the PC the wake time for a morning is decided in this order: the phone's alarm, when it falls between the planned bedtime of the night before and noon of that morning; otherwise a one-night change; otherwise the day plan. A one-night change made after the alarm already set that morning wins only when the user confirms it in a prompt, and is then stored as overriding the alarm for that morning. The PC uses a paired phone's alarm by default as soon as one reports it, with a Settings → Sleep switch, "Use my phone's alarm as wake time", to turn it off. The user chose this order, the confirmation and both defaults.

## Consequences

The evening countdown, the night on the day bar and the phone's own header follow the alarm the user actually set, with nothing to keep in step by hand, and an alarm turned off for a weekend simply falls back to the plan. A wake time now has three sources, so every place that shows one should say when it came from the alarm ("up 6:45 AM (phone alarm)") or the order will look like a bug. The alarm time joins what the phone shares with the user's own devices; it never leaves the device group and is encrypted like the rest of the file, but the project CLAUDE.md's rule now has to list it. How fresh the PC's copy is depends on the method: at once over the link, at the folder's interval otherwise. An alarm app that doesn't register its alarm as an alarm clock is invisible to this, which only a test on the user's phone shows. Removing the field later is cheap, since it is optional on both sides.
