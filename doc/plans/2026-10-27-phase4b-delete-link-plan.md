# Phase 4b: delete the Wi-Fi link — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** remove the ADR 14 Wi-Fi link: its server, pairing, port, the phone's client, the watch's PC route, their UI and their stored secrets. Everything it carried now travels in the sealed sync files (ADR 42).

**Spec:** user approval 2026-10-27 ("Delete it all": link, pairing, port and the watch's PC route; the watch works through the phone only). Research: the scope notes below (2026-10-27).

**ADR:** [45. Delete the Wi-Fi link](../adr/0045-delete-the-wi-fi-link.md) (written 2026-10-27). It supersedes ADR 14 and the link parts of 15, 19, 21 and 26, and amends 42's "separate step". The README rows are updated.

## Global Constraints
- Implementer rules: `.superpowers/sdd/implementer-rules.md`. No new dependencies. This is a **breaking change**: an old phone can't link to a new PC, and a new phone can't link to an old PC. Both still sync through files.
- Keep (they are used by sync):
  - PC: `LinkFraming.JsonOptions`, `DeviceCalendarEvents`, `LinkDayPlanEdit`/`Answer`/`Status`, `LinkEndEarlyTerms`, `FocusDayBuilder.CountedRecords`/`Local`/`At`/`DriftGap`, `DayPlanEditTimeService`, `SyncDeviceCard.MethodLink` (old files still read).
  - Phone: `PcState`/`PcProfile`/`PcStateAt`/`EndEarlyTerms`/`DayPlanAnswer`.
  - Shared: `TokenVault` with its Keystore alias `"accountabilibuddy-link-token"` (the sealed group code depends on it), and QRCoder.
  - **For the relay (phase 6, ADR 46):** the PC's certificate fingerprint helper (`LinkIdentity.Fingerprint`, and `CreatePfx`'s self-signed certificate code as a reference) and the phone's `shared/.../link/PinnedTrustManager.kt`. Move both to sync packages; don't delete them.
- Move and rename those kept types out of the link packages. Behaviour doesn't change.
- Core keeps no reference to App types (ADR 4).

## Tasks

### Task 1: PC
**Move:**
- `LinkFraming.JsonOptions` → `Core/Sync/SyncJson.Options`. Drop the framing methods.
- `DeviceCalendarEvents` → `Services/Sync/`.
- `LinkDayPlan*` → `DayPlanEdit*`, and `LinkEndEarlyTerms` → `EndEarlyTerms`, both in Core/Sync or Remote.

**Delete:**
- `Core/Link/*`, except what moved.
- `Services/Link/*` (`LinkService`, `LinkSessionHost`, `PublicNetworkNoticeService`).
- `Core/Ports/INetworkTrust.cs`, `PublicNetwork.cs` and `Platform.Windows/Network/WindowsNetworkTrust.cs`. Keep a copy of `WindowsNetworkTrust`'s adapter filter logic in the plan notes for Places (phase 12), or leave the file and its port in place if Places will need them; prefer leaving them, unused but compiled, with a comment "kept for Places (phase 12)".
- `LinkedDevicesViewModel`/`LinkedDeviceRowViewModel`, and the Devices view's Wi-Fi link blocks plus the "old way" switch.
- `SyncSettings.LinkOn` and the presets' `LinkOn`, plus the Setup assistant's wording.
- Every `_link`/`linkHost` use in `App.axaml.cs`.
- `LinkFocusDay` and family, and `FocusDayBuilder.Build`.

**Rewire:** `ProfilesViewModel.LinkedDevices` and `HasLinkedPhone` use `ReceivedDevicesService`, so per-device profile rows remain.

**Clean up once on upgrade:** delete the documents `devices.settings`, `devices.paired`, `devices.identity` and `devices.publicNetworksTold`. Add `SqliteJsonDocumentStore.DeleteAsync`, keyed off `app.version`'s previous version.

- [ ] Commit `PC: delete the Wi-Fi link (sync files carry everything)`.

