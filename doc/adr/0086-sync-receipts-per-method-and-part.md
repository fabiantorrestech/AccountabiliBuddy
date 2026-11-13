# 86. Sync receipts per method and part

Date: 2026-11-13

## Status

Accepted (owner, 2026-11-13: approved the SYNC1 sync troubleshooting plan, which needs this field)

Amends [22. Share progress between your own devices through sealed files](0022-share-progress-between-your-own-devices-through-sealed-files.md) with one more optional field in the sealed sync file.

## Context

The owner wants to troubleshoot sync from either app: see, per sync method (shared folder, Google Drive, relay, QR, file import), which other devices confirm they received this device's newest file, and which parts of it (tasks, goals, goal history, sessions, day plan, settings, steps…) they applied. Today a sync file carries only `acks` (ids of the phone actions a PC applied), `handoverAcks`, `goalYearsMerged` and the device card's `seen` map (the newest `writtenAt` held from each device, however it arrived, including copies carried by a third device). None of these says by which method a file arrived or whether its parts were applied, and putting objects into `acks` would make older apps unable to read the whole file.

## Decision

A sync file may carry an optional `receipts` list, appended last: one entry per other device in the group this device holds a file from (never itself, never a removed device), holding that device's id, the newest `writtenAt` held from it, per method the newest `writtenAt` that arrived by that method, per method the reason the last attempt failed (`damaged`, `otherGroup`, `tooBig`, `newerFormat`) and, in `failedAt`, the receiver's own clock at that attempt (so a sender can tell a failure on its newest file from an old one-off), per part the outcome of the last accept (`taken`, `held`, `off`, `absent`, `refused`) with the `writtenAt` it judged, and the goal history months merged from it (last three months; years stay in `goalYearsMerged`). Receipts carry ids, times and fixed codes only: never a name, title, file name, path or address. They are read leniently (a bad entry is dropped, never failing the file), bounded (16 entries, 5 methods, 12 parts, 3 months each), stripped from QR files, kept in the receiver's stored copy (they hold no text) and never put in a backup. `acks` and the device card's `seen` stay as they are. A receipt rides in the receiver's next file by any method; receiving a file never triggers a write, so receipts can't loop. Older apps ignore the field, and their rows show "older app: no per-part receipt".

A device with at least one other active device writes the list even when it is empty (so a receiver can tell "writes receipts, none yet" from an older app); a device alone in its group writes none. A device's removal backup (ADR 55) never carries receipts. Without a sync method (a single device, "works alone"), nothing is written or shown beyond "Not syncing".

## Consequences

Both apps can show a per-method, per-device and per-part confirmation board with green, yellow and red dots, judged by a pure rule in Core (with a Kotlin twin) against the receiver's own latest file, so a device that simply hasn't written since is yellow, not red. Each sync file grows by a few hundred bytes per device. The receiver keeps a small device-local record of what it received and applied, by method.

SYNC1b (the PC): the board's colours also need what this device itself sent, so a small device-local send log (`sync.sendlog`: per method the file time and when it reached the method, times only) keeps `PreviousSentAt`, judged against the receiver's own write time. A method the receiver has no record of (it confirms my files by other ways only and this device never read a file of it by that way) is grey "not used by that device", never red; the "files stopped arriving" rule uses the caller's real interval for the method and needs a file read by that method before. The sync log and the diagnostics report take only typed inputs (a method, a direction, numbers, a fixed note) and name devices as "PC 1" and "Phone 1".
