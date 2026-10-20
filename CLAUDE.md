# AccountabiliBuddy

Windows desktop focus guardian. Runs at startup, watches the visible window on every monitor, compares it to the active focus profile, and escalates (toast → overlay → minimize/block) when the user drifts. Companion apps (Android, Linux) come later.

## Hard rules
- **Local only, except read-only calendars (ADR 9, supersedes ADR 2).** Activity data, OCR text, labels and reasons never leave the device. The only outbound connections are read-only downloads of iCal links the user adds and Google sign-in + read-only Calendar API calls for accounts the user connects, all in `CalendarNetwork` (one auditable class), plus update checks and downloads from the public releases-only repo `fabiantorrestech/AccountabiliBuddy-releases` (ADR 12, `VelopackUpdater`), and Google Health sleep once ADR 11 is accepted. The browser extension talks only to the local native messaging host. Any other connection needs a new ADR.
- Core (`src/AccountabiliBuddy.Core`) has **no UI or OS dependencies**. Anything Windows-specific goes in `src/AccountabiliBuddy.Platform.Windows` behind a `Core/Ports` interface (ADR 4).
- Profiles/rules are persisted in a versioned JSON schema so a Kotlin Android companion can read them (ADR 7). Current: v4 (v2: unlock options, off days, lock settings; v3: looser-change delay, ask about unlisted sites; v4: time off (all day or hours, replaces off days) and one-off blocks); any shape change bumps it with a migration in `ProfileDocumentSerializer`.
- History icons: app icons are read from the program file of a running app; site icons come only from the browser extension (the browser's own favicon cache, Chromium `favicon` permission / Firefox data: URLs). Both are cached as files in the data folder's `icons\`. Never download an icon.
- Detection cascade, cheapest first: process/title/URL rules → OCR only when still unknown. Event-driven, plus a slow periodic recheck. Keep CPU and battery use low.
- Activity history (ADR 8): only times, app (process name), site (domain), classification and session context are stored readable. Window titles, full URLs, OCR text and early-end reasons are always DPAPI-encrypted (`IDataProtector`). Kept 90 days; logging and screen reading are revocable in the Setup tab, and screen reading is off by default.
- App-owned preferences and device-local state (quick session, privacy, integrations, watched browsers, the running-session checkpoint, the current unlock, pending looser changes, calendar sources/events/cache/settings, the clock anchor, setup-assistant state, the last-run app version, the day bar and on-screen timer/timeline settings (default timer; per-profile use default/on/off + colour), the time-up sounds, sleep mode (bedtime/wake, weeknights and weekends), omitted sessions (by start time; left out of day bar and totals; key "history.notReal"), History display names for apps/sites ("history.names") and apps/sites hidden from Most used ("history.hidden"), update settings ("updates"), Google setup guide progress; iCal addresses and Google tokens DPAPI-encrypted) live in the `documents` table via `SqliteJsonDocumentStore`, never in the shared profile JSON.
- No escape hatches during focus: Profiles, the schedule grid, Settings and every Setup Revoke/Delete are locked. The only ways out are ending early (wait + reason) or a profile's unlock option (wait + reason, then blocks lift until relock, midnight by default). A killed or crashed app resumes its session at the next launch. Changing the Windows clock doesn't move sessions, waits or countdowns: all time comes from `TrustedClock` (ignores jumps over 2 min, also across restarts in the same boot; after a reboot the Windows clock is trusted). Windows of a watched browser that the extension can't see (private windows, profiles without the extension) count as distracting. A watched browser whose extension isn't allowed in private windows (live report, else read from the browser's own settings files) triggers a repeating reminder (closing it snoozes 5 min; no opt-out) and, during focus, all its main windows count as distracting (app-level, minimized) except its own extensions/settings pages.
- Install and data safety (ADR 10): packaged with Velopack into `%LocalAppData%\AccountabiliBuddyApp` (never the data folder's name); updates come from locally built packages or, in the installed app, the public releases repo (ADR 12: Settings → Updates; asks before installing unless automatic updates are on; never installs during a session; dev builds never check). Data stays in `%LocalAppData%\AccountabiliBuddy` across updates and uninstall. A build not run from the install (dotnet run, test copies) uses `%LocalAppData%\AccountabiliBuddy-Dev` instead (title shows "(dev data)"), unless `ACCOUNTABILIBUDDY_DATA_DIR` is set, so dev sessions never mix with real ones. Before a DB migration and on the first run of a new version the DB is copied to `backups\`; a DB newer than the app is refused. At launch, the installed app repoints registrations from another copy (Run key, Start menu shortcut, native-host manifests) to itself; dev builds and test copies never do. Backups (`*.abbackup`): without secrets = plain JSON; with secrets = password AES-GCM; restore overwrite saves a this-PC-only copy first; restore and the setup assistant's import are locked during focus.
- Stricter changes apply at once; looser ones (`LooseningDetector`) wait `LockSettings.LooseningDelay` (10 min default) with a countdown and Cancel, never take effect during a session, and have no "apply now". Unknown activity escalates (nudge → overlay, never further) after the grace period; the classify prompt has no "not now": "On track" lasts for the session and needs a reason, "add exemption" is permanent and needs a reason. Ending a scheduled block early waits at least the profile's shortest unlock.

## Stack
- .NET 10 (LTS), C#, Avalonia UI (ADR 3)
- Windows platform project targets `net10.0-windows10.0.19041.0` (WinRT: Windows.Media.Ocr, Windows.Graphics.Capture)
- Win32 interop via CsWin32; SQLite via Microsoft.Data.Sqlite (ADR 6); DI via Microsoft.Extensions.Hosting; MVVM via CommunityToolkit.Mvvm
- Browser: one MV3 extension for Chromium (Chrome, Brave, Edge, Opera) + Firefox manifest; native messaging host ↔ App via named pipe (ADR 5)

## Layout
```
src/AccountabiliBuddy.Core/              domain, engines, ports, storage
src/AccountabiliBuddy.Platform.Windows/  Win32/WinRT adapters
src/AccountabiliBuddy.App/               Avalonia tray app
src/AccountabiliBuddy.NativeHost/        browser native messaging host
extensions/                              browser extension (shared + per-browser manifests)
doc/adr/                                 architecture decision records
handoffContext/                          agent handoff (gitignored)
```

## Commands
```
dotnet build AccountabiliBuddy.sln
dotnet run --project src/AccountabiliBuddy.App   # uses %LocalAppData%\AccountabiliBuddy-Dev (not your real data)
pwsh scripts/package.ps1 [-Version x.y.z] [-Publish]   # installer + portable zip in releases/; -Publish uploads them to the public releases repo (needs vpk 1.2.161 and gh auth login)
```

## Roadmap
MVP milestones M0–M7 (setup → core → watcher → app shell → enforcement → browser → OCR → logging) are done, plus the quick-session popup and two anti-escape passes (Setup lock, session resume, unseen browser windows, unlocks, off days; delayed looser changes, unknown escalation, ask about unlisted sites, scheduled end-early wait), the Exceptions tab (time off, one-off blocks, in-app events, iCal links, Google Calendar), the setup assistant (imports from Cold Turkey, AppBlock, BlockSite, LeechBlock, pasted lists), backups and the Velopack installer, the day bar (planned vs actual focus, events, live now line) and the on-screen window (click-through, Ctrl+Alt drag): per-profile session timer with a global default (styles, marks) and the all-day timeline (days with focus blocks; swaps with the timer or stacks); hover fades it, Ctrl+Alt Hide button; white glow + chime when focus or a break is up; sleep mode (day bar runs wake to bedtime, night and lost sleep shaded, evening "sleep if you go now" line coloured by hours). Backlog: progress heatmap. Device sync is planned in `doc/plans/device-sync.md` (not started). Post-MVP: V2 habit suggestions + distilled local classifier (synthetic frontier-generated data; real samples opt-in and redacted only); V3 Android companion + LAN/Bluetooth sync, Linux, tamper resistance.

## Conventions
- Explicit, readable code; SOLID; one public type per file; file-scoped namespaces; nullable enabled.
- No tests unless asked (Core is designed to be unit-testable).