### Task 2: Phone, watch, shared
**Move:** `PcState`, `PcProfile`, `PcStateAt`, `EndEarlyTerms` and `DayPlanAnswer` → `sync/`. Drop `PcState.parse` (link JSON). `shared/link/TokenVault` → `shared/` (package only; alias unchanged).

**Delete:**
- Phone: the `companion/link/` package (`LinkClient`, `LinkDiscovery`, `LinkStore`, `LinkedPc`, `PairingLink`, `LinkMessages`, `LinkStatus`, `DayPlanSync`); the pairing UI (`PairingDialog`, `SettingsScreen.PcSection`, `onPastePairingLink`, `onUnpairPc`, the `pair` intent and its manifest filter, `/pc-paired`); `WatchBridge.sendPcOffer`/`notePcPaired`; the "Wi-Fi straight to the PC" toggle; the link-only focus-day state (`focusday.json`, `CompanionState.focusDay`); and `DayPlanFileSync.linkConnected`.
- Rewrite every `link.status`/`lastState`/`pairedPc` consumer from the research list: `CompanionApp` (construction, `sendSyncFilesToPc`, `applyPcFileSettings`, `driveBusy`, the header merge, `refreshNow`), `EssentialsLaunch.realSessionStart`, `HeaderSource`, `Takeover`, `CompanionViewModel` (`useFiles()` is always true), `MainScreen`, `PhoneSnapshot`, `SyncCoordinator` (`pcPaired`, `METHOD_LINK` sending, `filesForPc`, `linkOn`).
- Watch: `wear/.../link/PcLink.kt`, the `PC_OFFER` handling, the Wi-Fi fallback in `WatchActivity`, the "Wi-Fi to the PC" line, `/pc-offer`, and `CHANGE_NETWORK_STATE` (plus `INTERNET`/`ACCESS_NETWORK_STATE` if nothing else uses them; check).
- Shared: `PcOffer`, `WatchPaths.PC_OFFER`/`PC_PAIRED`, `WatchLinkSettings.wifiFallbackOn` and `WatchSnapshot.wifiAllowed` (old JSON is still read and ignored).

**Clean up once on upgrade:**
- Phone: `companion/link.json`, `companion/focusday.json`, and `wifiFallbackOn` in `watch.json`.
- Watch: `pc.json`.

**First run with no PC file:** the PC card says "Sign in to Google Drive (Devices) to see and control your PC". No "Not linked" state.

- [ ] Commit `Phone, watch: delete the Wi-Fi link`.

### Task 3: Docs (orchestrator, after both merge)
- The new ADR.
- `doc/hard-rules.md`: the link bullets, the stored `devices.*` documents, the watch-via-PC paragraph, and the test-session "link state" clause.
- `CLAUDE.md`'s hard-rules and layout lines.
- `doc/roadmap.md`.
- Mark `doc/plans/linked-devices.md` and the watch plan as historical.

## Review Focus
1. A feature that silently stops because it read link-only data: the phone's live view (current task, profile, goal, plan parts), per-device profiles, the watch's PC noise.
2. Upgrade with a PC still paired: secrets deleted, no crash, no lingering listener or firewall prompt.
3. An old phone talking to a new PC: it shows "Update", and sync still works.
4. `TokenVault`'s alias changed by the move, which would make the sealed group code unreadable.
5. A Core → App reference introduced by the moves.

## Execution
After phases 11 and 11b (they touch the same files). Tasks 1 and 2 run in parallel; Task 3 follows. Implementers: Sonnet high; reviewers: Opus medium (sync, devices).

## Research notes (2026-10-27)
- No firewall rule, port reservation or installer hook exists (Windows prompted on first listen).
- Lag after deletion: phone → PC is a Drive upload plus the PC's poll, 10 s in a session or work part, else 5 min. PC → phone is a 30 s poll in a session or with the timeline pinned, else 15 min. Without Google Drive (or the phase 6 relay) there is no PC↔phone control.
