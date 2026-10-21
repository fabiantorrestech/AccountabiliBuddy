# Pixel Watch companion: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the watch mockup into a real companion that shows today's plan, focus sessions, drift, the running session and the week as rings. It gets live data from the phone (Data Layer), falls back to the PC over Wi-Fi, and possibly to a direct Bluetooth link. It also adds a Tile and a watch-face complication.

**Architecture:**
- The PC sends today's focus history to paired devices as a new link message.
- The phone combines that with what it already has into a small watch snapshot, and sends it over the Data Layer under the user's link settings.
- The watch keeps the last snapshot and draws the pages, Tile and complication from it.
- The fallbacks feed the same snapshot.

**Tech Stack:**
- PC: .NET 10 Core/App.
- Android:
  - Kotlin;
  - Compose for Wear OS 1.7.1;
  - Tiles 1.6.2 and ProtoLayout Material 3 1.4.2;
  - watch-face complications 1.3.0;
  - play-services-wearable 20.0.1 (user-approved 2026-10-21).

**Spec:**
- [ADR 21](../adr/0021-add-a-pixel-watch-companion-fed-by-the-phone.md) (Accepted).
- [ADR 14](../adr/0014-pair-the-phone-with-the-pc-over-the-local-network.md) and [ADR 15](../adr/0015-share-activity-history-between-paired-devices.md).
- The user's answers of 2026-10-21:
  - Data Layer with the relay on by default, then nearby only, then Wi-Fi to the PC, then Bluetooth.
  - Each can be turned off before linking a watch.
  - All four pieces are in scope.

## Global Constraints

- The watch receives only: plan parts (name, category colour, times), today's focus sessions (times, profile name, added by hand or not), drift stretches (times), the running session (profile, start, end or none), and focused and planned minutes for today and the last 7 days. Never titles, addresses, OCR text or reasons, and never an added session's note.
- Nothing goes to a watch until it's linked on the phone. Each route has its own switch on the phone:
  - Data Layer: on by default.
  - Google's encrypted relay: on by default.
  - Wi-Fi to the PC: on by default, used only once the watch is paired with the PC.
  - Bluetooth: only if Task 7 proves it works.
- With the relay off, the phone sends only messages (MessageClient), and only to nodes where `isNearby` is true; no DataClient items.
- The watch app keeps the phone app's application id and debug-key signature (the Data Layer needs both to match).
- New dependencies use exactly these versions:
  - `androidx.wear.compose:compose-material3` / `compose-foundation` 1.7.1;
  - `androidx.wear.tiles:tiles` 1.6.2;
  - `androidx.wear.protolayout:protolayout-material3` 1.4.2;
  - `androidx.wear.watchface:watchface-complications-data-source-ktx` 1.3.0;
  - `com.google.android.gms:play-services-wearable` 20.0.1.
  - No others. `kotlinx-coroutines-play-services` is not added; Google Tasks are awaited with `suspendCancellableCoroutine`.
- Never install on the Pixel Watch or the Pixel 7 without the user's OK in that session. Never target the watch with adb by accident: always use `-s`.
- No tests in the repo; scratch checks (ktcheck, a new PC check) prove each step, RED first.
- The PC's link stays as ADR 14 says: pinned TLS, local network only, listening only on a Private network.

## Review Focus

1. **Relay off:** no DataClient item is ever written, and a send while the watch is away (not nearby) is skipped, not queued.
2. **A day with nothing:** no plan parts, no sessions, no running session, or the PC never linked. Every page, the Tile and the complication show a calm empty state, never a crash or "0/0".
3. **Stale data:** a snapshot from yesterday shows as old ("updated 9 h ago"), and today's rings never draw yesterday's sessions.
4. **Sessions across midnight:** an added or recorded session over midnight counts in both days' totals, cut at midnight, as on the PC.
5. **Unlinking:** unlinking the watch on the phone stops all sends, and the watch clears its data on the next message or on its own unlink screen.

---

### Task 1: PC: today's focus as a link message

**Files:**
- Create: `src/AccountabiliBuddy.Core/Link/LinkFocusDay.cs` (records: `LinkFocusDay(DateOnly Date, IReadOnlyList<LinkFocusSession> Sessions, IReadOnlyList<LinkWindow> Drift, int FocusedMinutes, int PlannedMinutes, IReadOnlyList<LinkDayTotal> Week)`, `LinkFocusSession(DateTimeOffset Start, DateTimeOffset? End, string? Profile, bool Added)`, `LinkWindow(DateTimeOffset Start, DateTimeOffset End)`, `LinkDayTotal(DateOnly Date, int FocusedMinutes, int PlannedMinutes)`), one per file.
- Create: `src/AccountabiliBuddy.Core/Engine/FocusDayBuilder.cs`: `static LinkFocusDay Build(DateOnly day, IReadOnlyList<SessionRecord> records, IReadOnlyList<DriftStretch> drift, DayPlan plan, Func<DateTimeOffset, bool> isOmitted, Func<Guid?, string?> profileName, DateTimeOffset now)`.
- Modify: `Link/LinkMessages.cs` (`FocusDay = "focusDay"`), `LinkServer.cs` and `App/Services/Link/LinkSessionHost.cs` (send on connect and on change, hashed like `settings`; the change triggers are a session start or end, a history change such as an added session, and every 5 minutes while a session runs).
- Modify: `Link/PairedDevice.cs` and `LinkPairRequest.cs` docs: kind "phone", "tablet" or "watch".

