# Phase 6: self-hosted sealed relay — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** C 3.1, a standalone relay that stores the sealed sync files and pushes changes in about a second, plus "Own relay" as a sync method on the PC and the phone, and a guide for LAN + VPN, public internet and VPS. Then C 6.1 synced timers, with its own plan after this.

**Spec:** [ADR 46](../adr/0046-add-a-self-hosted-sealed-relay.md); feature-list plan "User answers" (relay form, reach).

## Global Constraints
- Implementer rules: `.superpowers/sdd/implementer-rules.md`.
- **Dependencies:** none beyond the .NET shared framework (`Microsoft.AspNetCore.App` via `<FrameworkReference>`; the user approved phase 6's dependencies). No NuGet packages, no Android libraries.
- The sealed format is unchanged (`SyncFileCodec`, "ABS1"). The relay never sees the group code or the key.
- Key derivation in both `Core/Sync/SyncKey.cs` and `shared/.../sync/SyncKey.kt`, the same HKDF as today:
  - info `"relay-group"` gives 16 bytes, hex, as the relay group id;
  - info `"relay-auth"` gives 32 bytes, base64url, as the bearer token.
- HTTPS only. A pinned self-signed certificate is checked by its SHA-256 fingerprint in the same base64url format as `LinkIdentity.Fingerprint`, so the PC and the phone agree. A CA-signed relay (behind a proxy) is checked normally with no pin.
- **Cross-phase:** phase 4b must keep and move the PC's fingerprint helper and the phone's `shared/.../link/PinnedTrustManager.kt`. Done in the 4b plan.

## Contract (all tasks)
- `PUT /v1/groups/{gid}/files/{name}`: body is the sealed bytes, at most 4 MiB. Returns `{ "etag": "<sha256 b64url>", "at": ISO }`.
- `GET /v1/groups/{gid}/files`: returns `[{ "name", "etag", "size", "at" }]`.
- `GET /v1/groups/{gid}/files/{name}`: returns the bytes, with an `ETag` header.
- `DELETE /v1/groups/{gid}/files/{name}`: used when leaving a group.
- `GET /v1/groups/{gid}/events`: an SSE stream of `event: changed` / `data: {"name","etag"}`, with a `:ping` comment every 25 s.
- Every call sends `Authorization: Bearer <token>`.
  - The first PUT for an unknown gid stores SHA-256(token).
  - Any later call with another token gets 403. Unknown gid with no store yet gets 404 on GET.
- `{name}` is the existing file name (12 hex + `.absync`).

---

### Task 1: The relay program
**Files:**
- Create `src/AccountabiliBuddy.Relay/` (net10.0, minimal API, `<FrameworkReference Include="Microsoft.AspNetCore.App" />`), added to the solution.
- Storage under `--data <dir>`: `<gid>/<name>`, an `owner.sha256` file, and files written atomically (temp then rename).

**Settings and startup:**
- `--urls` (default `https://0.0.0.0:8743`).
- `--proxy` (plain HTTP on 127.0.0.1, for a reverse proxy).
- First run without `--proxy`: create a self-signed ECDSA P-256 certificate (`CertificateRequest`, 10 years, SAN = the host's names and IPs), store it as `relay.pfx`, and print the fingerprint plus a ready "relay address + pin" line.

**Push:** an in-process broadcast per gid feeds the SSE.

**Limits:**
- 4 MiB per file.
- 64 files per group.
- 30-day age cleanup, run hourly.
- Simple per-IP rate limit: 120 requests a minute, with no NuGet.

**Logging:** times, gid prefix (6 characters) and status only. Never bodies or tokens.

**Deployment files:**
- `deploy/relay/Dockerfile` (official `mcr.microsoft.com/dotnet/aspnet:10.0` base, multi-stage).
- `docker-compose.yml`.
- `accountabilibuddy-relay.service` (systemd, `Type=simple`, a dedicated user).
- `install-windows-task.ps1` (Task Scheduler at startup, runs as a service account).
- `Caddyfile` example (reverse proxy, automatic Let's Encrypt).
- `scripts/publish-relay.ps1`: self-contained single-file builds for win-x64, linux-x64 and linux-arm64.

- [ ] Commit `Relay: a standalone sealed relay (stores and pushes sync files, can't read them)`.

### Task 2: PC "Own relay" sync method
**Files:**
- `Core/Sync/SyncKey.cs` (the relay id and token).
- `SyncDeviceCard.MethodRelay = "relay"`.
- `SyncSettings` (`RelayOn`, `RelayUrl`, `RelayPin?`).
- A new `Services/Sync/SyncRelayService.cs`, modelled on `SyncDriveService`: `Start`/`Restart`/`WriteNowAsync`/`ReadNowAsync`, unchanged-detection by etag, debounce, problem line, `RetryGoalAsync` after a read.
- The relay calls live in `CalendarNetwork`, which stays the one auditable class: a second `HttpClient` with a pinning handler when a pin is set; update its doc comment.
- An SSE listener that reconnects with backoff from 1 s to 60 s, plus a 5-minute fallback poll.
- `DevicesViewModel`/`DevicesView` (the switch, the address and pin fields, "Test connection", a relay Expander with the short steps).
- `App.axaml.cs` wiring.
- The join link adds `&r=<url>&p=<pin>` (`DevicesViewModel` ~336).
- Leaving the group calls DELETE.

**Guide:** a new "Own relay" part in `doc/guides/self-hosted-sync.md`:
- LAN + Tailscale/WireGuard with the pinned certificate;
- public internet through Caddy with Let's Encrypt, a DNS name and port 443;
- a VPS (any 1 GB Linux box, Docker or systemd, a firewall allowing only 443);
- updating;
- what the relay can and can't see.

Reword §1's "opens no connection" so it applies to the folder method only.

- [ ] Commit `PC: Own relay sync method (push in about a second), join link carries it, guide`.

### Task 3: Phone "Own relay" sync method
**Files:**
- `shared/.../sync/SyncKey.kt`.
- A new `sync/RelaySyncSource.kt`, modelled on `DriveSyncSource`: `loop(busy)`, `syncNow`, `forget`, status.
- HTTP via `HttpsURLConnection` with `PinnedTrustManager` when pinned; SSE read with a long read timeout.
- `PhoneSyncSettings` (`relayOn`, `relayUrl`, `relayPin`).
- `SyncCoordinator.acceptSealed` gets a relay branch, and `filesForFolder(cameBy = METHOD_RELAY)` carries the files.
- `CompanionApp` wiring:
  - The SSE stays open only while `driveBusy()` is true; otherwise poll on `DrivePoll` timing.
  - A phone action uploads at once.
- `DevicesScreen` gets a Relay section with a hint for the steps.
- `JoinLink` parses `r`/`p`, and `confirmJoin` turns the relay on.
- `network_security_config` stays cleartext-banned; update its ADR comment.

- [ ] Commit `Phone: Own relay sync method; joining by QR sets it up`.

## Review Focus
1. Anything readable leaving a device: a token or the code in logs, the relay storing plaintext, the URL query carrying secrets.
2. A pin mismatch or certificate change silently falling back to no pinning. It must fail closed and show a problem.
3. Push storms: SSE echoes of a device's own write, causing a write loop.
4. The 403 owner rule locking out a legitimate group after a code change or leave/rejoin. Rejoin with the same code gives the same token; a new group gives a new gid.
5. Path traversal or oversized bodies on the relay, and `{gid}`/`{name}` validated against strict patterns.

## Execution
- After phase 4b.
- Task 1 first, so its API is real. Then Tasks 2 and 3 in parallel; the Contract above is their interface.
- Implementers: Sonnet high. Reviewers: Opus medium (network, privacy, sync).
- The orchestrator records the relay in `doc/hard-rules.md` (outbound connections, the stored `sync.settings` fields) and in `CLAUDE.md`'s "Local only" bullet after merge.
- C 6.1 synced timers gets its own plan once the relay is merged.
