# 76. One task list with bins, labels and tags

Date: 2026-11-11

## Status

Accepted

## Context

The owner's Tasks redesign (the design handoff kept outside the repository, files `02 Data model and sync` and `05 Shared behaviour`) replaces the separate task lists with one list. Today a task lives in one of four stores, each its own shared list in the sync file: the next session's list (`nextTasks`, ADR 48), Later (`later`, ADR 50), Come back to with parked thoughts (`parked`, ADR 48) and Auto-saved (`autoSaved`, ADR 72). A move between them is a delete in one list and a new item in another, and every list merges item by item: the newest edit of the whole item wins, so a title changed on the phone and a step ticked on the PC at nearly the same time lose one of the two. Categories travel by name and colour only (ADR 62), with no id. ADR 72 added plain-name labels and local items kept on one device.

The redesign asks for: one list of tasks, each in one bin (This session, Queued and Finished on Today; To sort, Next and Later on the Shelf); one coloured label per task and any number of tags; a permanent task number ("142", and "142b" for a step) that is the same on every device; reminders with per-occurrence state that syncs; saved views that sync; a History of finished, archived and deleted tasks; and a sync rule where a field changed on one device and another field changed on another device both survive, while the same title or note changed on two devices asks the user which to keep. The phone keeps writing the old fields until its own run, so the PC must keep writing and reading them.

The constraints are the existing ones. Sync files are sealed per device (ADR 22) and older apps ignore unknown fields, so new parts must be optional and the format stays 1. Task text never leaves the sealed files (ADR 48, 50). Core has no UI or OS dependencies (ADR 4). Time comes from the trusted clock, so Core functions take it as a parameter. Shared lists keep tombstones 30 days and clamp stamps more than 2 minutes ahead (`SharedListMerge`).

## Decision

We will keep every task in one shared list, `tasks`, of `TaskItem`s, each in one `TaskBin`, and carry it in the sealed sync file with three more optional parts: `labels`, `views` and `taskLog` (History). The model lives in Core (`Domain/Task*.cs`, `Sync/SyncTask*.cs`, `Tasks/`); the running session's checklist keeps its `SessionTask` shape, with a mapper both ways.

**Field-level merge.** Each task carries a stamp per field (title, note, steps, label, tags, bin with previous bin, position and done time, reminders, estimate, due, highlight, can't miss, devices, calendar, number, plan). `TaskFieldMerge` merges two live copies field by field, newest stamp first, then device id, then value, so every device picks the same; tombstones, revives, local stubs, the 30-day rule and the clamp stay `SharedListMerge`'s. For the title and the note a stamp also keeps the hashes of the values it was edited from. A version made after seeing the other wins whatever the clocks say; when neither saw the other, the newer is shown and the other is kept on the task as a pending "Changed on both devices" version. Picking one is an edit whose history holds both values, which closes the conflict on every device. We chose a short list of earlier hashes over a single base hash because a single one turns two edits in a row on one device into a false conflict.

**Labels and tags.** The category becomes the label, at most one per task, with an optional opacity and a rings-on device list. Its id is a version-5 style GUID of the trimmed, lower-cased name, because the old fields name categories without ids and both platforms can compute it without talking. ADR 72's labels become tags, unchanged in behaviour.

**Numbers.** One device hands out final numbers: the PC with the lowest device id among the group's PCs, since no owner or primary device exists and both sides can compute this rule. Other devices give temporary numbers, shown the same way. The numbering device makes them final on its next merge, keeping the value when no final number has it, and keeps any replaced value as a former number so "143b" written meanwhile still resolves. A final number beats a temporary one in the merge, and a repeated final number is renumbered, so no two final numbers are ever alike.

**Old fields for one release.** A new PC rebuilds `nextTasks`, `later`, `parked`, `comebacks` and `categories` from the task list on every write and never stores them on their own. Next has no old home, so it travels in `later` with a new optional item marker, `bin: "next"`. Because an older app drops the marker when it passes an item on, a reader never takes its absence as a move between Next and Later. A file without `tasks` is read item by item: a field changes only where the old item differs from what the PC would write and is newer than that field's stamp, and fields the old types can't carry are never dropped. A file with `tasks` is the truth for its device.

**Migration.** A pure function moves the old stores into the new model once, after the database backup and behind a marker document, with no SQLite or profile schema change. The first actions of the next session's list fill This session, the rest go to Queued, tasks done before today and ticked Later items and come-backs go to History, and Auto-saved past its 30 days is left out. Ids are kept and no stamp comes from the clock, so a second run, or a run on another PC, adds nothing when merged. The old documents stay for a release as the way back.

## Consequences

Edits on different devices to different parts of a task no longer overwrite each other, a move between bins is a field change instead of a delete and re-add, and the phone's later run has one shape to mirror. The sync file grows by the per-field stamps, roughly a few hundred bytes per task, and carries the old fields beside the new ones for a release. When the phone moves to the new parts, the old fields and their reader can go.

The old-field reader stays the main risk of lost edits. An old app sees only whole items, so an edit it makes after the PC moved a task to another old list, before it received that move, can move the task back. An old item also always carries every field it can, so a stale old copy that is newer than a field's last PC edit overwrites that field. That is the item-level rule the lists follow today, so this run makes nothing worse. Numbers depend on the numbering PC syncing; until it does, other devices show temporary numbers. A newly linked PC with a lower id takes over numbering, which the repeat rule keeps safe but may renumber a few tasks. `doc/hard-rules.md` records the new parts, what each may carry, that task text travels only inside the sealed files, and that the old fields stay for one release.
