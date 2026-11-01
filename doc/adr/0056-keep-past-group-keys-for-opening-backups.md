# 56. Keep past group keys for opening backups

Date: 2026-11-01

## Status

Accepted

Amends [55. Backups of a removed device's data](0055-backups-of-a-removed-devices-data.md)

## Context

ADR 55 seals a removed device's backup with the group key in use right after the removal, and says later re-keys carry the group forward so the backup stays readable. Building it showed that this is false. Every removal (ADR 54) makes a new random link code, which gives a new group id and key, and the old key is thrown away. A backup therefore becomes unreadable at the next removal. A device that joins the group later has never held the old key at all. The restore step could only tell the user to link to the old group, which no device can do any more.

The user wants backups to stay restorable on any of their devices, including a replacement set up later. The constraints are the ones in ADR 54. A removed device must not learn keys made after its removal. Nothing readable leaves the user's devices. No library is added. The PC and the phone must read the same bytes.

## Decision

We will keep the group's past keys and share them only inside sealed sync files.

When a device switches key (applying or making a rekey), it adds the outgoing group id and its AES key to a local list of past keys. The list holds the derived AES key, never the link code, because the code also admits a device to the relay. It is stored encrypted like the identity (DPAPI on the PC, `TokenVault` on the phone) and capped at the 20 newest entries, each kept at most a year.

Each device writes the list as an optional field in its sealed sync file. Readers merge it as a set by group id, so a device that joins later learns every key the group still keeps. The file is sealed with the current key only, so a removed device cannot open the files that carry keys made after its removal.

Past keys are used for one thing only: opening a backup whose header names a past group id. They are never used to read or write sync files.

## Consequences

A backup made before any number of later removals can still be restored, from Drive or from a local file, on any device in the group, including one set up afterwards. The restore step's "made in a different group" message now means a group the user really left or never joined.

Every device's sync file carries a small list of keys. Its loss is no worse than the loss of the sync file's own key, because whoever can open the file can read the group's data anyway, and old data was readable under those keys before. A removed device still holds every key up to its removal, which ADR 54 already accepts.

Older apps ignore the field. Backups made while a device ran an older app can still only be opened with the key they were sealed with. The list is a new part of the sync contract and must stay readable by later versions. Past the cap, the oldest backups become unreadable again; the restore step says so instead of failing silently.