**Rules the builder pins:**
- It counts sessions the user started plus added ones.
- It leaves out omitted sessions and old scheduled blocks.
- It cuts each session at the day's edges.
- A running session counts until `now`.
- Planned minutes are the day plan's Watch parts for that day.
- The week is the 7 days ending today.

- [ ] **Step 1:** Write the scratch `focusdaycheck`:
  - a session across midnight counts 30 min on each day;
  - an omitted session counts 0;
  - an added session counts and is marked `Added`;
  - a scheduled block counts 0;
  - the running session counts until now;
  - the default plan's Tuesday is 540 planned minutes;
  - the week has 7 entries ending today.
- [ ] **Step 2:** Run it. Expected: compile errors (missing types).
- [ ] **Step 3:** Implement the records, the builder, the message and the send.
- [ ] **Step 4:** Run `focusdaycheck` and `linkcheck`. Expected: ALL PASS; linkcheck still passes.
- [ ] **Step 5:** Commit.

### Task 2: Phone: keep today's focus from the PC

**Files:**
- Modify: `android/.../link/LinkMessages.kt`, `link/LinkClient.kt` (handle `focusDay`).
- Create: `model/FocusDay.kt` and `data/FocusDayJson.kt` (parse the PC shape).
- Modify: `data/CompanionStore.kt` (file "focusday.json", `saveFocusDay`), `data/CompanionState.kt` (`focusDay: FocusDay?`).

- [ ] **Step 1:** Add ktcheck checks:
  - the PC's JSON (made by Task 1's check and written to `focusday-sample.json`) reads back with every field;
  - a missing `week` reads as empty;
  - the store keeps it across a restart.
- [ ] **Step 2:** Run. Expected: RED (missing types).
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run ktcheck and `assembleDebug`. Expected: ALL PASS, build succeeds.
- [ ] **Step 5:** Commit.

### Task 3: The watch snapshot and the Data Layer link

**Files:**
- Create: Gradle module `android/shared` (`com.android.library`, the same AGP; namespace `com.accountabilibuddy.shared`; only kotlinx-serialization-json).
  - `WatchSnapshot` moves here from `wear` (adds `createdAt`, `pcName`, `linked`).
  - Add `WatchSnapshotJson` (write/read).
  - Add `WatchRoute.choose(settings: WatchLinkSettings, nodes: List<WatchNode>): List<WatchNode>` (relay on: all connected nodes; relay off: only `isNearby`; Data Layer off: none).
  - Both `app` and `wear` depend on it.
- Create (phone): `watch/WatchLinkSettings.kt` (store "watch.json": `linkedNodeId`, `dataLayerOn = true`, `relayOn = true`, `wifiFallbackOn = true`, `bluetoothOn = false`).
- Create (phone): `watch/SnapshotBuilder.kt`, a pure function: CompanionState + link state + now → `WatchSnapshot`. It uses `FocusDay` and today's plan parts, and falls back to the plan alone when there's no `FocusDay`.
- Create (phone): `watch/WatchBridge.kt`. It finds the watch by the capability `accountabilibuddy_watch`, sends `/snapshot` with MessageClient through `WatchRoute`, answers the watch's `/hello`, and sends debounced on state changes (at most once every 10 s, plus at once on session start/end).
- Create (phone): the Settings → Pixel Watch card with:
  - a detected watch and a "Link this watch" button;
  - the four switches, with what each means (the relay text names Google's servers and end-to-end encryption);
  - Unlink.
- Create (watch): `res/values/wear.xml` (the capability); `link/WatchListenerService.kt` (`WearableListenerService`: `/snapshot` → `SnapshotStore` file → tell the UI, Tile and complication); `/unlink` clears it. On opening, the app sends `/hello` to the phone.
- Modify: `android/gradle/libs.versions.toml` (the versions above), both `build.gradle.kts`, `settings.gradle.kts` (`:shared`).

- [ ] **Step 1:** Add ktcheck checks (compile the `shared` sources):
  - snapshot JSON round trip;
  - `WatchRoute`: relay off drops a node that isn't nearby, Data Layer off sends to none;
  - SnapshotBuilder for a day with nothing, for a session across midnight (its part after midnight only), and leaving out an added session's note (its model has no note);
  - an old `FocusDay` (yesterday) is not used for today's rings.
- [ ] **Step 2:** Run. Expected: RED.
- [ ] **Step 3:** Implement the shared module, the phone bridge and settings, and the watch listener.
- [ ] **Step 4:** Run ktcheck and `assembleDebug` (all modules). Expected: ALL PASS, build succeeds.
- [ ] **Step 5:** Commit.

### Task 4: Real watch UI

**Files:**
- Modify: `wear/WatchActivity.kt`, `wear/ui/*`.
- Use Compose for Wear OS Material 3:
  - `AppScaffold`;
  - `HorizontalPagerScaffold` with page indicators;
  - `ScreenScaffold` with `TimeText`;
  - the three pages read the `SnapshotStore` (a flow).
- States:
  - not linked: "Link this watch in AccountabiliBuddy on your phone";
  - linked with no data yet;
  - old data: "updated 3 h ago" under the middle text;
  - a running session without an end.
- [ ] **Step 1:** Add ktcheck checks for the pure text helpers moved to `shared` (e.g. "updated 3 h ago", "2h 55m"; an empty day says "No focus yet" instead of "0m of 0m").
- [ ] **Step 2:** Run. Expected: RED.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run ktcheck and `:wear:assembleDebug`. Expected: pass. Visual check on the watch only with the user's OK (the emulator can't run here; Ruling).
- [ ] **Step 5:** Commit.

