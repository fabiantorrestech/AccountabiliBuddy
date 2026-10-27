# 46. Add a self-hosted sealed relay

Date: 2026-10-27

## Status

Accepted

Amends [26. Sync through Google Drive only](0026-sync-through-google-drive-only.md) (a second network sync method next to Drive) and [45. Delete the Wi-Fi link](0045-delete-the-wi-fi-link.md) (it brings back a fast path that needs no Google account).

## Context

With the Wi-Fi link deleted (ADR 45), every device syncs through sealed files (ADR 22) carried by Google Drive (ADR 26) or by a folder another tool keeps in step. Drive is polled, so a phone action reaches the PC in seconds to minutes, and without a Google account there is no network path at all. The user wants a self-hosted option (C 3) and chose, on 2026-10-27, a small standalone relay program in addition to the existing Syncthing/Nextcloud guide. They want it reachable three ways: on the home network plus their own VPN, from the public internet with a real certificate, and on a rented VPS.

The hard rules allow outbound connections only to the listed services, and any other connection needs an ADR. The sealed file format must not change, and the relay must never be able to read what it stores. New dependencies need the user's OK, which they gave for this phase.

## Decision

We will add a relay server, `AccountabiliBuddy.Relay`, a small ASP.NET Core program built on the shared framework that ships with .NET (no NuGet packages). It stores each device's sealed sync file as opaque bytes and tells connected devices within about a second when a file changes. It runs as a plain process: under Windows Task Scheduler, a Linux systemd unit, or a Docker container. The apps get a third sync method, "Own relay", next to Drive and the folder: off until the user turns it on with the relay's address.

It works per group, under a relay group id derived from the sync group's code with HKDF (info "relay-group"). A device proves it belongs by sending a bearer token derived the same way (info "relay-auth"). The relay keeps only a hash of the first token it sees for a group and refuses others. Neither value can be turned back into the sealing key, and the code itself never reaches the relay.

The API holds four operations and a push stream under `/v1/groups/{id}/`: list files, get one, put one's own, and delete one's own when leaving a group. The push stream is server-sent events. Files over 4 MiB are refused, and files untouched for 30 days are deleted.

Transport is HTTPS only, in one of two ways. Either the relay makes a self-signed certificate on first run and the apps pin its SHA-256 fingerprint (LAN, VPN or Tailscale), or it listens on localhost behind a reverse proxy with a real certificate (public internet, VPS), where the apps check the certificate normally. The join link and QR carry the relay's address and pin next to the group code, so a second device joins the relay by scanning. The phone keeps the push stream open only while it is busy, which is the same rule as its 30 s Drive poll; otherwise it polls.

## Consequences

Devices on a relay see each other's changes in about a second, with no Google account, at home or anywhere the relay is reachable. Control from the phone becomes near real time again without the link's pairing and listening port on the PC. The sealed format is unchanged, so Drive, the folder and the relay can run side by side and carry each other's files.

The user now runs and secures a server. A public relay is an attack surface: the token gate and the size and age limits bound abuse, but it isn't hardened like a hosted service. The relay sees file names, sizes, times and the client addresses, though never the contents. Trust-on-first-use means whoever first writes a group id owns it on that relay; since the id comes from the secret code, only a group member can claim it. The pinning helpers from the deleted link (the fingerprint check on the PC, `PinnedTrustManager` on the phone) must survive phase 4b. A self-signed relay's certificate can't be rotated without re-joining or re-pinning every device. The phone's push only works while it is busy, because of Android's background limits.
