# Android companion: plan

Status: **in progress**: A, B and C working on the emulator; D partly (phone use, Curbox status); E designed (ADR 14, Proposed). Stack: [ADR 13](../adr/0013-build-the-android-companion-with-kotlin-and-jetpack-compose.md). Code: `android/`.

## Decisions so far (from the user, 2026-10-20)

| Topic | Decision |
|---|---|
| Order | A foundation → B timeline + sleep → C widget + notification → D phone tracking + Curbox → E PC remote |
| Profiles on the phone | Import the PC's backup file (`.abbackup`: readable, or password-protected); real sync comes with E |
| Where | `android/` in this repo |
| Device | Pixel, Android 15+ (target API 36, min API 26); emulator `Pixel_9` on the dev PC |
| Notification | Always on, silent, pinned at the top (like MyCalendarWidget's), showing today's timeline |
| Curbox | Import its block lists; run alongside it without fighting over Accessibility. Its "block part of a site/app" features (Shorts, Reels) are a **desktop** backlog item; Android blocking is discussed later |
| PC remote | Pair by scanning a QR code the PC shows (same Wi-Fi, end-to-end encrypted, no internet); needs its own ADR |

## Milestones

### A. Foundation
- Gradle project (version catalog, pinned versions ≥ 2 weeks old).
- Kotlin model of the profile document v4 (ADR 7): profiles, rules, weekly blocks, time off, one-off blocks, lock settings; v2/v3 migration (off days → time off) like `ProfileDocumentSerializer`.
- Import: open a `.abbackup` (readable or password: PBKDF2-SHA256 600k + AES-GCM, associated data `accountabilibuddy-backup/1`) or a bare profile JSON; keep it in app storage. Also picks up sleep and day-bar settings and in-app calendar events when the backup has them.
- Kotlin ports of `DayTimeline` (planned windows, time off subtracted, fit to day), `SleepSchedule` and `ProfileColors` (same colour per profile id as Windows).

### B. Timeline + sleep
- Today screen: the day bar (planned focus per profile, events, the red now line, night shading, lost sleep), headline ("in focus until…", "next: … at …"), sleep line (green ≥ 7 h, yellow ≥ 5 h, red) from two hours before bedtime.
- Sleep settings on the phone (bedtime/wake, weeknights/weekends), seeded from the backup.
- Health Connect: read last night's sleep sessions (on-device, permission asked) and show actual sleep next to the plan.
- Bedtime reminder two hours before bedtime (silent notification), like Windows.

### C. Widget + notification
- Glance home-screen widget: today's bar and the next block; refreshes every 15 minutes and on changes.
- Ongoing, silent notification pinned at the top with the bar drawn as an image, the headline and the sleep line; updated every few minutes and when the screen turns on. Can be turned off.

### D. Phone tracking + Curbox
- Usage access (UsageStatsManager): today's apps and time, shown like the PC's History.
- Accessibility service (opt-in, explained first) for the app in front and the address in browsers, matched against the imported profiles: drift is *tracked* during focus blocks. Blocking is decided later.
- Curbox: import its exported block lists into a phone profile; detect it and keep working alongside it.

### E. PC remote → linked devices

Superseded by **[linked-devices.md](linked-devices.md)** (2026-10-20): pairing, remote, settings sync, shared usage, awareness during focus, phone sites, Cold Turkey status. The original remote sketch below is kept for history.

- **Pairing:** PC Setup tab → "Pair a phone" shows a QR code with an `accountabilibuddy://pair?h=<ip>&p=<port>&f=<cert sha-256>&c=<one-time code>` link. The Pixel camera opens it in the companion (no camera code in the app); the values can also be typed in.
- **Transport:** TLS to the PC with its self-signed certificate pinned by fingerprint; private-network addresses only; off by default.
- **Trust:** the one-time code (a few minutes, rate-limited) is traded for a random device token. The PC keeps a token per device (DPAPI); the phone keeps it with the Android Keystore. Unpair on either side revokes it.
- **API (JSON):** `GET /v1/today` (session state, phase end, profile; planned and actual focus for the day bar), `POST /v1/session/start {profileId, minutes?}`, `POST /v1/session/end-early {reason}`. The PC applies its own wait and reason rules, and the phone shows the countdown. Later: `GET /v1/profiles` so the phone stays in sync without importing backups.
- **Phone:** gains INTERNET, limited by a network security config to paired PCs; a "PC" card on Today; start and end from the phone.
- **Open questions for the user:** (1) a QR encoder library on Windows (e.g. QRCoder, MIT) or a hand-written one; (2) is the camera-opens-a-link flow fine on your Pixel; (3) should ending a PC session from the phone be allowed at all, or view and start only?

## Backlog noted on the way
- Desktop: Shorts/Reels detection and blocking parts of sites or apps (from Curbox).
- Windows backups should include sleep and day-bar settings so the phone gets them (done in A).
- Curbox import: its API (v2) lists groups with counts only, and its backup zip holds shared prefs, not the DataStore where app groups and keywords live. A real import needs Curbox's `list()` to return the items (apps, websites, keywords), e.g. an opt-in `list(kind + ":ITEMS")` in an API v3; a small upstream PR, like the grayscale fix. Keyword exports (.txt, one per line) could be imported meanwhile.
- Phone rules: the PC's rules match Windows process names and URLs; the phone needs its own targets (Android package, and browser address via Accessibility) before it can tell distraction from work. Until then phone use is measured against planned focus time only.
