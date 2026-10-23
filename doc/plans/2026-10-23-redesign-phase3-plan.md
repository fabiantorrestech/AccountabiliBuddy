# Redesign phase 3: Drive-only sync

> **Status:** approved by the user 2026-10-23 (option 1: full plan, play-services-auth on the phone). ADR 26 Accepted. Deleting the Wi-Fi link (Task 9) still needs the user's explicit OK at the time.

**Goal:** the user's devices sync through Google Drive (ADR 26); the Wi-Fi link goes; the watch is fed only by the phone; the phone can tick tasks, park thoughts and tick come-backs through the sync files; phone pickups and pickups across PCs are counted (design G, K); lights out on the personal PC (K).

**Spec:** `doc/plans/2026-10-22-redesign-design.md` sections H, I (watch fed by the phone), K (lights out, pickups), G (phone pickups); reviewer's questions 1, 2, 3, 7, 8.

## Before any code (the user)

1. **Google Cloud project:** enable the Drive API; add the `drive.file` scope to the OAuth consent screen; move the app out of "Testing" (needs a privacy policy URL and a short verification for a sensitive scope; `drive.file` is non-sensitive, so it should pass without a security review).
2. **Android OAuth client:** create one in the same project with the phone app's package name and the SHA-1 of the signing key (debug key today; the release key later).
3. **New dependency on the phone:** Google sign-in for Android (`androidx.credentials` + `com.google.android.libraries.identity.googleid`, or Play services auth). Your rules need your OK for a new library.
4. **Removing the link:** OK to delete the Wi-Fi link code (PC `Core/Link/LinkServer` and its host, discovery, pairing; phone `link/`) once Drive sync has worked for a week? (Suggested: keep it behind a hidden switch for a week, then delete.)
5. **ADR 26:** accept as written, or change.

## Tasks (once the above is done)

1. **PC Drive client** (`CalendarNetwork` gets the Drive calls; scopes united; one app folder; list/read/write by opaque file names). Checks: a fake Drive in syncapp.
2. **PC Drive sync method** (`SyncSettings.DriveOn`; poll 20 s in a session, 5 min idle; reuse `SyncCoordinator` write/read). Settings → Devices.
3. **Phone sign-in + Drive client** (one auditable class); **phone Drive method** (30 s while a session runs or the notification is pinned, else 15 min, and on the watch's /hello).
4. **Sync-file fields** (optional): calendar events, the running session's planned end and breaks, pickups, the action list with ids and the PC's acknowledgements in `Seen`. Cross-language checks.
5. **Phone actions:** tick a task, park a thought (→ come-back), tick a come-back; the watch's two buttons (tick, park) go through the phone.
6. **Pickups:** the phone counts unlocks during a PC session (Usage access), shown on the review card and History; input on the personal PC within a minute of the work PC's counts as a pickup (K).
7. **Lights out** (opt-in, per computer, K): lock or sleep the personal PC at each work part's start and after 3 idle minutes during work hours; off when the goal is met.
8. **Watch fed only by the phone:** drop the watch's PC route; the Link page becomes a freshness dot; a Tile refresh on every snapshot.
9. **Retire the link** (after the user's OK and a week of Drive sync): notice on paired devices; delete the server, pairing, discovery and trust notices; ADR 14 superseded.

Ship after Tasks 1-3 (Drive sync works next to the link), then 4-8, then 9.
