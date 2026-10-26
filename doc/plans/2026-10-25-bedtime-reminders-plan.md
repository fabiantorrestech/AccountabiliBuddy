# Bedtime reminders: plan

Date: 2026-10-25 · Status: proposed (awaiting the user's OK)

## Goal

Replace the single "two hours before bedtime" reminder with a list the user sets: how many, when, and for each one
whether it's a notification or an alarm (sound, volume, vibration) and which devices get it. A live countdown to
bedtime shows meanwhile, and follows bedtime when it moves (alarm changed, smart bedtime, a one-night change).

## Decisions (user, 2026-10-25)

| Question | Answer |
|---|---|
| Defaults | 45 min before and 15 min before (notifications), at bedtime (an alarm, phone only) |
| More reminders | Any number, each at its own minutes before bedtime |
| Alarm buttons | Dismiss and Snooze (5 min), labelled with which reminder it is ("Bedtime · 3/3") |
| Devices | Phone (default), any PC, the Pixel Watch (buzzes; an alarm buzzes until dismissed) |
| Dismiss | Dismissing on one device stops it on the others, best effort |
| Where set | The shared sleep settings (both ways, newest edit wins, ADR 35) |

## Settings (shared "sleep")

`bedtimeReminders`: on/off, and a list of reminders:

| Field | Meaning | Default |
|---|---|---|
| `minutesBefore` | When, before bedtime (0 = at bedtime) | 45, 15, 0 |
| `alarm` | Rings (else a notification) | false, false, true |
| `devices` | Where: `phone`, `watch`, or a PC's device id | phone (+ watch for notifications) |
| `sound`, `volume`, `vibrate` | For alarms | Bell, 80%, on |

Edited in Settings → Sleep on both apps (a list with add/remove and per-reminder options). The old single reminder's
on/off becomes the list's on/off; its 2-hour time isn't kept.

## Behaviour

- **Bedtime** is tonight's bedtime as each device's sleep schedule has it now (plan, smart bedtime, alarm-moved wake
  for a part woken by the alarm, one-night change). Reminder times are recomputed whenever it changes, so moving the
  alarm or a smart bedtime step moves them; a reminder whose time has already passed isn't fired late (unless it's
  under 2 minutes late).
- **Countdown:** from the first reminder until bedtime:
  - Phone: a live notification "Bedtime in 32m (11:30 PM) · 2/3 reminders" (the timeline card shows it in its header
    as now).
  - PC: the on-screen window's sleep line counts down to bedtime (it already shows the sleep left).
  - Watch: the snapshot carries tonight's bedtime and reminders; a card and the buzz.
- **Notification reminder:** a notification on each chosen device ("Bedtime in 15m · 2/3").
- **Alarm reminder:** rings on each chosen device: phone, a full-screen alarm (sound at its volume, vibration) with
  "Bedtime · 3/3", Dismiss and Snooze 5 min; PC, the alarm window (as this PC's wake alarm, ADR 35); watch, buzzes
  until dismissed. Phone alarms use exact alarms, so they ring with the app closed.
- **Dismiss everywhere (best effort):** a dismiss or snooze goes to the other devices as `{reminder, night, action}`:
  phone ↔ watch over the Data Layer at once, phone ↔ PC over the link or in the sync files (Drive polled every 10 s
  while a reminder rings). A device that hears it stops ringing (snooze: rings again at the same new time).
- During a focus session a reminder still fires as set (my call: bedtime matters more than the session); the countdown
  notification stays silent as always.

## Tasks

1. ADR 38 (reminder list in shared sleep settings; dismissals between devices in the sync files, the link and the Data
   Layer) + project CLAUDE.md lines.
2. Settings model both sides + migration from the old switch; the reminder schedule engine (pure: bedtime + list →
   times, recompute on changes); checks.
3. Phone: exact alarms per reminder, the countdown notification, the full-screen alarm (Dismiss / Snooze, label),
   Settings → Sleep list editor.
4. PC: reminder notifications and the alarm window, Settings → Sleep list editor.
5. Watch: reminders in the snapshot, buzz + card, an alarm that buzzes until dismissed.
6. Dismiss/snooze across devices (Data Layer, link, sync field `reminderActions`); checks.
7. Test on the Pixel 5 + watch + PC; ship.