### Task 5: Tile and complication

**Files:**
- Create: `wear/tile/FocusTileService.kt` (ProtoLayout Material 3: today's focused/planned as an arc, the time left of a running session, tap opens the app).
- Create: `wear/complication/FocusComplicationService.kt` (types `RANGED_VALUE` = focused/planned today or the session's progress, and `SHORT_TEXT` = time left or focused today; refreshed when a snapshot arrives).
- Modify: the manifest services and permissions (`com.google.android.wearable.permission.BIND_COMPLICATION_PROVIDER`, the tile's `BIND_TILE_PROVIDER`).
- [ ] **Step 1:** Add ktcheck checks for the pure value helpers in `shared`:
  - ranged value from a snapshot (0 when nothing is planned, capped at the max);
  - short text ("25m" left, or "2h 55m").
- [ ] **Step 2:** Run. Expected: RED.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run ktcheck and `:wear:assembleDebug`. Expected: pass.
- [ ] **Step 5:** Commit.

### Task 6: Wi-Fi to the PC as a fallback

**Files:**
- PC:
  - `LinkPairRequest`/`PairedDevice` kind "watch";
  - Setup → Linked devices lists a watch with its own icon;
  - a paired watch receives `state`, `settings` (the plan part only is used) and `focusDay`, and may not start or end sessions (the server refuses `start` and `endEarly` from kind "watch").
- Phone: scanning the PC's QR code offers "Pair my watch with this PC". The pairing offer (address, port, fingerprint, one-time code) goes to the watch by a Data Layer message. This is a one-time message, sent even with the relay off when the watch is nearby.
- Watch: `link/PcLinkClient.kt`, the minimal client:
  - pinned TLS, framing, `hello`, read `state` / `settings` / `focusDay`;
  - it builds the same `WatchSnapshot` with the `shared` builder (moved there from the phone).
  - It runs only when no phone node has answered for 2 minutes and Wi-Fi is available. It asks for Wi-Fi with `ConnectivityManager.requestNetwork(TRANSPORT_WIFI)`, because Wear sends traffic through the phone by default, and lets it go after each update.
  - Permissions: `INTERNET` and `ACCESS_NETWORK_STATE`.
- Move the framing and pinning code from the phone's `link/` to `shared` if both need it; the phone's behaviour stays the same.
- [ ] **Step 1:** Add checks:
  - `linkcheck` (PC): a watch can pair, gets `state`/`settings`/`focusDay`, and `start` from a watch is refused;
  - ktcheck: framing round trip in `shared`.
- [ ] **Step 2:** Run. Expected: RED.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run linkcheck, ktcheck and the builds. Expected: pass.
- [ ] **Step 5:** Commit.
- **Uncertain:** whether the Pixel Watch keeps Wi-Fi up long enough for a TLS handshake on the user's network. This is checked on the watch with the user (Ruling if it fails).

### Task 7: Bluetooth fallback, test first

- [ ] **Step 1:** Spike (throwaway, not committed): with the user's OK to install on both devices, check whether a phone-app ↔ watch-app connection works next to the system's own Bluetooth link. Try RFCOMM with a fixed UUID first, then BLE GATT with the phone as server.
- [ ] **Step 2a, if it works:** a tiny protocol (the phone sends the same snapshot JSON, length-prefixed) behind the "Bluetooth" switch (off by default). The phone needs `BLUETOOTH_CONNECT`, which is requested when that switch is turned on. Commit.
- [ ] **Step 2b, if it doesn't work:** amend ADR 21 (the chain ends at Wi-Fi) and remove the switch.

### Task 8: Docs and ship

- Update the project CLAUDE.md:
  - the hard rules' connection list: the watch link and Google's encrypted relay are on by default and can be switched off;
  - the Layout (`android/shared`, `android/wear`);
  - the Commands;
  - the Roadmap.
- Version 0.1.51; fresh opus review of the whole branch, then one fix pass; commit, push, install the PC app. Phone and watch installs need the user's OK.
