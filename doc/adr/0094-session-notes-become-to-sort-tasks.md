# 94. Session notes become To sort tasks

Date: 2026-11-24

## Status

Accepted (owner, 2026-11-24; Tasks redesign D11: "Come-backs and session notes are not in the Archive: on upgrade they become To sort tasks (a session note's text goes into the task's notes)"; chose: the title is the note's first line; a new note line becomes a task at once; once a task, a note follows the task rules, including where task text may go).

Amends [87. Sync session notes between your own devices](0087-sync-session-notes-between-your-own-devices.md) (notes stop being a list of their own on the PC and lose their stricter privacy rules once converted) and [76. One task list with bins, labels and tags](0076-one-task-list-with-bins-labels-and-tags.md) (a new source of To sort tasks).

## Context

The redesigned Archive shows only tasks: unfinished, done and recently deleted. Come-backs are already To sort tasks (ADR 76), so they only leave the Archive's list. Session notes, the lines typed in Focus mode, are a separate synced list (`sessionNotes`, ADR 87) with their own privacy rules: never in a QR file, never in the copy a receiver keeps of another device's file, never in a plain backup. The owner wants each note to become a To sort task so it gets sorted like everything else.

Constraints: every device that holds a note may convert it, in any order, offline. A late converter must not overwrite edits another device made to the task since. Receiving a file must never trigger a write (hard rules, ADR 86). Older PCs keep writing notes until they update. Task text reaches places note text never did: the one-release legacy `comebacks` field (which goes in QR files and in kept copies) and task-list backups, including plain auto-backups.

## Decision

We will convert every live session note into a To sort task and then delete the note.

The task's id is made from the note's id (a version-5 style id in a namespace of its own), so every device that converts a note makes the same task. Its title is the note's first line, cut to 80 characters with an ellipsis; its Notes hold the full text; its origin line reads "Session note" with the note's time in the house format and the converting device's time zone; it is created at the note's time and keeps the note's session start and focus profile id as origin data, so the session review can list the tasks made from that session's notes. Every field of the new task is stamped with the note's own edit time, never the time of the conversion, so a device converting late never overwrites a change made since and never raises "Changed on both devices". If a task with that id already exists, live, in History or as a tombstone, nothing is added. Only after the task is saved is the note deleted, as an ordinary synced delete (a tombstone), so it is never converted twice; while the task list can't be opened the note is kept.

The conversion runs once at start, after the database backup and behind a marker, and again after any merge that brings live notes. It writes nothing on its own: the new tasks and the tombstones leave with the device's next normal write. From this version a new line typed in Focus mode becomes a To sort task directly, in the same shape. The "Sort them now" card for earlier-version tasks goes.

Once a task, a note's text follows the task rules: it may travel in the legacy come-backs field while that field is written, sit in plain task-list backups and auto-backups, and be shown wherever task titles are. The `sessionNotes` part keeps being read for older PCs and keeps its ADR 87 rules for the notes it still carries.

## Consequences

The Archive and the session review need only tasks. Notes written on an older PC appear as To sort tasks on an updated device after the next sync. A note's text is less protected than before; the owner accepted this (2026-11-24). An edit to a note made on an older PC after another device converted and deleted it is lost, because the tombstone wins over an older edit. The phone converts notes the same way once its Kotlin model catches up; until then it shows the To sort tasks the PC made and sees the notes disappear from its Archive.
