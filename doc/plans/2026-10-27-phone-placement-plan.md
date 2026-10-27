# Phase 11b: phone placement — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** during a PC focus session the phone tells where it is (bag, face down on the desk, face up, pocket, in use) from its own sensors, and the PC nags when it's out. Never by buzzing the phone.

**Spec:** `doc/plans/2026-10-26-feature-list-plan.md`, "User answers" → Phone placement (2026-10-27).

**ADR:** none. There is no new connection, dependency or module boundary. One optional field joins the phone's sealed sync file, recorded in `doc/hard-rules.md`.

## Global Constraints
- Implementer rules: `.superpowers/sdd/implementer-rules.md`. No new dependencies or permissions. Proximity, light, accelerometer and significant motion need none.
- **No phone vibration or sound** from this feature.
- Only the placement label, times and counts go to the PC. Never sensor values, and never the work-use reason (it stays on the phone, like every reason).
- Low battery: sensors only while a PC focus session runs and not on a break; event-driven first.
- **Unsure (say so in the report):** how reliably Android delivers sensor readings to a background app while the screen is off and the phone lies still (Doze). Detection must degrade to "unknown", never to a false "out".

## Contract (both tasks)
- Phone sync file field `placement` (optional; absent from older phones):
  `{ "state": "bag|desk|faceUp|pocket|inUse|unknown", "since": ISO, "workUseUntil": ISO?, "outCount": int, "pocketMinutes": int, "sessionStart": ISO }`
- Kotlin `shared/sync/SyncFile.kt` + `SyncFileJson.kt`; C# `Core/Sync/SyncFile.cs` (a `SyncPlacement` record, `[JsonIgnore(WhenWritingNull)]`). The format version stays 1.

---

### Task 1: Phone detects its placement
**Files:** a new `placement/` package (`PlacementDetector`, `PlacementClassifier`, `WorkUseStore`), `notify/PinnedTimelineService.kt` (host it), `notify/ParkedNotice.kt` (actions), `sync/SyncCoordinator.kt` (`ownFile()`), the shared sync files above, and a Today/Setup line.

**Host:**
- The detector runs inside `PinnedTimelineService` (the existing `specialUse` foreground service) while `EssentialsLaunch.runningSessionStart(...) != null` and not on a break.
- With the timeline notification off there is no detection: the state is `unknown`, and Settings shows "Turn on the timeline notification for phone placement". Add a SetupFeatures entry (phone `setup/SetupFeatures.kt`) for this version.

**Signals, event-driven:**
- Screen on/off, `USER_PRESENT` (already received there), proximity changes (on-change sensor), light changes, and a wake-up significant-motion trigger.
- Plus a 5 s accelerometer sample at `SENSOR_DELAY_NORMAL` on each event and about once a minute while the service can run.

**Classifier (thresholds as named constants):**

| State | When |
|---|---|
| `inUse` | Screen interactive and unlocked, an app that isn't an essential in front (or unknown without Usage access), no work use, no emergency |
| `faceUp` | Screen off, proximity far, light > 15 lux, flat (gravity z > +8.5), still |
| `desk` (face down) | Proximity near, gravity z < −8.5, still |
| `bag` | Proximity near, light < 5 lux, still, not flat face down |
| `pocket` | Proximity near, dark, and either small body motion (variance above "still") or roughly upright (\|y\| dominant) |
| `unknown` | Anything else, or no fresh reading in 3 minutes |

"Still" means accelerometer magnitude variance below a constant over the sample window.

**Escalation on the phone:**
- 3 minutes of continuous `inUse` or `faceUp` without work use: open the Essentials screen silently (`Takeover.startFromBackground(EssentialsActivity.intent(this, Focus))`, with the same skip rules as Essentials: call, ringing, emergency, locked).
- `outCount` counts each new stretch of `inUse` or `faceUp` (no work use). `pocketMinutes` adds up pocket time.

**Need it for work:**
- A `ParkedNotice` action, plus a row on Today while a session runs.
- Pick Call / 2FA / Work app / Other, a short note, and 5 / 10 / 15 min.
- Uses 1–3 in a session: a note of at least 5 characters. From the 4th: at least 20 characters and a 30 s wait (`EndEarlyTerms`-style countdown).
- Stored in prefs "workUse" (reasons stay here; the file carries `workUseUntil` only).
- When it ends: the parked notification reads "Done? Put it back".

**Sync:** on a state change (not more than once per 20 s), call `driveSource.syncNow()` so the PC hears within about 10–30 s.

- [ ] Commit `Phone: detect where the phone is during a focus session (bag, desk, pocket, out)`.

### Task 2: PC nags from the phone's placement
**Files:** `Core/Sync/SyncFile.cs` (+ the `SyncPlacement` record), a new `Services/Sync/PhonePlacementService.cs` (modelled on `PcPickupsService`: subscribe to `ReceivedDevicesService.Changed`, newest phone file, dedupe), `App.axaml.cs` wiring, `Services/Sync/PhonePickups.cs` (`Describe`).

Only during a real focus session on this PC, not on a break, and never for a test session. Ignore a placement whose `sessionStart` differs from this session's start by more than 2 minutes.

**Nags:**

| State | What the PC does |
|---|---|
| `pocket` | `presenter.ShowNotice("Phone in your pocket", "A bag or face down on the desk is better.")` at most every 10 min |
| `inUse` or `faceUp`, no `workUseUntil` | At once: `ShowNotice("Phone's out", "Put it away (bag or face down).")`. Still out after 1 min: `ShowReminder` (stays until "Got it") "Still on your phone. It opens Essentials in 2 min." |
| `bag`, `desk`, `unknown`, or work use | Nothing |
| Work use ends | Once: `ShowNotice("Work use is over", "Put the phone back.")` |

**Session review:** the review's phone line adds "out N× · pocket M min" when the placement field is present.

- [ ] Commit `PC: nag when the phone is out or in a pocket during a session`.

## Review Focus
1. A false "out" while the phone is in a bag or face down, for example a stale reading after Doze. That must read as `unknown`.
2. Nags during a break, a test session, after the session ended, or about another day's session.
3. The work-use reason or sensor values leaving the phone.
4. Essentials opening over a call, while locked, during an emergency or during work use.
5. Battery: sensors left registered after the session ends, or a wake lock held.

## Execution
- After phase 11. Tasks 1 and 2 run in parallel; the Contract above is their interface.
- The orchestrator records the `placement` field and the rules in `doc/hard-rules.md` after merge.
- The user checks on the Pixel: bag, face down, face up, pocket while sitting and walking, unlock, and work use.

| Task | Implementer | Reviewer |
|---|---|---|
| 1 | Sonnet high | Opus medium (enforcement, privacy) |
| 2 | Sonnet medium | Opus medium (focus session) |
