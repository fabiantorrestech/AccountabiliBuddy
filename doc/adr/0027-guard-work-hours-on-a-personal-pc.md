# 27. Guard work hours on a personal PC

Date: 2026-10-22

## Status

Superseded by [28. Close the work-hours guard's free exits](0028-close-the-work-hours-guards-free-exits.md)

## Context

ADR 18 made AccountabiliBuddy a body double that never starts sessions by itself: blockers enforce, and only sessions the user starts are held. The user works at a work PC with a personal laptop beside it. In hyperfocus, an idea that "has to happen now" sends them to the laptop, where they open Claude Code and start on something that could wait (redesign design, section K, approved 2026-10-22). A wait on the laptop that is paid once is paid in the morning and forgotten; the laptop then sits open and a chair flip costs nothing. The work PC can't see the laptop quickly enough to rely on sync, and the laptop's owner can always stop the app, so whatever runs there is a speed bump that has to make the lazy path the right one. Calls and interviews must still work there without an exception.

## Decision

We will let a computer guard work hours by itself, only where the user turns it on (Settings → Your day, offered for the Personal computer type, off by default). While it is on, during the day plan's Watch parts, with no session the user started running there and not stopped for today, the apps on its list (Claude Code, terminals and IDEs by default; the user's list) are minimized at once with a notice that points to Ctrl+Alt+N for parking the idea. Browsers, mail and calls are never on the list. Stopping for today, or turning the switch off during work hours, costs the same wait and reason as ending a focus session early. A gate window ("Work until 5:00 PM · what were you about to do?") opens on the first input after three minutes with no input, saves what is typed as a come-back, and enables Continue after 60 seconds. The decision is made locally from the computer's own day plan; sync only relaxes it in later phases (goal met, no session running). This amends ADR 18's "no scheduled sessions" for this opt-in guard only; sessions themselves still start only by the user.

## Consequences

The laptop can't be the cheap outlet during work hours: dev tools close by themselves and every return after a pause meets the gate, without blocking calls or the browser. Enforcement now happens outside a session, so the guard has its own rules to keep straight (it steps aside while a session runs on that computer, and a one-off day or calendar part relaxes it once phase 3 brings those across), and a wrongly configured list could hold a tool the user needs; the switch and the list are the user's, with the end-early friction as the only way around them mid-day. The guard is a speed bump: killing the app ends it until the app starts again, and tamper resistance stays out of scope. Reversing this means removing an opt-in switch and two windows, with no data or schema to migrate.
