# 25. End a session at once when its checklist is done

Date: 2026-10-22

## Status

Accepted

## Context

A focus session the user starts can only end early through friction: a wait and a typed reason, or a profile's unlock option (the "no escape hatches during focus" rule in the project CLAUDE.md). Guided sessions give a session a checklist of tasks. When every task is done, the session has done its job, and making the user wait before ending it also skews the timeline: the waiting minutes look like focus spent on the work. The user asked that finishing the checklist let them end the session from the on-screen window without the wait, with the reason filled in.

## Decision

We will let "End session" end a focus session at once, with the reason "All tasks done", when all of these hold: every task of its checklist is ticked, the checklist had at least one task when the session started, and the session has run at least 10 minutes. Otherwise ending early works as before. Each task's tick time is recorded, so the timeline can show how long each item took. A notice "All tasks done" (on by default, can be turned off) offers the same end. The user chose ending at once with a prefilled reason; the guards are my call to keep the rule meaningful: without them, a one-item checklist ticked right away would be an escape hatch.

## Consequences

Finishing what the session was for ends it cleanly and the timeline stays accurate. The anti-escape rule gains its first exception, so it gets weaker: someone determined can still write one easy task before starting and tick it after 10 minutes. The guards make that a decision taken before the session, not an impulse during it, which is the friction the rule is about. The project CLAUDE.md's rule has to name the exception. Reverting means removing one path; sessions already ended this way stay in history with their reason.
