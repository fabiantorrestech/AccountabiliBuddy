# Linked devices v1: plan

Status: **approved 2026-10-20, in progress** (L5 first). Decisions: [ADR 14](../adr/0014-pair-the-phone-with-the-pc-over-the-local-network.md) (link), [ADR 15](../adr/0015-share-activity-history-between-paired-devices.md) (shared history), [ADR 16](../adr/0016-read-browser-addresses-on-android-with-an-accessibility-service.md) (phone sites). The bigger picture (several PCs, home server, Bluetooth) stays in [device-sync.md](device-sync.md); this is its first, smaller step.

## Why

Switching devices is the easiest way out of a focus session: the PC is locked down, so the phone (or a tablet) takes over. The goal is **awareness, not blocking**: seeing it as it happens and afterwards changes the habit. Curbox already blocks apps and sites on the phone, and Cold Turkey blocks on the PC; this app shows what's going on across all of them. Necessary phone use (2FA codes, texts, calls) must stay easy and never count against you.

## Decisions (user, 2026-10-20)

| Topic | Decision |
|---|---|
| QR code on the PC | QRCoder library (MIT) |
| Pairing | The phone's own camera scans the QR; the link opens the companion (no camera code in the app); values can be typed in |
| Remote | The phone can view, start, and end early a PC session (the PC's own wait and reason) |
| Sync | Profiles, sleep, day bar and events come from the PC; backup import stays as a fallback |
| Usage | Every linked device's usage is visible on the PC and on the phone: per device and combined |
| Shared fields | Times, app, website domain, on/off-track label (as History stores them). Window titles, full addresses, OCR text and typed reasons never leave their device |
| Pickup during focus | A silent check-in card with one-tap answers plus a typed option; must never get in the way of calls or emergency features |
| Phone sites | In this version, through an opt-in Accessibility reader (read only, browsers only) |
| Cold Turkey | Show which blocks are on; a profile can list the blocks it expects, with a warning at session start when they're off |

## Shape

- **Hub:** one Windows PC. Phones and tablets pair with it; data between a phone and a tablet goes through the PC. Several PCs come later (device-sync S2).
- **Off until turned on** in the PC's Setup tab ("Linked devices"). The PC listens on one TCP port, answers only private-network addresses, and Windows Firewall asks once (allow on private networks).
- **Wire:** TLS to the PC's own certificate, pinned by its fingerprint from the QR code; on top, length-prefixed JSON messages both ways, so the PC can push a session change the moment it happens. No HTTP server and no protocol library on either side (.NET `SslStream`, Android `SSLSocket`).
- **Trust:** the QR's one-time code (5 minutes, 5 tries) is traded for a random device token. The PC keeps a hash of it per device; the phone keeps it encrypted with the Android Keystore. Unpair on either side revokes it; on the PC, unpairing is a Revoke, so it's locked during focus.
- **When the phone connects:** while its screen is on or the app is open; it closes the connection when the screen turns off. Nothing runs while the phone sleeps (battery). If the PC's address changed, the phone asks the local network once ("where is PC <id>?", one UDP broadcast) and the PC answers; no beacons.
- **Messages (v1):** `hello`, `state` (session, phase end, profile, today's plan), `settings` (profiles document, sleep, day bar, events; sent when their version changed), `start`, `endEarly` (the PC answers with its wait; the reason is typed on the phone and checked by the PC), `usage` (segments since the last sync), `presence` (screen on/off, app in front during focus), `status` (Cold Turkey from the PC, Curbox from the phone).

## What each device shows

| Where | What |
|---|---|
| Phone, pinned card | During a PC session: "In focus on PC: Coding until 12:00", with phone time this session |
| Phone, Today | A "PC" card: session, start, end early; the day bar with a lane per device |
| Phone and PC, History | This device, each linked device, or all combined; today and the last 7 days; device switches per focus hour |
| PC, on-screen timeline and day bar | A thin lane per linked device, filled while it's in use during focus |
| PC, Session tab | "Phone this session: 3 pickups, 6 min (2 min essential)"; Cold Turkey blocks on/off |
| Both, at session end | A summary: focus held, pickups, minutes per device, top apps and sites |

## Awareness on the phone (during a PC focus session)

1. **Essentials never count:** Phone, Messages, authenticators, the bank's app, Clock (a default list, editable in the app). Time in them shows as "essential".
2. **Check-in on unlock:** a silent card peeks: "In focus on PC. What's this for?" with **2FA code**, **Text or call**, and **Something else…** (type a few words). Opening an essential app answers it by itself, and the card goes away.
3. **Nudges:** after 2, 5 and 10 minutes in a non-essential app (or a site the profile blocks), a silent card: "4 min in Chrome during focus. Back to Coding?"
4. **Switch count:** each move from the PC to the phone during focus (non-essential) counts as a switch.
5. **Away from the PC's network:** the phone knows scheduled blocks from the synced schedule, so check-ins and nudges still work for them; a session started by hand on the PC shows only once the phone has reached the PC.
6. **The typed check-in text** stays on the phone (encrypted, like reasons on the PC); only the chosen answer is shared.

**Emergency and calls stay untouched:**
- Only notifications are used: no overlays, no full-screen screens, nothing drawn over other apps.
- The Accessibility reader never filters keys or gestures, and only listens to browsers. It never sees the phone, emergency or system screens.
- No check-in while a call is going on.
- Emergency SOS (pressing power 5 times), the power-menu Emergency button and emergency calls from the lock screen work without unlocking, so they never trigger a check-in at all.
- **Tested on the emulator only**, whose phone line is simulated (an emergency number dials the emulator's fake modem, nothing real). If the emulator image doesn't include Emergency SOS, that part is covered by the rules above, and this plan will say so.

## Phone sites (opt-in)

- An Accessibility service limited to known browsers (Chrome, Brave, Firefox, Edge, Samsung Internet, DuckDuckGo, Opera). It reads only the address bar and keeps only the **domain**.
- Domains are matched against the session profile's URL rules (already in the profile document), so Amazon shows as off-track on the phone too.
- Turned on in Settings with an explanation; it runs alongside Curbox's own Accessibility service.

## Cold Turkey (PC)

- Its settings file is copied and read when it changes (checked once a minute; the existing import reader decodes it).
- A block counts as **on** when it's switched on and, for scheduled blocks, when now is inside its schedule.
- Per profile (Profiles tab, saved on this PC only): "Cold Turkey blocks this profile expects".
- At session start, and once a minute during it, a silent notice names expected blocks that are off.
- Shown in the Session tab and on the phone's PC card.

## Storage

- **PC:**
  - `activity_segments` gains a `device_id` column; null means this PC. It's a DB migration, with the usual backup first.
  - Paired devices go in the documents table ("devices.paired": name, kind, token hash, last seen).
  - The certificate goes in "devices.identity", DPAPI-encrypted.
  - Expected Cold Turkey blocks go in "coldturkey.expected".
- **Phone:**
  - usage segments and check-ins go in an SQLite database from Android itself (no library), kept 90 days;
  - the token is encrypted with the Keystore;
  - the essentials list is a JSON file.

## Milestones

| # | Milestone | Done when |
|---|---|---|
| L5 | Cold Turkey status and expected blocks (PC only; small, useful now) | Session tab lists the user's 5 blocks as on/off; an expected block that's off is named at session start |
| L1 | Pairing and remote | The emulator pairs from the QR link; a session started on the PC shows on the phone within 3 s while the app is open; starting from the phone works; ending early from the phone waits the PC's time and needs the reason; a wrong token is refused; unpair is locked during focus |
| L2 | Settings sync | A profile edited on the PC is on the phone at its next connect; backup import still works |
| L3 | Shared usage and combined History | The phone's usage appears in the PC's History under the phone and under All, and the PC's on the phone; lanes per device on both day bars |
| L4 | Awareness | Check-in on unlock during PC focus, essentials answer it, nudges at 2/5/10 min, switch count, session summary on both. Emergency and call checks pass on the emulator |
| L6 | Phone sites | With the reader on, amazon.com in Chrome shows as off-track during focus; the reader sees no other apps |

Order: L5 first (quick, PC only), then L1 → L4, then L6.

Progress: **L1 built 2026-10-20** (Core `Link/` 22/22 loopback checks; Setup → Linked devices with QR code; Android pairing, PC card, start, end early, unpair verified emulator ↔ test server. Still to try: the real Pixel over Wi-Fi with the camera scanning the QR code, and the Windows Firewall prompt). **L5 built 2026-10-20** (block states parsed from Cold Turkey's settings copy, Session tab card, per-profile expected blocks, notice at focus start / when one turns off; checks pass on the user's real file and made-up "on" cases; screens not seen yet, needs an install).

## L1 build notes

- **Core `Link/`** (plain .NET, no OS code, so Linux can reuse it):
  - framing: a 4-byte length, then UTF-8 JSON, at most 1 MB;
  - message records: `{type, body}` envelopes;
  - pairing codes and token hashes;
  - `LinkServer`: TLS over `SslStream`, answers private addresses only, pairing, auth, push;
  - it talks to the app through `ILinkSessionHost` (state, start, end-early terms, end), so a test harness can host it without the whole app.
- **Certificate:** ECDSA P-256, self-signed, created once. Stored as PFX in documents "devices.identity", DPAPI-protected through `IDataProtector`. Its fingerprint is SHA-256 of the DER.
- **Port:** a fixed 47821, so the firewall rule stays put.
- **Ending early from the phone:**
  1. The phone asks.
  2. The PC answers with its wait and minimum reason length (`SessionController.BypassSettings`) and notes the time.
  3. After the wait, the phone sends the reason.
  4. The PC checks both against its own clock and ends the session with that reason, the same way as from its window.
- **Starting from the phone:** a profile and a length, or count-up (the Quick Start profile). Refused while a session runs.
- **Setup tab, "Linked devices":**
  - an on/off switch (documents "devices.settings");
  - "Pair a phone", which shows the QR code (QRCoder 1.8.0), the code and its countdown;
  - the paired devices with when each was last seen;
  - Unpair, locked during focus.
- **Android:**
  - INTERNET permission, and a network security config with no cleartext;
  - the deep link `accountabilibuddy://pair`;
  - `link/`: link parsing, a pinned `SSLSocket` client, framing, the paired-PC store with the token sealed by an Android Keystore AES-GCM key;
  - it connects while the app is open, for now;
  - a PC card on Today (status, Start, End early with the countdown and reason);
  - a PC section in Settings (paired PC, Unpair).
- **Testing:** the emulator reaches the PC at 10.0.2.2, which arrives as loopback (allowed for testing). A harness hosts `LinkServer` with a fake session host, so the installed app is never disturbed. The Pixel 7 is tried at the end, after Windows Firewall has asked.

## After L4: fill Cold Turkey and Curbox lists from AB (decided 2026-10-20)

- **Idea (user):** from a suggestion or a History row, pick a profile and one button adds the site or app to that profile and to the matching blocker list: a Cold Turkey block "A.B. - <profile>" on the PC, a Curbox group on the phone. Removing works the same way.
- **Lists only:** AB never switches Cold Turkey or Curbox blocks on or off with sessions.
- **Cold Turkey:**
  - its own command line adds a site to an existing block (`-add "<block>" -web "<site>"`, `-exception` for allowed sites);
  - it has no command to remove a site, add an app or create a block: you create "A.B. - <profile>" once, and AB lists removals and apps for you to change in Cold Turkey;
  - editing its settings file was tested on 2026-10-20 with a test block, and it doesn't work: Cold Turkey rewrote the file from its own copy the moment its window opened, so the test block vanished; the user's blocks were unchanged; backup in `%LOCALAPPDATA%\AccountabiliBuddy-ColdTurkeyBackup`;
  - its command line may time out while its window is open.
- **Curbox:** its API (v2) only starts or stops focus and switches groups. A PR to the user's fork (`../curbox-android`) adds commands to add or remove apps, websites and keywords in a group, and to list a group's items; then upstream.
- Needs an ADR (AB controlling other apps' lists) when it starts.

## Not in v1

- several PCs linked together;
- a home server;
- Bluetooth;
- blocking on the phone (Curbox does that);
- Android app rules in the profile document (a schema change; essentials and domains cover v1).
