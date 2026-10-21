# 20. Keep hand-added focus sessions in the activity history

Date: 2026-10-20

## Status

Accepted

## Context

On 2026-10-20 the user asked to add past focus sessions by hand in History (focus done away from the app, or a session the app missed), shown in their own shade, counting everywhere a recorded session counts (History's totals, the day bar's Focus row, and the phone once it shows focus history), with a date, start and end times and an optional note, no overlaps with other sessions, and editable later. Recorded sessions live in the activity history database (ADR 8) as session events (started, phase changes, ended) with readable times and mode, and an encrypted reason; backups carry that database's tables as the History part, while app-owned side documents such as omitted sessions ("history.notReal") are not backed up.

## Decision

We will store a hand-added focus session as session events in the same table as recorded ones: a started and an ended event with a new session mode, `added`, its times readable and its note encrypted like an early-end reason. The activity database's schema version goes up by one with no change to its tables, so an older app refuses a database of this version instead of failing to read it; backup files aren't versioned that way, so restoring one from this version into an older app isn't supported, and from now on the reader skips session modes it doesn't know instead of failing. Adding, editing (replace) and deleting go through new activity log methods that only ever touch events of mode `added`; recorded sessions are never changed by them.

## Consequences

Hand-added sessions travel with History in backups, are kept 90 days like everything else in it, and show up wherever session history is read, so the day bar and History need only to know the new mode (shade, "added by hand", no drift, edit and delete). Every reader of session modes must handle `added`, and older app versions can no longer open a database from this version. The 90-day cleanup removes them with the rest of the history. Because they carry no activity segments, focus time that History computes from segments has to add their length explicitly.
