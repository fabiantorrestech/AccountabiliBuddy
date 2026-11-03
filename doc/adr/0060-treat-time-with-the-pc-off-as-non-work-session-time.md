# 60. Treat time with the PC off as non-work session time

Date: 2026-11-03

## Status

Accepted

## Context

A session is saved every 30 seconds and restored at launch, but its end time did not move while the PC was off. Staying off past the end completed the session with no wait or reason, which made shutting down a way out of a session, against the rule that a session has no escape hatches. The time off was also counted as focus: it was logged as an interruption and shown in History as "app closed", but focus totals only subtract away stretches, so it stayed in every total and in the sessions other devices received. Nothing told a crash from a restart or a power cut, and other devices could not tell a PC had gone off, so a phone stayed held until a timed session's planned end.

AB cannot tell a real power cut from someone holding the power button, so a cause cannot be allowed to change the rules. A power cut also gives no warning, so other devices cannot rely on the PC saying goodbye.

## Decision

We will treat any stretch with the PC off, asleep or AB closed as non-work session time: never focus, whatever happens to the timer. It is recorded as an away stretch of the new kind `DeviceOff`, built from the existing Interrupted and Resumed events, so every total leaves it out and the session reaches other devices in pieces around it, each with the minutes off before it. This also corrects the totals of past sessions, since records are rebuilt from those events.

What it does to the timer is a device-local setting, pausing by default: the phase's start and end move forward by the gap, as on a return from a step away. Keeping the original end instead is looser, so it waits the looser-change delay and is refused during a focus session; if that end passes during the gap, the session closes at the moment the PC went off. A gap of 4 hours or more, or a new local day, closes the session at that moment under either setting. A gap under 2 minutes carries on with no notice.

A notice, on by default and with its own switch, says how long the PC was off and what was done. A session that closed is always told. The notice names a likely cause from the boot id, a saved "Windows is ending" flag and the System event log (1074, 6008, 41), read locally; the cause only words the notice. Ending from it is the normal end-early flow.

Other devices learn about it two ways: `SyncRunning.OffSince` in a best-effort last file while Windows shuts down, and a file at least every 5 minutes during a session, so a file 15 minutes old means the PC went quiet. A phone calls a PC quiet only if one of its own sync methods read successfully while the file was that old, so turning off the phone's data frees nothing. Phone guards hold for 10 minutes after a signalled shutdown, then let go until the PC is back. Only times travel; causes never leave the PC.

## Consequences

Shutting down no longer shortens a session or earns focus, and totals are honest, including for past sessions, whose numbers may drop. Sessions can finish later than first planned, and that is the point of the default. A 4-hour absence does end a session with no wait, which we accept because a stale session the next morning is worse.

A power cut releases the phone at about 15 minutes, not 10, since it has to be noticed first. Slow sync can delay that further, and a phone that cannot read keeps its old limits. The heartbeat adds a small sync write every 5 minutes during a session. The last file is best effort: Windows may not allow time for it, and Drive is the least likely to finish.

Sleep and a killed app follow the same rules, since both were the same way out. A new way of ending a session early must not bypass this.
