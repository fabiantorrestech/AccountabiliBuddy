# AccountabiliBuddy

Windows desktop focus guardian. Runs at startup, watches the visible window on every monitor, compares it to the active focus profile, and escalates (toast → overlay → minimize/block) when the user drifts. Companion apps (Android, Linux) come later.

## Hard rules
- **Local only, except read-only calendars (ADR 9, supersedes ADR 2).** Activity data, OCR text, labels and reasons never leave the device. The only outbound connections are read-only downloads of iCal links the user adds and Google sign-in + read-only Calendar API calls for accounts the user connects, all in `CalendarNetwork` (one auditable class). The browser extension talks only to the local native messaging host. Any other connection needs a new ADR.
- Core (`src/AccountabiliBuddy.Core`) has **no UI or OS dependencies**. Anything Windows-specific goes in `src/AccountabiliBuddy.Platform.Windows` behind a `Core/Ports` interface (ADR 4).
- Profiles/rules are persisted in a versioned JSON schema so a Kotlin Android companion can read them (ADR 7). Current: v4 (v2: unlock options, off days, lock settings; v3: looser-change delay, ask about unlisted sites; v4: time off (all day or hours, replaces off days) and one-off blocks); any shape change bumps it with a migration in `ProfileDocumentSerializer`.
- Detection cascade, cheapest first: process/title/URL rules → OCR only when still unknown. Event-driven, plus a slow periodic recheck. Keep CPU and battery use low.
- Activity history (ADR 8): only times, app (process name), site (domain), classification and session context are stored readable. Window titles, full URLs, OCR text and early-end reasons are always DPAPI-encrypted (`IDataProtector`). Kept 90 days; logging and screen reading are revocable in the Setup tab, and screen reading is off by default.
- App-owned preferences and device-local state (quick session, privacy, integrations, watched browsers, the running-session checkpoint, the current unlock, pending looser changes, calendar sources/events/cache/settings; iCal addresses and Google tokens DPAPI-encrypted) live in the `documents` table via `SqliteJsonDocumentStore`, never in the shared profile JSON.
- No escape hatches during focus: Profiles, the schedule grid, Settings and every Setup Revoke/Delete are locked. The only ways out are ending early (wait + reason) or a profile's unlock option (wait + reason, then blocks lift until relock, midnight by default). A killed or crashed app resumes its session at the next launch. Windows of a watched browser that the extension can't see (private windows, profiles without the extension) count as distracting.
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
dotnet run --project src/AccountabiliBuddy.App
```

## Roadmap
MVP milestones M0–M7 (setup → core → watcher → app shell → enforcement → browser → OCR → logging) are done, plus the quick-session popup and two anti-escape passes (Setup lock, session resume, unseen browser windows, unlocks, off days; delayed looser changes, unknown escalation, ask about unlisted sites, scheduled end-early wait) and the Exceptions tab (time off, one-off blocks, in-app events, iCal links, Google Calendar). Device sync is planned in `doc/plans/device-sync.md` (not started). Post-MVP: V2 habit suggestions + distilled local classifier (synthetic frontier-generated data; real samples opt-in and redacted only); V3 Android companion + LAN/Bluetooth sync, Linux, tamper resistance.

## Conventions
- Explicit, readable code; SOLID; one public type per file; file-scoped namespaces; nullable enabled.
- No tests unless asked (Core is designed to be unit-testable).
