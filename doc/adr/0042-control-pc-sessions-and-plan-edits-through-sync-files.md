# 42. Control PC sessions and plan edits through sync files

Date: 2026-10-26

## Status

Accepted

Amends [26. Sync through Google Drive only](0026-sync-through-google-drive-only.md) (which dropped starting and ending PC sessions from the phone) and [19. Let paired phones change the day plan](0019-let-paired-phones-change-the-day-plan.md) (its transport)

## Context

ADR 26 made Drive the way devices sync and kept the Wi-Fi link (ADR 14) only behind an "old way" switch. Four things still need the link: starting a PC session from the phone, ending one early with the PC's wait and reason, the phone's day plan edits (ADR 19), and part of the phone's live view of the PC (the current task, the running profile, the daily goal). The user decided (2026-10-26) to move all four to the sealed sync files and then delete the link. Sync files are slower (the PC polls Drive every 10 seconds during a session or a work part, the phone every 30 seconds, an action uploads at once), and a file carries no reply, so a PC can only acknowledge an action by its id, and today it acknowledges kinds it doesn't know.

## Decision

The phone controls a PC through `SyncAction`s in its own file, and the PC answers in its file: new action kinds `start` (profile id, length or no set end), `endEarlyBegin`, `endEarly` (the request id) and `dayPlanEdit` (the edited plan with the PC version it was based on), and a new `actionResults` list of (action id, ok, message, and for a plan clash the PC's plan version) that every new PC writes for every action it applied or refused, unknown kinds included ("not supported"). Ending early is two steps: the PC turns `endEarlyBegin` into an offer in its file (`endEarly`: request id, the phase it belongs to, allowed from, minimum reason length) using its own wait and its own clock; the phone counts down to "allowed from" and sends `endEarly`; the PC checks the id, the phase, the time and freshness (10 minutes after "allowed from") and ends the session. The typed reason stays on the phone: the PC records "reason given on <phone>, N characters", so reasons still never leave the device they were typed on. A plan edit is decided by the PC exactly as ADR 19 says (refused during a counted focus session, a clash answered back to the phone with both edit times, which asks there). The PC's running session in its file also carries the current task, the profile id and the count-up profile id, and its file carries the daily goal in minutes (ADR 29). A phone shows "Update AccountabiliBuddy on <PC>" when a PC's file has no `actionResults` support (by its app version). The phone controls the newest PC file's PC by default and lets the user pick one when there are several. Actions older than their freshness are dropped, never applied late.

Added with the watch's noise switcher (W 1, 2026-10-26): a `noise` action (`{op: toggle|next|prev|volume, value?}`) plays/pauses, skips and sets the volume of a PC's noise player, sent by the phone on the watch's behalf, and the PC's file carries a `noise` object (playing, the shown name and never a path, volume, locked, at). It needs PC 0.1.109. While its noise plays, and for 10 minutes after a noise command, the PC polls Drive every 10 seconds instead of every 5 minutes. A start, end early or noise command a PC hasn't answered in 10 minutes is given up on the phone, checked each minute while one waits, so an off PC that sends no file doesn't keep it waiting.

## Consequences

The phone can start and end PC sessions and edit the plan at home, at work and on mobile data, with a lag of seconds to a minute instead of near real time, and the link can be deleted (a separate step, with its own clean-up of ADR 14, 15 and 21's link parts). The sync file grows an offer, results, three running-session fields and the goal; an old PC simply never answers, which the phone detects by version. The end-early wait still runs on the PC's clock, so a phone can't shorten it. Controlling a PC's noise player from the watch costs that PC more Drive calls (a poll every 10 seconds while noise plays), a known network and battery cost.
