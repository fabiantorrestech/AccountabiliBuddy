# Alarm detection, watch session page, ring ticks, complication, guided sessions

Date: 2026-10-22 · Status: draft for the user's approval

## Why
- The phone took a calendar app's alarm ("Calendar Alarm") as the wake time (ADR 23 reads Android's next alarm clock, which any app can set).
- On the watch, a running focus or break timer isn't the first thing you see, and nothing buzzes when it ends.
- The rings have no clock marks, so a time on them is hard to read.
- There's no watch-face complication for the running timer.
- The PC can't hold a session's goal as a checklist; the user wants guided sessions with tasks and subtasks, checked off in the app and the on-screen window, visible on the phone and watch.

## Decisions (from the user, 2026-10-22)
- **Alarm:** learn which app is the wake-up alarm, and let the user pin one.
- **Task names:** they may travel to the user's paired devices (encrypted link and sync files only).
- **PC layout:** a Tasks card on the Session tab, not a sub-tab. The quick-session popup lists tasks before Start. The on-screen window shows the current task and "2/5"; with Ctrl+Alt held it shows the checklist, items can be ticked there, and Done is collapsed.
- **All tasks done:** an optional notice "All tasks done". "End session" in the Ctrl+Alt window then **ends at once**, with the reason filled in as "All tasks done", guarded as below. Each task's done time is recorded, so the timeline shows how long items took.
- **Watch alerts:** at the end of a focus or break: medium vibration, no sound by default. Settings for vibration (off/light/medium/strong), sound on/off, and a buzz 1 minute before the end.

## Order
1. Verify the black-Tile fix (already on the watch, not committed): the Tile's last timeline step never expires.
2. Alarm detection (bug).
3. Watch: session page first while one runs, with alerts.
4. Ring ticks on the Tile and the in-app ring.
5. Complication for the running timer.
6. Guided sessions: PC first, then the phone and watch.
Each step is committed on its own with scratch checks; installs follow.

## 1. Black Tile
- **Cause (tested in part):** the Tile gives the watch a 2-hour timeline. When the watch doesn't ask again (e.g. overnight), every step expires and it draws nothing. The Tile was black at 10 AM after its last drawing at ~1 AM.
- **Fix:** the last step has no end.
- **Verify:** a 3-minute-timeline build; wait 4 minutes and look. Needs the watch unlocked and on the wrist; the first try was stopped by its PIN screen.

## 2. Alarm detection
- **Phone:** `NextAlarm` also reads which app set the alarm (`AlarmClockInfo.showIntent.creatorPackage`) and keeps a short history of what it saw (app, time; last 30).
- **Learning:**
  - An app counts as the wake-up alarm when at least 2 of its alarms, on different mornings, fell between bedtime and the plan's wake + 3 h.
  - Calendar apps never count (package name contains "calendar").
  - The pinned app always wins.
- **Settings → Sleep:** "Wake-up alarm: Auto (QR Alarm)" or a pinned app, chosen from apps seen setting alarms.
- **Hidden alarms:** Android only shows the soonest alarm from any app. While another app's alarm is next, the phone keeps the last time it saw from the wake-up app, while that time is still ahead.
- **Sync:** only that alarm goes into the sync file.
- **Checks (ktcheck):**
  - learning from a history;
  - a calendar app is ignored;
  - a pinned app wins;
  - a hidden alarm is remembered until it passes.

## 3. Watch session page and alerts
- **While a session runs, the first page is the session:**
  - the profile (or "Break") with a big countdown, or "40m so far";
  - the ring of the phase;
  - the current task and "2/5" once tasks exist (step 6).
- **Otherwise** pages stay as they are (Today first).
- **Alerts at a phase's end** (focus → break, break → focus, session end):
  - vibration at the chosen strength, optional sound, optional 1-minute warning;
  - timed on the watch from the running session's end it already has;
  - an exact alarm may need the watch's "Alarms & reminders" permission, which only you can grant there. I'll check on your watch before relying on it.
- **Settings page** on the watch for these.

## 4. Ring ticks
- On the Tile rings and the watch app's Today ring (24-hour, midnight at the top):
  - a tick each hour;
  - longer ticks at 3, 6, 9 and 12 (AM and PM);
  - small "12" labels at the top and bottom where there's room.
- **Shared pure tick list:** `TileRing.ticks()`; ktcheck checks the angles.

## 5. Complication
- The existing complication shows, while a session runs: a countdown to the phase's end that ticks live (Android counts it, no updates needed), and the phase progress as a ring.
- "40m so far" for count-up.
- Otherwise focus vs plan, as today.

## 6. Guided sessions (ADR 24, ADR 25)
- **ADR 24:** task names may leave the PC, to the user's paired devices only (link and sealed sync files). They're DPAPI-encrypted at rest on the PC, like notes on added sessions.
- **ADR 25:** with every task of a checklist made before the session started done, and the session at least 10 minutes old, "End session" ends at once, reason "All tasks done". This changes the anti-escape hard rule.
- **Model (Core):**
  - `SessionTask(Id, Title, DoneAt?, Subtasks)` and `SessionSubtask(Id, Title, DoneAt?)`.
  - A task is done when ticked; ticking all its subtasks ticks it.
  - The running session's checklist is saved with its checkpoint (resumes after a crash) and kept with the session in history (90 days).
- **PC UI:**
  - **Quick-session popup:** a task list before Start (add, subtasks, remove).
  - **Session tab:** a Tasks card while a session runs (tick, add, edit) with "Done (n)" collapsed.
  - **On-screen window:** the current task and "2/5". The Ctrl+Alt details show the checklist with checkboxes and "Done (n)" collapsed, plus "End session" when all are done.
  - **Notice:** "All tasks done — end the session?" (Settings → Focus sessions, on by default).
- **Timeline:** task done marks on the Focus row (PC, phone), with a hover such as "Write intro · 25m".
- **Phone:** the Today page shows the running session's checklist, read-only, from the link and the sync files.
- **Watch:** the session page shows the current task and "2/5".
- **Checks:**
  - Core rules: done rollup, the end-at-once guards, checkpoint round-trip;
  - sync file and link carry the checklist in both languages;
  - each UI by hand.

## Not in this plan
Editing tasks from the phone or watch; recurring task templates; Curbox/Blockers row; flexible lunch; deep-work goal.
