# 50. Task estimates, notes and a Later list in the shared task lists

Date: 2026-10-28

## Status

Accepted

Amends [48. Share tasks and come-backs across devices](0048-share-tasks-and-come-backs-across-devices.md) (the shared lists gain fields and a fourth list).

## Context

The Claude Design redesign gives the PC a Tasks page (board 5x). Each task there has an estimate (25, 50 or 90 minutes), a Notes box, "Move to Later" and "Put in plan". The page lists four groups: This session, Come back to, Later (collapsed, with "Next due: …") and Done today. Today a task has only a title, subtasks and a category. Nothing past the next session can be kept: unfinished tasks of past sessions only show up in the Notebook's history.

On 2026-10-28 the user asked for these features to be built, not just drawn, and for task notes to sync to their own devices like task titles do. The hard rules said notes never leave the device. That was written for session reasons and notes, and task titles already travel sealed to the user's devices (ADR 24, 48).

## Decision

We will add three optional fields to a task, in the running checklist and in ADR 48's next-session list:
- an estimate in minutes;
- a note (free text);
- a planned day part, set by "Put in plan".

When that part starts, the task joins the session draft and shows in Next up.

We will add a fourth shared list, **Later**, built the way ADR 48 builds its lists. Each item has a stable id, its fields, an optional due date, an edit time, a position, and a tombstone kept 30 days. Devices merge item by item, and the newest edit wins. "Move to Later" moves a task there. During a session, moving an open task off the checklist counts as taken off open (ADR 25), so it can never end a session early. Unfinished tasks of past sessions are offered for Later; they are not moved there on their own.

A task's note is private text. It travels only inside the sealed sync files to the user's own devices, the same as task titles, and is stored DPAPI-encrypted in the `documents` table. It is never logged, never shown on the watch, and never sent anywhere else. New fields are optional, so older readers on the phone and watch ignore them.

## Consequences

The Tasks page can show the design's groups with real data, and a task planned for tomorrow comes back when tomorrow's part starts. The phone can show and edit estimates, notes and Later once the phone redesign adds them; until then the phone keeps them unchanged when it merges.

The sync files grow by the notes and the Later list, bounded by the 30-day tombstones. Notes add one more kind of private text that must stay sealed in transit and encrypted at rest, and `doc/hard-rules.md` records it with the other synced fields. Removing a field later means a reader that skips it, which is cheap. Removing the Later list would orphan items users have kept there, which is not.
