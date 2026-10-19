# Setup assistant, imports, backups and installer: plan

Status: **built 2026-10-19** (S1–S5). See ADR 10 for packaging.

## Context
New users (you first) should get up and running without retyping everything from other blockers. You also want an installer to test the app as a real install, plus a repeatable rebuild and repackage step. Each new installer must update over the old one, keep the data, migrate it, and allow settings export and import.

Order: **S1 import → S2 setup assistant → S3 backups → S4 installer + package script → S5 checks.**

## What was found on this PC
- **Cold Turkey:** `C:\ProgramData\Cold Turkey\data-app.db`, table `settings`, one row.
  - The value is "CTB17" + hex; each byte − 0x11 gives plain JSON: `blocks`, `settings`, `additional`.
  - Each block has: `web[]`, `exceptions[]`, `apps[]`, `type` (continuous or scheduled), and `schedule[]` (`"d,h,m"`; days 1–5 = Mon–Fri, so 0 = Sun).
  - `break` holds e.g. `delay,10,m,20,…`.
  - Undocumented format: a Cold Turkey update can break it, so the importer fails softly and falls back to paste.
  - Your 5 blocks: Frozen Turkey (empty), Distractions (75 sites), p (224), fabs work distractions (9 sites, Mon–Fri 07–12 + 13:30–17), Amazon (1 site, Mon–Fri 05–17, delay 10 min).
- **Cold Turkey usage:** `data-browser.db` `stats` (date, domain, seconds) and `data-helper.db` `statsApp` (date, file, seconds). Plain SQLite, ~1 year.
- **AppBlock (Edge):** `…\Edge\User Data\<profile>\DNR Extension Rules\lniombipmmhhighllijmcdncnefpjloi\rules.json`. This is the browser's standard rules format, so it's reliable: `urlFilter` such as `||tiktok.com` (4 sites). It has no schedule.
- **BlockSite (Chrome):** `Local Extension Settings\eiimnmioipafcokbfikbljfdeojpcgbh\000003.log`.
  - LevelDB log holding plain JSON (`blockedSites`, `activeDays`, `timeIntervals`).
  - Your list there is empty; it's probably kept in BlockSite's cloud account.
  - Best effort, else paste.
- **Not installed here:** LeechBlock, Freedom, FocusMe, Curbox. Curbox appears to be an Android app (F-Droid); its "web extension" and any export format still need to be checked.

## S1. Importers (Core, plus a Platform port for locations)
- `Core/Import/`: one reader per source, returning `ImportedBlockList(Source, Name, Sites, Exceptions, Apps, WeeklyWindows, UnlockDelay?)`. Readers take text, bytes or a DB path, so Core stays OS-free.

  | Source | Reader | What comes over |
  |---|---|---|
  | Cold Turkey | `ColdTurkeySettingsReader` (decode + JSON) | blocks, apps, exceptions, schedules, delay-break → unlock option |
  | AppBlock (Chrome, Edge, Brave) | `DnrRulesReader` (`rules.json`) | sites |
  | BlockSite | `BlockSiteStorageReader` (scan the LevelDB `.log` for the latest JSON values; no `.ldb`/snappy) | sites, schedule if present |
  | LeechBlock NG | `LeechBlockOptionsReader` (its exported options file; check the format against its GitHub source) | sets: sites, times, days |
  | Curbox | its export file if one exists (check), else paste | sites |
  | Freedom, FocusMe, anything else | existing paste box, with per-app "where to copy it" hints | sites, apps |
