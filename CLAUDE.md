# AccountabiliBuddy

Windows desktop focus guardian. Runs at startup, watches the visible window on every monitor, compares it to the active focus profile, and escalates (toast → overlay → minimize/block) when the user drifts. An Android companion (ADR 13, `android/`) is in progress; Linux comes later.

## Hard rules
Short version. The full text, with every stored document, synced field and edge case, is `doc/hard-rules.md`: **read its matching part before changing privacy, sync, devices, enforcement, focus sessions, sleep, storage or schema code**, and record new or changed rules there, not here.
- **Local only.** The only outbound connections: read-only iCal downloads and Google Calendar (ADR 9; ADR 78 adds one write path: opted-in tasks' title and times as events in the app's own "AB Reminders" calendar, scope calendar.app.created, and experimentally a calendar the user picks, scope calendar.events, `GoogleCalendarWriter`), Google Drive (scope drive.file, the app's own folder, sealed sync files and sealed settings backups only; ADR 26, 71), read-only Beeminder goal reads (ADR 43), the user's own sealed relay (ADR 46, sealed sync files only, HTTPS pinned or CA-checked), network time over SNTP (ADR 70, time.google.com then time.cloudflare.com), all in `CalendarNetwork`; update checks/downloads from `fabiantorrestech/AccountabiliBuddy-releases` (ADR 12, `VelopackUpdater`). Any other connection needs a new ADR.
- Window titles, full URLs, OCR text, reasons and notes never leave the device. Between the user's own devices only: sealed sync files (ADR 22, AES-256-GCM, one file per device; the Wi-Fi link is deleted, ADR 45) and the watch's limited snapshot through the phone (ADR 21). What each may carry is listed in `doc/hard-rules.md`.
- Core (`src/AccountabiliBuddy.Core`) has **no UI or OS dependencies**; Windows code goes in `Platform.Windows` behind a `Core/Ports` interface (ADR 4).
- Profiles/rules are a versioned JSON schema readable by Kotlin (ADR 7), currently **v13**; any shape change bumps it with a migration in `ProfileDocumentSerializer`.
- App-owned preferences and device-local state live in the `documents` table (`SqliteJsonDocumentStore`), never in the shared profile JSON; secrets and private text are DPAPI-encrypted (`IDataProtector`).
- Activity history (ADR 8): only times, app, domain, site section (ADR 17), classification and session context are readable; the rest is encrypted or not stored. Kept 90 days. Never download an icon.
- Detection cascade, cheapest first (rules, OCR only when still unknown); event-driven; keep CPU and battery low.
- **No escape hatches during focus.** Profiles, Day plan, Settings, Setup revokes, restore are locked during a session the user started. Ways out: end early (wait + reason), a profile's unlock option, or "All tasks done" (ADR 25). Step away never shortens a wait or unlocks anything. All time comes from `TrustedClock`.
- Stricter changes apply at once; looser ones wait (10 min default), never during a session, no "apply now".
- Focus time counts only sessions the user starts and ones added by hand in History (ADR 20); no scheduled sessions (ADR 18).
- Install and data safety (ADR 10): installed to `%LocalAppData%\AccountabiliBuddyApp`; data in `%LocalAppData%\AccountabiliBuddy` (dev builds: `AccountabiliBuddy-Dev`); DB backed up before migrations; a newer DB is refused.

## Commands
```
dotnet build AccountabiliBuddy.sln
dotnet run --project src/AccountabiliBuddy.App   # uses %LocalAppData%\AccountabiliBuddy-Dev (not your real data)
pwsh scripts/package.ps1 [-Version x.y.z] [-Publish]   # installer + portable zip in releases/; -Publish uploads them to the public releases repo (needs vpk 1.2.161 and gh auth login)
cd android && JAVA_HOME="C:/Program Files/Android/Android Studio/jbr" ./gradlew assembleDebug   # phone and watch APKs (app/ and wear/build/outputs/apk/debug)
cd android && JAVA_HOME="C:/Program Files/Android/Android Studio/jbr" ./gradlew assembleRelease   # small R8 build (~2.5 MB), signed with this PC's debug key so it installs over the debug build
cd android && JAVA_HOME="C:/Program Files/Android/Android Studio/jbr" ./gradlew :wear:assembleDebug   # the watch app (wear/build/outputs/apk/debug); same app id as the phone app
pwsh scripts/publish-apks.ps1 [-Tag vx.y.z]   # after a release build: phone + watch APKs onto that release (default the latest) in the public releases repo; never a release of their own (the desktop update feed needs releases.win.json on the latest)
```

## Roadmap
Done so far and what's next: `doc/roadmap.md`. Current run: `doc/plans/2026-10-26-feature-list-plan.md`. Plans for the rest: `doc/plans/2026-10-27-*` (relay clients, day plan modes, ideas to plan later). Waiting on the user: the Claude Design batch, Places.

## Conventions
- Explicit, readable code; SOLID; one public type per file; file-scoped namespaces; nullable enabled.
- No tests unless asked (Core is designed to be unit-testable).
- **Modes (ADR 68, 69):** for any feature or behaviour change that touches how the two modes work differently (enforcement, sessions, gates, settings per mode, guidance), ask the user first whether it applies to AccountabiliBuddy mode, ProductaBuddy mode, or both. Skip the question when the change works the same in both modes. In auto mode, don't ask: assume both modes, unless applying it to both doesn't seem logically right; then park that feature (log it as pending) until the user is back to decide.
- **Smallest PC window (owner, 2026-11-14):** every PC view must fit and work at the smallest window the owner uses: the installed app measured at **901 × 567 DIP client area** (1078 × 677 px window, 1060 × 667 px client, at 117.7% display scaling); the window's own minimum is 900 × 560 DIP (`MainWindow.axaml`). Nothing may overlap, clip a control or need sideways scrolling at that size; check it (e.g. the render harness) for every new or changed PC view.
- Times shown to the user are 12-hour with AM/PM (e.g. 4:05 PM) on the PC, the phone and the watch, whatever the system's 24-hour setting; never 24-hour.
- A new or reworked feature that needs setting up gets an entry in SetupFeatures (PC `src/AccountabiliBuddy.App/Services/Setup/SetupFeatures.cs`, phone `android/app/.../setup/SetupFeatures.kt`) with the version it arrives in, its "set up?" check and its assistant page, so existing installs can set it up from the migration assistant (5 taps on the About logo, on the PC and the phone; a quiet "New since your last version" card on Today after an upgrade, "Not now" until the next version).
- Icons come from the Material icon libraries (ADR 32): `Material.Icons.Avalonia` on the PC, `material-icons-extended` on the phone; the same meaning gets the same icon on both (the phone's titles map in `SectionIcons`).
- **Planning and big builds (owner, 2026-11-14):** plan multi-task or design work with the strongest model available (Fable or Opus), and have a second strong-model agent double-check the plan against the full spec before any implementer starts. Then run it with `/tiered-build` (default profile `standard`). For design work, every brief carries the spec's detail checklist for its screens, and UI tasks are checked against the mockups before commit.
