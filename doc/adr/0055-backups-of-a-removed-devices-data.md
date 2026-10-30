# 55. Backups of a removed device's data

Date: 2026-10-30

## Status

Accepted

Amends [26. Sync through Google Drive only](0026-sync-through-google-drive-only.md)

## Context

When a device is removed from the group (ADR 54), its data is already on the other devices. That covers the focus sessions, phone pickups, sleep nights and drift carried in its sync file. The user wants a choice between keeping that data in History and Stats (marked as the removed device) and erasing it everywhere. Before either, they want the data backed up, so a later setup can restore it.

Their order of preference is: Google Drive, inside the app's own sync folder in a folder for that device, when Drive is how they sync. If Drive is full or not linked, a local backup on the device doing the removal. And on every platform, a new setup offers "restore from an older device".

ADR 26 lets the app use Drive with the `drive.file` scope, in its own folder, for sealed sync files only. A backup is a new kind of file in that folder, so it needs recording. The privacy rules still apply: nothing readable leaves the device. Window titles, addresses and notes never go anywhere except inside sealed files between the user's own devices.

## Decision

A backup of a removed device is that device's last received sync file, re-sealed with the group's new key (ADR 54). It stays a sealed `.absync` file that only the user's linked devices can open. It is written to one of two places:

- **Google Drive,** when Drive is a sync method in use: `AccountabiliBuddy sync/Backups/<device name>/<date>.absync`, still under `drive.file`. Nothing else is stored there, and the backup carries no more than the device's sync file already carried.
- **A local file,** when Drive is full (the upload fails on quota), not linked, or not wanted: a file the user picks. On the PC this defaults to `Documents\AccountabiliBuddy backups\`; on the phone it goes through the system file picker.

The remove dialog then lets the user keep the data, shown as "<name> (removed)", or erase that device's data from every device. Shared list items it added (tasks, Later items) stay either way, because they belong to the whole group.

Every platform's setup assistant gains a "Restore from an older device" step after linking. It lists the backups found on Drive under `Backups/`, or takes a local backup file. Restoring brings that device's history back, shown under its old name. A backup sealed with a key the device doesn't hold cannot be opened; it can only be restored in the group that made it.

## Consequences

A removed phone's history is never lost by accident, and it can come back on a replacement device. Backups stay as private as sync files: sealed, on the user's own Drive or disk, and opened only by their devices.

The Drive folder now holds files that do not belong to a live device, and a user who removes many devices accumulates them. They are small, but nothing prunes them automatically. The user deletes them from Drive by hand, and the restore list shows their dates.

A backup is only useful inside its group. If the user leaves the group or starts from scratch, old backups can't be opened unless they rejoin the same group. Re-keying after the backup keeps it readable, because it was sealed with the new key and later rekeys carry the group forward. A user who starts a brand-new group loses access. The restore step says so instead of failing silently.

Detecting "Drive full" relies on the quota error from the upload. The fallback to a local file is offered then, not guessed in advance.
