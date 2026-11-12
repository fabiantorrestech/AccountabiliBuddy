# 82. More session actions from the phone

Date: 2026-11-12

## Status

Proposed

Amends [42. Control PC sessions and plan edits through sync files](0042-control-pc-sessions-and-plan-edits-through-sync-files.md) (four new action kinds; the transport, the results list and the end-early flow stay as they are). Builds on [77. Step away keeps the timer running with blocking on](0077-step-away-keeps-the-timer-running-with-blocking-on.md) for what Pause timer means.

## Context

UI round 2 gives the phone's Today a Session menu (⋯, a half-height sheet; brief §4.1, board Today-Phone-Session-Menu) with the same rows as the PC's title-bar Session menu: Step away, Pause timer, Add 10 minutes, End and mark the rest unfinished, End session. For a session that runs on a PC, the phone can only ask through its sync file (ADR 42), and a PC takes only `stepAway`, `back`, `endEarlyBegin` and `endEarly` (plus task, noise, plan and calendar actions) from the phone today. Pause timer, Add 10 minutes and End and mark the rest unfinished have no action kind, so the round 2 build draws them greyed "Coming soon" for a PC session (plan §3b item 1). The phone itself runs no focus session on Today (its test session has its own card), so there is no phone-side session for these rows to act on either.

On the PC each of the three is already a menu command with its own rules: Pause timer only where the profile allows a pause (`CanPause`), Add 10 minutes only in a focus phase that can be extended (`CanExtend`), and End and mark the rest unfinished only with open tasks, and only through the PC's end flow (`SessionEndFlow`: the profile's wait and reason outside ProductaBuddy, ADR 25, 69).

## Decision

Four new `SyncAction` kinds go from the phone to the PC whose session Today shows (the same PC Step away and End early go to, `doc/hard-rules.md` "Which PC the phone's Today shows"):

- `pauseTimer` and `resumeTimer` (no text): the PC pauses or resumes its countdown exactly as its own Pause timer and Resume do; the phone shows the paused state from the PC's file.
- `addTime` (text: minutes, only "10"): the PC adds 10 minutes as its own Add 10 minutes does.
- `endUnfinished` (no item, no text): the PC starts its own End and mark the rest unfinished, which begins the normal end-early flow: the phone then receives the usual `endEarly` offer and finishes with `endEarly` (wait and reason length only), exactly as End session does today. Unfinished tasks go back to Today › Queued on the PC once the session has ended.

The PC applies each one **only where its own menu would offer it at that moment** (the same `CanPause`, `CanExtend`, `CanEndToQueued` checks, the same session lock, the same ProductaBuddy rules), and refuses otherwise with a short message in `actionResults` that the phone shows as a toast. Each is taken only within 2 minutes of the PC first seeing it (like `start`), never twice (action id), and only for the session that was running when it was sent (the phone names the session's start time in `item`; a different session refuses). None of them is an escape hatch: Pause timer never shortens a wait, unlocks anything or stops enforcement beyond what the PC's own pause does; Add 10 minutes only makes a session longer (stricter); End and mark the rest unfinished ends only through the wait and reason.

Nothing new is synced beyond the four kinds: no new field in the file, no new text, no task titles (the PC already knows its tasks). An older PC answers "not supported" (ADR 42's results), and the phone says to update it.

## Consequences

The phone's Session menu works the same for a PC session as the PC's own menu, with the PC deciding every case, so the two can't disagree about what is allowed. The PC's action handling grows four cases, each delegating to an existing command. Until this ADR is accepted, the phone draws the three rows greyed "Coming soon" for a PC session and sends nothing new (round 2, `HANDOFF-QUESTIONS.md` Round 2). The `CLAUDE.md` and `doc/hard-rules.md` lists of what the phone may send to a PC name the four kinds when they ship.
