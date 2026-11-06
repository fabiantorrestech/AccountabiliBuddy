# 71. Back up settings by kind, and let a device stop syncing its task lists

Date: 2026-11-06

## Status

Accepted

## Context

Until now the PC had one manual backup with four parts (profiles, settings, calendars, history). Its "settings" part held just seven documents, so most of the PC's settings (overlay, sounds, theme, noise, work hours and more) were in no backup at all. The phone had its own file of its settings with an auto-backup to a folder. Neither split what every device shares from what belongs to one device, so a restore on one device could carry another device's local choices, and nothing backed up the shared settings automatically. ProductaBuddy (ADR 69) added a second copy of several settings per device, which made the gap larger.

The owner asked for backups that separate what syncs from what doesn't, each with its own export, import and auto-backup, with the auto-backups also kept on Google Drive. They also asked for a switch that lets a device keep its task lists to itself, and for the task lists to be exportable on their own. They asked to keep the activity history out of automatic backups, with export and import only, until device sync is reliable.

The hard rules allow Google Drive only for sealed sync files in the app's own folder (ADR 26). The removed-device backups of ADR 55 already store a sync file sealed with the group's key there.

## Decision

We will back up by kind. There are four kinds, written in the same backup file on the PC and the phone (the PC's `BackupEnvelope`: readable, or AES-GCM with a password). The content names its kind and the platform that wrote it.

| Kind | Holds | File |
|---|---|---|
| Synced settings | Profiles and rules (with the day plan), sleep and bedtime reminders, the day bar, the daily goal, the mode; plus the task lists while this device shares them | `.absettings`, opens on the PC and the phone |
| This device's settings | Every device-local setting, both modes' copies, and on the PC its calendars | `.abdevice`, refused on the other platform |
| Task lists | Tasks, Later, come-backs, task categories, in their sync form | `.abtasks` |
| History (PC) | The activity history, as before | `.abbackup`, export and import only |

**Coverage:** on the PC, a registry (`DeviceSettingsRegistry`) lists every stored document as a device setting, a synced setting, a task list, a calendar document, or state that is never backed up. A dev build names any stored document that is on no list at start, so a new setting can't be left out silently. On the phone, the existing preference and file lists gain the per-mode copies.

**Restoring:**
- **Synced settings** go through the services that own them, as an edit made by hand. A looser part waits, the mode follows ADR 68, and the change reaches the other devices at the next sync. The phone takes the profiles and day bar as from a PC's backup, since the PCs own them.
- **Device settings with their own wait** (the quit guard and PC-off) go through their services too, so an old backup can't loosen them at once.
- **All restores** are refused during a focus session the user started, as before.
- **Task lists** offer Merge (the newest edit of each item wins) or Replace (the backup's live items are stamped now, so they win; nothing is deleted by an old backup).

**Auto-backup:**
- **What:** synced settings and this device's settings, plus the task lists while the device doesn't share them, each with its own switch, all on by default.
- **When:** on the PC, every 2 minutes, a kind is written when its content changed, once a day, and on an app update. On the phone, a minute after a change, on an update, and by hand.
- **Where locally:** the PC writes to `Documents\AccountabiliBuddy backups\<kind>\`; the phone writes to the folder the user picked. The newest 10 per kind stay.
- **Drive copies:** with Drive sync on in a device group, a copy goes to `Settings backups/<kind>/<device>/` in the app's Drive folder. Each copy is sealed with the group's key, as the one document of a sync file, and the newest 10 stay. A device in the group can list them and restore one.

**Task list sync switch:** each device gets "Sync task lists with my other devices", on by default. Off, its sync file carries no task lists or categories, which ADR 48 already reads as "keeps no shared lists", and it takes none from others. Turned back on, its lists merge with the group's by the usual rules; items deleted elsewhere meanwhile stay deleted.

## Consequences

**What improves:**
- Every PC setting is now in a backup, and a test in dev builds keeps it that way.
- A restore can no longer carry one device's local choices to another, and it can't loosen a guard without its wait.
- The shared settings are backed up without anyone thinking about it, and a new device in the group can restore them from Drive.

**What it costs:**
- Drive now holds settings backups as well as sync files. They are sealed with the group's key, so Drive still holds nothing readable, and a backup made before the key changed (a device was removed) can no longer be opened. The hard rules list the new Drive use.
- The backup format grows three optional content fields. Files from before kinds still open, as their old parts.
- The kind and platform sit inside the content, so a password-protected file is checked only after its password is entered.
- A device that stops sharing its task lists drops out of the group's lists. Its own lists then depend on its own backups.
- History stays out of automatic backups until device sync is reliable enough to revisit it.
