# Device sync (V3): plan

Status: **draft for later**, not started. Decisions here become ADRs when we begin (see "ADRs needed").

## Goal

Every device you own runs AccountabiliBuddy and knows about every session, so switching devices is not a way out.

- Start a session on Windows → the phone is in that session within seconds, and vice versa.
- End early on any device (with that device's friction) → it ends everywhere.
- Each device enforces with its own blocks (Windows: windows, tabs; Android: apps, sites).
- A device that goes quiet during focus is noticed and logged.

## What syncs

| Data | Synced? | Notes |
|---|---|---|
| Session state (start, end, phase times, profile) | Yes, real time | Absolute timestamps, so every device computes Pomodoro phases itself, with no drift |
| Unlocks (countdown, relock) | Yes | One unlock lifts the blocks on every device; cancel or "Lock again" anywhere |
| Profiles, rules, schedule, off days (ADR 7 JSON) | Yes | Last writer wins per profile; edits stay locked during focus on every device |
| Device presence (heartbeat) | Yes | For "phone was unreachable 14:00–14:40 during focus" |
| Activity history | Open question | Default: each device keeps its own. Optionally share daily summaries (readable fields only, ADR 8) |
| Titles, URLs, OCR text, reasons | **Never** | DPAPI-encrypted per device (ADR 8); they don't leave it |

Scheduled blocks also run on each device from its own copy of the schedule, so they work with no connection at all. That's the strongest offline guarantee.

## Topology and fallback order

```
             ┌────────────── Home server (NAS, Docker) ──────────────┐
             │  relay + mailbox, stores only encrypted events        │
             └──────▲───────────────────▲───────────────────▲────────┘
                    │ 1. preferred      │                   │
               Windows PC ◄──2. LAN──► Android phone ◄──► (Linux later)
                    ▲    (incl. USB tether / hotspot)       ▲
                    └──────────3. Bluetooth─────────────────┘
                         4. offline: queue, enforce locally
```

| Order | Path | When | Notes |
|---|---|---|---|
| 1 | Home server | Whenever reachable | Always-on store-and-forward: a device that was off catches up from its mailbox. Reachable away from home only through your own VPN (e.g. Tailscale or WireGuard), never a public cloud |
| 2 | Direct LAN | Server down, same network | Discovery by UDP broadcast beacon (no extra library). **USB tethering and phone hotspot are LAN too**: they create a network interface, so no extra transport is needed |
| 3 | Bluetooth | No network path | Small messages only (session and unlock events). Windows RFCOMM or BLE GATT via WinRT, Android `BluetoothSocket` |
| 4 | Offline | Nothing reachable | Queue outgoing events; keep enforcing the last known session and the local schedule; merge on reconnect |

Devices send on every available path at once. Event ids make duplicates harmless.

## Sync model

- **Event log, not state copies.** Each change is an event: `SessionStarted`, `SessionEnded(early, reasonHash)`, `UnlockRequested`, `UnlockCancelled`, `Relocked`, `ProfileChanged`, `Heartbeat`, and so on. Every event carries:
  - an id
  - the device id
  - a hybrid logical clock (HLC) timestamp
  - a signature
- **State = fold of events**, computed by a portable `SyncEngine` in Core (ADR 4 boundary; no OS code).
- **Sessions have ids.** "End" names the session it ends, so ending an old session offline can't end a newer one started elsewhere.
- **Conflict rules (stricter wins on ties):**
  - Two concurrent starts: the one ending later wins.
  - End vs. extend at the same HLC instant: extend wins.
  - Config edits: last writer by HLC.
- **Friction stays local.** The device where you end early shows its own wait and reason; the other devices only receive the result.
- Existing seams: `ISyncTransport`, `PeerDevice` and `SyncPayload` in `Core/Ports` become the per-path adapters (server, LAN, Bluetooth).

## Pairing and security

- Each device has an identity key pair (Ed25519 signing, X25519 encryption), stored in DPAPI on Windows and the Android Keystore on the phone.
- **Pairing:** the PC shows a QR code (public key, one-time code, LAN address); the phone scans it, and both confirm a 6-digit code. The server is paired the same way and learns only the public keys.
- **End-to-end encryption:** events are encrypted to the device group, so the server relays and stores blobs it can't read.
- **Setup tab → "Devices":** shows paired devices, last seen and path; Unpair counts as a Revoke, so it's locked during focus.
- Uses only built-in crypto (.NET `ECDiffieHellman` / `AesGcm`; Android JCA). Ed25519 may need a library on .NET, which would require your OK.

## Android companion (sketch)

| Need | Android API | Risk |
|---|---|---|
| Foreground app | `UsageStatsManager` (usage access) | Low |
| Browser URL / YouTube channel | `AccessibilityService` | **Play Store restricts accessibility use** → sideload / F-Droid, or a VPN-DNS fallback for domains only |
| Block | Overlay (`SYSTEM_ALERT_WINDOW`) + "go home" via accessibility | Medium |
| Domain blocking | Local `VpnService` DNS filter (on device, no remote server) | Medium; conflicts with a real VPN |
| Tamper resistance | Device admin "prevent uninstall during focus" | Policy-dependent |
| Rules | Same JSON (ADR 7) + rules scoped per platform: `ProcessName` (Windows) vs `AppId` (Android package); URL, Channel and Domain shared | Schema v3 |

## Calendar (deferred from P1)

Read-only calendar view for planning off days. It needs network access to Google, so it belongs here:
- **Option A:** the home server fetches your private iCal link and relays events like any other sync data. The desktop app still never talks to the internet.
- **Option B:** Google OAuth in the app (an ADR 2 amendment, plus a Google Cloud project).

## ADRs needed when we start

1. **Supersede/amend ADR 2:** data may leave a device only to your own paired devices and self-hosted server, end-to-end encrypted, and never to third-party services. This also rewrites the CLAUDE.md "Local only" hard rule.
2. Sync model: signed event log + HLC + stricter-wins merge.
3. Transport order: server → LAN → Bluetooth → offline; UDP beacons for discovery.
4. Home server: technology and packaging (ASP.NET Core minimal API in Docker is the default candidate; it's part of .NET, not a NuGet package).
5. Profile schema v3: platform-scoped app rules.

## Milestones

| # | Milestone | Done when |
|---|---|---|
| S0 | ADRs above | Accepted |
| S1 | Core `SyncEngine` + event model + in-memory transport | Headless checks: concurrent start/end, offline queue merge, duplicates ignored, unlock across devices |
| S2 | Windows ↔ Windows over LAN, pairing, Setup "Devices" | Session started on PC A appears on PC B in < 3 s; unpair locked during focus |
| S3 | Home server (Docker) + server-first with LAN fallback | Server down → LAN keeps syncing; device off for an hour catches up from its mailbox |
| S4 | Android companion MVP | Pair by QR; session start/end both ways; app blocking; friction on phone |
| S5 | Bluetooth fallback | No Wi-Fi: session start reaches the phone over Bluetooth |
| S6 | Accountability | History shows "phone unreachable during focus"; optional shared daily summaries |
| S7 | Calendar view | Option A or B chosen and built |

## Open questions

1. What should the phone block first: apps only, or browser sites and YouTube channels too (needs accessibility, so no Play Store)?
2. Share activity summaries across devices, or keep history per device?
3. Which NAS (Synology, TrueNAS, Unraid, …), and does it run Docker?
4. Sync away from home through your own VPN, or home network only?
5. Should ending early on the phone use the same friction settings as Windows (shared), or its own?
