# 48. Share tasks and come-backs across devices

Date: 2026-10-27

## Status

Accepted

Amends [24. Let session task names reach your own devices](0024-let-session-task-names-reach-your-own-devices.md) (tasks are edited on every device, not only shown) and [30. Carry come-backs to the user's devices](0030-carry-come-backs-to-the-users-devices.md) (come-backs and parked thoughts are no longer read-only on the phone).

## Context

Each PC keeps its own session checklist, its own "Come back to" list and its own parked thoughts. The phone and watch can see a PC's running tasks and open come-backs (ADR 24, 30), and the phone can tick a task or park a thought through a sync action (ADR 26, 42), but it can't add or edit tasks. Two PCs never share a list.

The user works across two PCs and a phone and wants one list everywhere. On 2026-10-27 they asked for four things:
- the next session's task list is shared;
- come-backs and parked thoughts are one list;
- the phone can add and edit tasks;
- edits made elsewhere reach a running session's checklist.

Task names already go only to the user's own devices, sealed, and the three-tasks-per-session rule (phase 11) and the no-escape-hatch rules must keep holding.

## Decision

We will share three lists between the user's devices through the sealed sync files (ADR 22), using any sync method (folder, Drive, relay):
- the **next-session list** (Today's draft);
- **Come back to**;
- **parked thoughts**.

Each item has a stable id, its fields, an edit time and, when deleted, a tombstone kept 30 days. Devices merge the lists item by item: the newest edit of each item wins, and a tombstone newer than an item's last edit removes it. Order is a per-item position, also newest edit wins.

A **running session's checklist** stays owned by the PC that runs it. Other devices change it only by sync actions (add, edit, reorder, tick, park) that this PC applies under its own rules:
- the three-tasks rule (a 4th is parked or needs Add anyway on the PC);
- no change that loosens the session;
- late actions refused, as ADR 42 says.

The phone gets an editor for the next-session list, come-backs and parked thoughts, and sends running-session edits as actions. The watch stays read-only.

## Consequences

The user plans on any device and finds the same list everywhere. A task ticked on the phone or the other PC shows done on all of them after a sync, and a come-back written on one PC can be handled on another.

Lists now merge, so two devices editing the same item offline keep the later edit and lose the earlier one; two different items always both survive. Tombstones grow the files a little, bounded by the 30-day expiry. The speed depends on the sync method: seconds with the relay or Drive, up to the folder tool's interval with Autosync. Task text stays end-to-end sealed and never reaches anything but the user's devices. The old read-only paths (ADR 30's open come-backs in the link state) are replaced, so an older phone sees the shared lists as read-only until it updates.