- Lines are turned into rules by the existing parser, `Core/Engine/BlockListParser.cs`. It already handles wildcards, paths, `.exe` and `C:\…\x.exe`.
- **`ImportPlanner`** turns lists into profiles + `ScheduleBlock`s:
  - One profile per list; blocks with no schedule become profiles you start manually. Empty lists (Frozen Turkey) are skipped and shown.
  - **Overlaps are merged** (your choice):
    - Sweep each weekday's boundaries.
    - A slot covered by one list gets that profile. A slot covered by several gets a combined profile "A + B" holding every list's rules, created once per combination.
    - Adjacent slots with the same profile are joined.
  - Unlock option:
    - A list's delayed break becomes an unlock option with that wait.
    - A combined profile gets one only if all its lists have one, using the longest wait. (My call.)
  - Never overwrites: name clashes get " (Cold Turkey)" added. Imported slots that clash with your existing weekly blocks are left out and listed. (My call.)
- **Usage suggestions** (Cold Turkey stats): your top sites and apps by time over the last 30 days.
- **Platform:** `IBlockerSourceLocator` finds the Cold Turkey DB and per-browser and per-profile extension folders. Files are copied to a temp folder before reading, because Cold Turkey keeps its DBs open.

## S2. Setup assistant (App)
- **When it opens:**
  - automatically on first launch (no `setup.assistant` document);
  - from Setup → "Run setup assistant";
  - disabled during a focus session, like Revoke.
