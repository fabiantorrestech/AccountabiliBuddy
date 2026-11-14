# 87. Sync session notes between your own devices

Date: 2026-11-13

## Status

Accepted (owner, 2026-11-13: "Yes, they should sync between devices. Phone should get full archive.")

Amends [72. Sync every task list, with labels, and keep local items on a chosen device](0072-sync-every-task-list-with-labels-and-local-items-kept-on-a-chosen-device.md): its task history carried "never session notes or reasons"; notes now travel, reasons still never do.

## Context

The Archive (the PC's Notebook) shows come-backs, notes kept in Focus mode, tasks left unfinished and tasks done, plus Recently deleted. Since ADR 72 the tasks travel between the user's devices in the sealed sync files, but session notes were deliberately left on the PC that wrote them, so two PCs show different Archives and the phone has none. The owner noticed the two PCs' Archives differ and asked for one Archive everywhere, including a full one on the phone. Notes are the user's own private text: the hard rules keep such text off every outside service, but already let task titles, steps and task notes travel between the user's own devices inside AES-256-GCM sealed files (ADR 22, 26, 46).

## Decision

We will carry session notes in the sealed sync file as an optional, shared list merged like the other shared lists (ADR 48 rules: newest edit wins, deletions as tombstones kept 30 days, the same 90-day window as the task history), with each note's id, text, the session it belongs to (start time and focus profile id, never a window title, app or URL), its written and edited times, and its deletion time. Notes are governed by the same "Sync task lists" switch as the task history (ADR 71), are never put in a QR file, never sent on to the watch, never logged and never written to the relay or Drive outside the sealed files. Session reasons (why a session ended early, unlock reasons) still never leave the device.

## Consequences

Every PC shows the same Archive: the same notes, come-backs and unfinished and done tasks, with edits and deletions made anywhere. The phone can show a full Archive (come-backs, notes, unfinished and done tasks, Recently deleted) built from the same synced data, so it gains notes it never had. Sync files grow by the notes' text (small next to the task history). Older apps ignore the new field, so a note edited on an older app is not seen as an edit until that app updates. A note now exists on every device of the group, so deleting it means deleting it everywhere, which the tombstone already does; a user who wants a note kept on one device has no option for that yet.

Owner decisions, 2026-11-14 (AR1b review): (1) session notes go into a backup only when it is password-protected (a Task lists backup with a password, as the goals' notes); a plain export or an auto-backup never holds them, and a restore merges them like another device's notes, also on a PC not yet linked to a group. (2) A note's tombstone is kept for the note's whole 90-day window, not 30 days, so a backup restored late never brings a deleted note back.
