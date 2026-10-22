# 24. Let session task names reach your own devices

Date: 2026-10-22

## Status

Accepted

## Context

Guided sessions give a focus session a checklist: tasks with subtasks that the user types before or during the session and ticks off on the PC. The user wants to see that checklist on the phone and the watch, including from a work computer that shares only through the sync folder (ADR 22). Task names are text the user types, like the notes on sessions added by hand, and the project's local-only rule has so far kept such text on the PC (the readable part of activity history is limited to times, apps, sites and their sections, classification and session context; notes and reasons are encrypted at rest and never sent).

## Decision

We will let the running and recent sessions' checklists, with their task and subtask names and when each was ticked, travel to the user's own paired devices, and only there: in the link's session state (ADR 14) and as an optional part of a session in the sealed sync files (ADR 22). On the PC, checklists are encrypted at rest with DPAPI like notes on added sessions, and kept 90 days like the rest of the history. The user decided this in chat on 2026-10-22, knowing that names would leave the PC for their phone and watch.

## Consequences

The phone and watch can show what the session is for and how far along it is, also for a session on the work computer. Task names become one more piece of user-written text that leaves the PC, so the project CLAUDE.md's rule has to name them, and a sync file now carries something more personal than times and site names; it stays sealed with the group's key and goes nowhere but the user's devices and the folder they chose, where only those devices can open it. Early-end reasons and notes on added sessions still never leave the PC. Taking this back means dropping an optional field on both sides, which is cheap, but names already on a device stay there until that device's history is cleared or it leaves the group.
