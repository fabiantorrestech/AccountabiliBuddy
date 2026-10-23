# 29. Share the daily goal with paired devices

Date: 2026-10-23

## Status

Accepted

## Context

Redesign phase 4 makes the daily deep-work goal (asked on the first run, the PC's "goal" document) central: Today's card, the on-screen window and History on the PC. The approved design (section G) also puts it on the phone's header and widget and the watch's ring, and section I asks the watch to buzz when the goal is reached. ADR 15 and ADR 21 list exactly what a paired phone and the watch receive ("focusDay": plan parts, counted sessions, drift, focused and planned minutes, the week; the watch snapshot the same with the running session), and the project `CLAUDE.md` hard rule repeats that list, so a new field needs a record. The goal is a number of minutes the user chose: not activity, not a title, address or reason.

## Decision

We will add the daily goal in minutes as an optional field to the PC's "focusDay" link message (`goalMinutes`) and to the snapshot the phone sends the watch, and nothing else about it. The phone shows today's focus against it ("40m of 1h goal"), the watch draws it as the outer ring and buzzes once when today's focus reaches it. Devices that don't know the field ignore it; a PC from before sends none and the phone and watch then show no goal. The goal stays set on the PC; it doesn't travel in the sealed sync files (ADR 22) until a later change adds it to their documents.

## Consequences

The goal is visible where the user looks without adding a connection or any activity data, and older and newer versions keep reading each other. A phone that only syncs through files, without the link, shows no goal yet. The watch's buzz depends on the snapshot's goal and today's focus, so it can be late by as much as the snapshot is old (the phone sends one at once on a session change, else at most every 10 seconds). Reversing it means dropping one optional field on each side.
