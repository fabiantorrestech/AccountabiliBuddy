# 10. Package with Velopack

Date: 2026-10-19

## Status

Accepted

## Context

Until now the app only ran from a build folder (`dotnet run` or bin\Debug). The user wants to install it like any other program, test it as a real install, and keep installing newer builds over the old one. Every update must keep the user's data (profiles, history, calendars, all in `%LocalAppData%\AccountabiliBuddy`), migrate it when the format changes, and leave the browser connections and "start with Windows" working even though the program's folder moves. A script should rebuild and repackage in one step.

The options were Inno Setup (a classic installer; needs its compiler on the build PC, nothing in the app), WiX (an MSI, built by `dotnet build`, awkward for per-user installs), a portable zip only (no Start menu entry, uninstaller or in-place upgrade), and Velopack. The user chose Velopack.

## Decision

The app is packaged with Velopack (`Velopack` NuGet package in the App, the `vpk` command-line tool on the build PC), version 1.2.161:

- Per-user install to `%LocalAppData%\AccountabiliBuddyApp`, no admin rights. The package id is deliberately not `AccountabiliBuddy`, because that folder holds the user's data and Velopack replaces or removes its own folder on update and uninstall.
- `Program.Main` runs Velopack's startup hooks first. Uninstall removes what the app registered (start with Windows, browser connections, the Start menu quick-session shortcut) but keeps the data, so a reinstall picks up where the user left off; "Delete my data" in the Setup tab stays the way to remove it.
- At launch the app repairs registrations that point to an older location (the start-with-Windows entry and the browser connection manifests) and refreshes its copy of the browser extension when the bundled one is newer.
- Before any database or profile-schema migration, the database is copied to the data folder's `backups` folder; a database newer than the app is not opened.
- Updates come only from packages built locally by `scripts/package.ps1` (run the new Setup.exe, or Update.exe with the local package folder). The app never checks for updates on the internet; that would need a new ADR under ADR 9.
- Builds are unsigned for now, so Windows SmartScreen warns on first install.

## Consequences

Installing, updating over an older version and uninstalling work like ordinary Windows programs, and each build also produces a portable zip. Velopack adds one dependency to the App and one tool to the build PC, and its folder layout (a stable stub exe in the install root, the real files in `current`) is what registrations must point to. Development builds and the installed app share the data folder and the single-instance lock, so only one of them runs at a time. Code signing and online updates remain open decisions.
