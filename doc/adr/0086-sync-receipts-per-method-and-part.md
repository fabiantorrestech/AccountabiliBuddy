# 86. Sync receipts per method and part

Date: 2026-11-13

## Status

Accepted (owner, 2026-11-13: approved the SYNC1 sync troubleshooting plan, which needs this field)

Amends [22. Share progress between your own devices through sealed files](0022-share-progress-between-your-own-devices-through-sealed-files.md) with one more optional field in the sealed sync file.

## Context

The owner wants to troubleshoot sync from either app: see, per sync method (shared folder, Google Drive, relay, QR, file import), which other devices confirm they received this device's newest file, and which parts of it (tasks, goals, goal history, sessions, day plan, settings, steps…) they applied. Today a sync file carries only `acks` (ids of the phone actions a PC applied), `handoverAcks`, `goalYearsMerged` and the device card's `seen` map (the newest `writtenAt` held from each device, however it arrived, including copies carried by a third device). None of these says by which method a file arrived or whether its parts were applied, and putting objects into `acks` would make older apps unable to read the whole file.

## Decision

A sync file may carry an optional `receipts` list, appended last: one entry per other device in the group this device holds a file from (never itself, never a removed device), holding that device's id, the newest `writtenAt` held from it, per method the newest `writtenAt` that arrived by that method, per method the reason the last attempt failed (`damaged`, `otherGroup`, `tooBig`, `newerFormat`), per part the outcome of the last accept (`taken`, `held`, `off`, `absent`, `refused`) with the `writtenAt` it judged, and the goal history months merged from it (last three months; years stay in `goalYearsMerged`). Receipts carry ids, times and fixed codes only: never a name, title, file name, path or address. They are read leniently (a bad entry is dropped, never failing the file), bounded (16 entries, 5 methods, 12 parts, 3 months each), stripped from QR files, kept in the receiver's stored copy (they hold no text) and never put in a backup. `acks` and the device card's `seen` stay as they are. A receipt rides in the receiver's next file by any method; receiving a file never triggers a write, so receipts can't loop. Older apps ignore the field, and their rows show "older app: no per-part receipt".

Without a sync method (a single device, "works alone"), nothing is written or shown beyond "Not syncing".

## Consequences

Both apps can show a per-method, per-device and per-part confirmation board with green, yellow and red dots, judged by a pure rule in Core (with a Kotlin twin) against the receiver's own latest file, so a device that simply hasn't written since is yellow, not red. Each sync file grows by a few hundred bytes per device. The receiver keeps a small device-local record of what it received and applied, by method.
