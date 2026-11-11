# 77. Step away keeps the timer running with blocking on

Date: 2026-11-11

## Status

Accepted

## Context

Since the step-away feature, stepping away during a session froze the countdown and held enforcement as it was (nothing new judged, nothing blocked lifted) until "I'm back", which then moved the phase's end later by the time away. The Tasks redesign (`05 Shared behaviour` §2) asks for a step away where "the timer keeps running; time logged as away", next to a separate "Pause timer". With the old behaviour, a step away is a way to stop the clock and have enforcement hold still for as long as the user likes.

The hard rules say a step away never shortens a wait or unlocks anything, looser changes wait (10 minutes by default) and are never made during a session, and stricter ones apply at once. The phone reads the PC's running session from the sync file (`SyncRunning`): while `awaySince` is set it treats the countdown as paused and extends the end by the time away so far (ADR 26), so a step away that crosses the old end never ends the session on the phone.

## Decision

The owner decided (2026-11-11): by default a step away keeps the session timer running and blocking stays on. A device-local setting, "When I step away" (`tasks.settings`, `StepAwayTimer`), switches back to the old behaviour.

- **Keep timer running (default):** `SessionState.AwayTimerRuns`. The countdown keeps going and phases end on time (a session can end while the user is away), `PausesEnforcement` is false, and nothing asks "Back to the task?" or treats input as being back at the desk. The stretch is still logged as AwayStarted/AwayEnded (stepped away) and is never focus time; "I'm back" moves nothing.
- **Pause timer:** exactly the old step away.
- **Direction:** keep-running → pause is looser (blocking held while away): it waits the looser-change wait and is refused while a session runs; pause → keep-running applies at once. A backup restores it through the same service.
- **Per mode (ADR 69):** "When I step away" and the session's number of actions (ADR 76) are kept per mode in the same document, `tasks.settings` (AccountabiliBuddy) and `tasks.settings.pb` (ProductaBuddy), same defaults (keep the timer running, 3 actions); ProductaBuddy's copy starts as AccountabiliBuddy's. The looser-change rules hold for either copy, and backups restore both through the service. They are not in the mode compare view, whose "make both agree" writes both copies directly and would skip the wait.
- **When it is read:** at the moment of stepping away. A looser change can't take effect while a session runs, and a step away takes the stricter of the setting at the session's start (kept in the session's state and its checkpoint, so a restart keeps it) and now, so neither a change nor a mode switch during a session can loosen it. The number of actions is fixed at the session's start.
- **A break never freezes (owner, 2026-11-11):** outside focus and planning (a Pomodoro break, where nothing is enforced) a step away never freezes the countdown, whatever the setting: the break keeps running and ends on time, and "Pause timer" is refused there. With "Pause timer", when focus starts while the user is still away, the step-away rules for focus apply from that moment: the countdown freezes from the focus's start (`SessionState.AwayPausesInFocus`, kept in the checkpoint) and "I'm back" gives the focus its full length. With "Keep timer running" nothing changes. Before this, a paused step away in a break froze the break, with blocking off, for as long as the user liked. Lunch in a break keeps its own rule (`EndBreakForLunch`).
- **Side tasks (owner, 2026-11-11):** a side task gets the same treatment. A setting "When I start a side task" (`TaskSettings.SideTask`, in the same per-mode documents, same default) keeps the timer running by default: the countdown goes on and phases end on time (`StartSideTask(length, keepTimerRunning)`, `AwayTimerRuns`), enforcement stays on as side tasks always did, the side task's own length (`AwayEndsAt`), chime, card and +5 min still work, and the time is logged as a side task, never focus. "Pause timer" is the side task as before (the countdown waits; enforcement on). Same direction rules (keep → pause waits and is refused during a session, pause → keep at once), the session keeps the stricter of its start value (`SessionState.SideTaskAtStart`, checkpointed) and now (`StepAwayTimerPolicy.ForSideTask`), backups restore it through the service, and it is not in the compare view. A break never freezes for a side task either: in a break the countdown keeps going whatever the setting, and with "Pause timer" a side task still on when focus starts freezes from the focus's start (`AwayPausesInFocus`). This closes the same hole: a side task in a break froze the break, with blocking off. A lunch that starts during a side task with the timer running ends it and starts lunch's away, as for a step away.
- **Unchanged:** lunch (it pauses the session as before; a lunch that starts while stepped away or on a side task with the timer running ends that away and starts lunch's, logged as two stretches) and "Pause timer" (ADR 76). The phone's and watch's Step away / I'm back actions go through the same setting.
- **Older phones:** while the timer runs, the PC's running session goes out without `awaySince`, so a phone keeps counting down to the real end and its locks follow it; it doesn't show the PC as stepped away until the Android run adds that.

## Consequences

- A step away no longer stops the clock or loosens anything by default; users who relied on it must choose "Pause timer", which waits like any looser change; the same goes for a side task.
- A timed session can end during a step away; its record closes the away at the session's end, and the time away is left out of focus totals as before.
- The phone shows the PC's session running during a step away with the timer running (no "stepped away" state) until the Android run carries the mode.
