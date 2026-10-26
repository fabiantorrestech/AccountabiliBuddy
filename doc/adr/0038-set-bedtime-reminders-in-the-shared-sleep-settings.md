# 38. Set bedtime reminders in the shared sleep settings

Date: 2026-10-25

## Status

Accepted

## Context

Each device had one bedtime reminder, two hours before bedtime, as a plain notification. The user wants a list they
set themselves (2026-10-25): several reminders at chosen minutes before bedtime (45, 15 and 0 by default), each a
notification or an alarm with its own sound, volume and vibration, sent to the devices they pick (the phone by default,
any PC, the Pixel Watch), with a live countdown to bedtime that follows it when it moves, and an alarm labelled with
which reminder it is, with Dismiss and Snooze. Dismissing on one device should stop it on the others, best effort.

## Decision

The reminder list lives in the shared sleep settings ("sleep": `bedtimeReminders`, on/off and the list), so it is set
once and edited on the PC or the phone, the newest edit winning (ADR 35). Each device works out tonight's bedtime from
its own sleep schedule, recomputes the reminder times whenever bedtime moves, and fires the reminders addressed to
it: a notification, or an alarm (the phone's rings through exact alarms even with the app closed; a PC shows its alarm
window; the watch buzzes until dismissed). The phone shows a live "Bedtime in …" notification from the first reminder
to bedtime; the PC's on-screen window and the watch show the countdown. The phone fires the watch's reminders over the
Wearable Data Layer ("/reminder"), since the watch has no plan of its own.

A dismiss or snooze goes to the other devices as `{night, reminder, action, at}`: over the Data Layer between phone
and watch, and in the sealed sync files and the ADR 14 link between the phone and the PCs (`reminderActions`, kept a
day). A device that hears one stops that reminder ringing; a snooze rings again at the same new time everywhere.

## Consequences

The user sets their wind-down once and it reaches every device. Reminders follow the real bedtime. Dismissing
everywhere is only as quick as the route: at once phone and watch, within seconds over the link or Drive (polled every
10 seconds while a reminder rings), not at all for a device that is off. The old single reminder's switch becomes the
list's switch.
