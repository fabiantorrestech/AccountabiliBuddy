# Automatic backups to a folder: design

Date: 2026-10-21. Status: **draft, waiting for the user's answers** (questions at the end). Not started.

## Why

The user asked for automatic backups on the PC and the phone:
- on by default, written whenever settings or configuration change;
- to a default folder, or to one the user picks.

Today, backups only happen when the user exports one by hand (Settings → Backup), plus the PC's own copies before a database migration.

## Goals

1. **On by default** on both apps, with no setup.
2. **Written on change:** a new backup a short while after any setting or configuration change (profiles, rules, day plan, sleep, day bar, timer, noise player, privacy choices, calendars, sync and watch settings), not on every keystroke.
3. **A default folder, or the user's own**, chosen in Settings (PC) or Settings → Backup (phone).
4. **Restorable with what exists:**
   - PC backups are ordinary `.abbackup` files that Settings → Backup → Restore and the setup assistant's import already read;
   - the phone gets a matching restore for its own backups.
5. **Never fills the disk:** old automatic backups are pruned; files the user made by hand are never touched.

## Non-goals

- Backing up the activity history on every change (it changes all the time). See question 2.
- A cloud service of our own. A folder that happens to be synced by Drive or OneDrive is the user's choice.

## Proposed behaviour (my calls, to confirm)

**When**
- **Trigger:** 30 seconds after the last change (a burst of edits makes one backup).
- **Startup:** once at startup if the newest automatic backup is older than the last change.
- **During focus:** never while a restore runs; during a focus session it still writes, since only reading settings is involved.

**What**
- **PC:** the parts Profiles + Settings + Calendars, **without secrets**. That's today's no-password export: calendar links and Google sign-ins are left out.
- **Phone:**
  - its own state: phone choices, sleep and day bar, the day plan edit waiting for the PC, the watch link settings, Devices and sync settings;
  - the PC's documents it holds.

  Never the sync link code or device tokens.

**Where (defaults)**

| | Default | Pick your own |
|---|---|---|
| PC | `%UserProfile%\AccountabiliBuddy Backups` (a local folder, deliberately not Documents, which on this PC syncs to OneDrive; see question 1) | A folder picker in Settings → App & data → Backup |
| Phone | `Documents/AccountabiliBuddy` in the phone's shared storage (kept after an uninstall; no storage permission needed on Android 10+) | Android's folder picker (the choice is remembered) |

**Files and pruning**
- **Name:** `AccountabiliBuddy-auto-2026-10-21-1530.abbackup` (PC) / `AccountabiliBuddy-phone-auto-…abbackup` (phone).
- **Kept:** the newest 10, plus the newest of each of the last 14 days.
- **Only its own files are deleted:** files matching `*-auto-*` that this device wrote.

**Settings**
- **The switch:** "Automatic backups" (on), the folder, "Last backup: 3:30 PM".
- **"Back up now"** and **"Open folder"** buttons.
- **A problem** (folder gone, no permission, disk full) shows in Settings and the Devices tab. It never interrupts focus.

## Fits with what exists

- It's a document-backed setting ("backup.auto": on/off, folder, last written) in each app's own store.
- It reuses `BackupService.ExportAsync` on the PC.
- On the phone, a small writer for its own state, read back by an extended import.
- Restoring an automatic backup goes through the existing restore, so looser profile changes still wait (ADR 10 rules unchanged).

## Questions only the user can answer

1. **PC default folder:** a local folder (`%UserProfile%\AccountabiliBuddy Backups`), or Documents (which on your PC syncs to OneDrive, so you'd get an off-PC copy for free)?
2. **Activity history:** settings and configuration only (small, frequent), or also the history once a day (bigger)?
3. **Encryption:** plain, like today's no-password export (readable by whoever can read the folder), or sealed with your sync link code so only your own devices can restore it? The second matters if the folder is synced to a cloud.
4. **Order:** build it after flexible lunch, or before?
