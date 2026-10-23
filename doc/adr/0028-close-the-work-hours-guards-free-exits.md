# 28. Close the work-hours guard's free exits

Date: 2026-10-23

## Status

Accepted

Supersedes [27. Guard work hours on a personal PC](0027-guard-work-hours-on-a-personal-pc.md)

## Context

ADR 27 let a personal computer guard work hours by itself: during the day plan's Watch parts, listed dev tools are minimized, stopping for today costs the end-early wait and reason, and a gate asks "what were you about to do?" on the first input after three idle minutes. A review of the first build (2026-10-22) found that the guard could still be lifted for free in three ways, each one click: deleting or shortening today's Work part in the Day plan tab, adding a one-off day off for today (the day plan always applies at once, ADR 18), and starting a session on that computer, since the guard stepped aside for any session and the default Quick Start profile allows developer tools and ends on command. The hyperfocus impulse the guard exists for is exactly "one click and I'm in", so any free path defeats it. The same review found that the gate opened on any input after a pause, including moving the mouse to unmute in a call or interview, which broke ADR 27's promise that calls need no exception. The user chose to close all three exits and to open the gate only on dev tools (2026-10-22).

## Decision

We keep ADR 27's guard and change three things. First, a session no longer pauses it: while it guards, listed apps are minimized during a session too; only the gate stays quiet then, since the session has its own enforcement. Second, while the guard is on and not stopped for today, a day plan change that frees some of today's remaining work time (time that was a Watch part between now and midnight and no longer is, including a one-off day for today and moving a part later) waits like a looser change: the configuration's countdown and Cancel, never during a session, surviving a restart. This holds for every route that changes the plan (the Day plan tab, Exceptions, a paired phone, received sync files) because it is decided where every change goes through. It amends ADR 18's "the day plan always applies at once" for this case only. Third, the gate opens when a listed app comes to the front the first time after work hours start and again after three minutes with no input, in place of that minimize notice; plain input, calls, the browser and anything not on the list never open it. Everything else stands from ADR 27: opt-in per computer, off by default, decided locally from the computer's own day plan, stopping for today or turning it off or removing an app while work is left today costs the end-early wait and reason, and ADR 18's "no scheduled sessions" is amended for this guard only.

## Consequences

Every way out of the guard during work hours now costs the same wait and reason, and calls on the laptop are never interrupted. In exchange, a legitimate change of today's plan on a guarded computer waits the looser-change delay (or the user stops for today first), and a paired phone's plan edit is accepted with a note that it waits on the PC, while the phone shows the PC's old plan until it applies. Sessions on a guarded computer can't use the listed dev tools during work hours even when their profile allows them; personal coding there happens after work hours or after stopping for today. The gate now only catches a return that heads for a dev tool, not a return to browsing. Reversing this means restoring the session check and the input trigger and removing the configuration's day plan hold, with no data or schema to migrate.