- **Steps** (a separate window, Back / Next / Skip):
  1. **Welcome:** what it does, everything stays on this PC.
  2. **Bring your blocks:**
     - Detected sources with what was found, e.g. "Cold Turkey: 5 blocks, 2 with schedules", "AppBlock (Edge): 4 sites".
     - "Import a file…" (LeechBlock, Curbox, an AccountabiliBuddy backup).
     - Paste box with hints.
  3. **Your usage:** on by default, can be skipped.
     - "Suggest blocks from your Cold Turkey usage": top items with checkboxes and a target profile.
     - Alternative: "Copy that usage into History", **off by default**. Daily site/app totals only, no titles.
     - (My reading of your answer: suggest by default, importing is optional.)
  4. **Review:**
     - Profiles to be created (rename, or untick to skip).
     - A week preview of imported schedule slots, with merges marked "A + B".
     - Unlock options.
  5. **Browsers:** install the extension for each detected browser (reuses the Setup tab's browser section).
  6. **Basics:** start with Windows, Start-menu quick session, activity history on, screen reading off (reuses the Setup tab's toggles).
  7. **Done:** summary; open Schedule.
- **Saving:** everything is saved at the end in one step through `ConfigurationService`, so stricter changes apply at once and looser ones get the normal countdown. On a fresh install nothing is running, so it all applies at once.

## S3. Backups: export and import (Core format, App UI in Settings and assistant step 2)
- **Export:**
  - Settings → "Export settings…": tick parts (profiles, rules & schedule; app settings; calendars; activity history), then choose:
    - **Full (password-locked):** includes calendar links, Google sign-in and history with titles/URLs decrypted. The whole file is encrypted with your password (AES-GCM, key via PBKDF2-SHA256; .NET built-in, no new library).
    - **Without secrets (no password):** plain JSON. Calendar links, Google tokens and the encrypted history fields (titles, URLs, screen text, reasons) are left out; history keeps times, app, site and classification.
  - Never exported, because it belongs to this PC: running session, current unlock, pending changes, clock anchor, calendar cache.
- **Import:**
  - Pick a file → enter the password if it's locked → tick parts (default all).
  - If settings already exist: **Overwrite / Merge / Cancel**.
    - **Merge:** adds the backup's profiles, blocks and calendars next to yours. Where both have a setting, yours stays.
    - **Overwrite:** an automatic backup of the current state is saved first (`backups\` in the data folder) so it can be undone.
  - Locked during a focus session.
  - Profile JSON goes through `ProfileDocumentSerializer`, so older backups migrate.
  - Secrets are encrypted again with DPAPI on import.
- **Format:** `*.abbackup` = `{format: "accountabilibuddy-backup", version: 1, createdAt, appVersion, parts…}`, with an encrypted envelope for Full.

## S4. Installer (Velopack) and package script
- **ADR 10** (adr skill): packaging with Velopack, per-user install, no auto-update from the internet (local packages only; ADR 9 unchanged).
- **New dependencies** (approved by choosing Velopack):
  - `Velopack` NuGet in the App;
  - the `vpk` dotnet tool (`dotnet tool install -g vpk`, dev machine only).
  - Latest stable versions, checked at implementation.
- **packId `AccountabiliBuddyApp`:** installs to `%LocalAppData%\AccountabiliBuddyApp`. It must not be `AccountabiliBuddy`, because that is the data folder and Velopack replaces or removes its own folder.
- **`Program.Main`:** `VelopackApp.Build()…Run()` first. Hooks:
  - install / update → nothing heavy;
  - uninstall (fast) → remove the Run key, native-host registry keys and the Start-menu quick-session shortcut. **Data is kept** (delete it with Setup → Delete my data). (My call.)
- **Repair at launch** (an install moves the exe):
  - Run key points elsewhere (e.g. your Debug build) → rewrite it to Velopack's stable root exe.
  - Native-host manifests point to another `NativeHost.exe` → rewrite them.
  - Bundled extension version newer than the copy in the data folder → refresh the copy and say "reload the extension in your browser".
- **Safe upgrades:**
  - Before any DB (`PRAGMA user_version`) or profile-schema migration, copy the DB to `backups\pre-v{n}-{date}.db`.
  - If the DB is **newer** than the app (an older build installed over a newer one), don't open it; show a clear message instead.
- **Version:** `<Version>` in the App csproj (start 0.1.0), shown in Settings.
- **`scripts/package.ps1 [-Version x.y.z]`:**
  1. Bump the version (default: patch + 1).
  2. `dotnet publish` App, Release, win-x64, self-contained (NativeHost and `browser-extension\` included).
  3. `vpk pack --packId AccountabiliBuddyApp --packVersion … --mainExe AccountabiliBuddy.App.exe`.
  4. Output in `releases\`: `…-Setup.exe` (installer) and `…-Portable.zip` (portable). `releases/` and `publish/` are gitignored.
- **Updating:** run the new Setup.exe; it installs over the old one. Check this behaviour in step 1 of S4. Fallback: the script also leaves the packages so `Update.exe` can apply them.
- **Unsigned build:** Windows SmartScreen will warn the first time ("More info → Run anyway").
- **Note:** a dev build (`dotnet run`) and the installed app share the data folder and the single-instance lock, so close one to run the other.

## S5. Checks
- **Headless:**
  - Cold Turkey reader on a copy of your DB (5 blocks, schedules, overlap merge to "Amazon + fabs work distractions" 07–12 and 13:30–17, Amazon alone 05–07 and 12–13:30);
  - AppBlock rules;
  - BlockSite JSON;
  - LeechBlock sample;
  - planner name clashes;
  - backup round trip (both modes, wrong password, older version);
  - migration backup and the newer-DB guard.
- **UI:**
  - the assistant on a fresh data folder (first run opens it; import; review; finish → profiles and schedule as expected);
  - export/import with Overwrite, Merge, Cancel and selected parts;
  - locked during focus.
- **Package:**
  - run `scripts/package.ps1` → install Setup.exe → the app runs from `%LocalAppData%\AccountabiliBuddyApp`;
  - Run key and native host repaired;
  - package 0.1.1 and install over it → data and profiles intact, version updated;
  - uninstall → data folder kept, registry entries gone;
  - portable zip runs.
- Regression UI suites and a dark/light pass.
- CLAUDE.md (hard rules: backups, packaging), roadmap, handoff.

## Flags
- **Cold Turkey, BlockSite and AppBlock files are undocumented or internal:** imports are best effort and always fall back to paste.
- **Still to check during S1:** Curbox (desktop/web extension? export file?) and LeechBlock's file format.
- **Uninstall keeps data**, so a reinstall picks up where you left off.
