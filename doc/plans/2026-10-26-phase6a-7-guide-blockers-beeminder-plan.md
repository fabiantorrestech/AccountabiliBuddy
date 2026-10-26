# Phase 6a + 7: self-hosting guide, other blockers, Beeminder (read only) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** C 3.2 (a self-hosting guide in both apps for existing tools, Syncthing and Nextcloud, on Windows/macOS/Linux, using the folder method the apps already have), C 7 (detect other blockers, status only) and C 11 (Beeminder goals, read only). The sealed relay program (C 3.1) waits for the user's OK on its new framework/dependencies and its ADR; synced timers (C 6.1) wait for the relay.

**Spec:** `doc/plans/2026-10-26-feature-list-plan.md` (decisions C 3, C 7, C 11).

## Global Constraints
- Implementer rules: `.superpowers/sdd/implementer-rules.md`. No new dependencies (HttpClient on the PC, `HttpURLConnection` on the phone).
- Hard rule: "Any other connection needs a new ADR" — Beeminder gets ADR 43 (written by the controller before Task 3). All PC outbound calls stay in `CalendarNetwork` (the one auditable class).
- Times 12-hour. Material icons.

## Tasks

### Task 1: Self-hosting guide (C 3.2)
**Files:** `doc/guides/self-hosted-sync.md` (new; the full guide), the PC's Devices tab (a "Sync with your own server" expander next to the sync-folder setting with the short steps and a link to open the guide file), the phone's Devices → Sync folder (backup) section (the same short steps).
**Content:** what the folder method is (sealed files only; the server never sees contents); **Syncthing** (Windows: SyncTrayzor or Syncthing + autostart; macOS: Syncthing app; Linux: package + systemd user service; Android: Syncthing-Fork, then pick its folder in the phone's folder picker); **Nextcloud** (desktop client on Windows/macOS/Linux syncing a folder; Android: the Nextcloud app's folder sync or Autosync for Nextcloud); using an always-on computer as the server (Syncthing "introducer"/always-on device, power settings so it doesn't sleep); reaching it from outside home **safely**: prefer Syncthing's own relays/NAT traversal (no ports opened) or a mesh VPN such as Tailscale/WireGuard; never expose a file share directly to the internet; a small VPS option (Syncthing or Nextcloud on a 1 GB VPS, firewall, updates, backups); what to check when files don't arrive. No product endorsements beyond naming; no affiliate links.
- [ ] Commit `Docs + both apps: a guide to syncing through your own server (Syncthing, Nextcloud)`.

### Task 2: Detect other blockers (C 7)
**Files:** PC `src/AccountabiliBuddy.Platform.Windows/Integrations/` (a detector behind a Core/Ports interface: installed programs from the uninstall registry keys (HKLM/HKCU, 64/32-bit) and/or known install folders for **Freedom** and **PluckEye**; no process scanning loops), the Integrations tab (a "Other blockers" card: "Detected: Freedom — not controlled by AccountabiliBuddy"), the setup assistant's import page mention; phone `android/app` (detect **AppBlock** (`cz.mobilesoft.appblock`), **StayFocused** (`com.stayfocused`), **Freedom** (`to.freedom.android2`) via `PackageManager` with `<queries>` entries in the manifest; verify package names from the Play Store listings in your knowledge and say which you're unsure of), shown in the phone's Settings → Permissions & setup (or Integrations-like section).
- Status only: nothing else changes.
- [ ] Commit `Other blockers: detect Freedom, PluckEye, AppBlock, StayFocused (status only)`.

### Task 3: Beeminder goals, read only (C 11, ADR 43)
**Files:** PC: `Services/CalendarNetwork.cs` (the Beeminder GET calls: `https://www.beeminder.com/api/v1/users/me/goals.json?auth_token=…`), new `Services/Beeminder/` (`BeeminderService`: token DPAPI-encrypted in document "beeminder", refresh every 30 min and on Today open, keeps the last result), Integrations → Beeminder card (paste the personal auth token from beeminder.com/api/v1/auth_token.json, test, remove), Today card "Beeminder" (each goal: title, what's needed today / safe days, deadline 12-hour, red when due today); sync file: a derived `beeminder` list in the PC's file (title, needed today text, safe days, deadline) so the phone's Today shows the same card (phone has no token, opens no connection). Watch: deferred.
- [ ] Commit `Beeminder: your goals on Today (read only, ADR 43)`.

## Execution
Tasks 1 and 2 in parallel now; Task 3 after ADR 43 is written.
