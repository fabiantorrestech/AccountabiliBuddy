# Phase 4a: move the link's last jobs to sync files — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Everything that still needs the Wi-Fi link works through the sealed sync files (C 1, first half): start / end early from the phone, phone day-plan edits, and the phone's live view of the PC (current task, profile ids, daily goal, plan-built focus day). The link keeps working untouched until Phase 4b deletes it (that step needs the user's OK: it deletes files).

**Architecture:** New optional fields in the sync file on both sides (C# `src/AccountabiliBuddy.Core/Sync`, Kotlin `android/shared/.../sync`), PC handling in `SyncActionsService` reusing the link's decision logic (moved out of `LinkSessionHost` into reusable services so both paths share it), phone UI over the files whenever the link isn't connected.

**Spec:** ADR 42 (`doc/adr/0042-control-pc-sessions-and-plan-edits-through-sync-files.md`), decisions table in `doc/plans/2026-10-26-feature-list-plan.md` (C 1). Research map: the section "Where things are today" below (file:line from 2026-10-26).

## Global Constraints

- Implementer rules: `.superpowers/sdd/implementer-rules.md`.
- Reasons typed on the phone never leave the phone; the PC records "reason given on <phone name>, N characters".
- The end-early wait runs on the PC's clock; the phone can never shorten it.
- Every new field is optional; old files read; an old PC never answers (detected by version), an old phone ignores the new fields.
- Don't delete link code in this phase; don't change link behaviour.

## The contract (both sides must match exactly; JSON camelCase via `LinkFraming.JsonOptions` / `SyncFileJson.kt`)

| Where | Field | Shape |
|---|---|---|
| `SyncAction.Kind` | `start` | `Text` = JSON `{ "profileId": "<guid>", "minutes": 25 }` or `{ "profileId": "<guid>", "noSetEnd": true }` (minutes 1..720) |
| `SyncAction.Kind` | `endEarlyBegin` | no extra fields |
| `SyncAction.Kind` | `endEarly` | `Item` = the offer's `requestId`; `Text` = `"<N>"` (the reason's length only) |
| `SyncAction.Kind` | `dayPlanEdit` | `Text` = JSON `{ "basedOn": "<PC plan version>", "editedAt": "<iso>", "plan": <day plan JSON as in SyncDayPlan> }` |
| `SyncFile` | `actionResults` | list of `{ "id": "<action id>", "ok": bool, "message": "<shown to the user>"?, "planVersion": "<PC plan version>"?, "planEditedAt": "<iso>"? }`; a PC that supports results writes one for **every** action it saw, unknown kinds → `ok:false, message:"Not supported by this version"`; kept until the phone's action leaves its file (same lifetime as acks) |
| `SyncFile` | `endEarly` | `{ "requestId": "<guid>", "phaseStartedAt": "<iso>", "allowedFrom": "<iso>", "minReasonLength": 10, "note": "<the PC's text>"? }` or absent; ends on command (breaks, sessions with no set end) → no offer, the `endEarlyBegin` result is ok and the session ends at once |
| `SyncRunning` | `currentTask`, `profileId`, `countUpProfileId` | string?, guid?, guid? |
| `SyncFile` | `goalMinutes` | int? (ADR 29 daily goal) |
| `SyncDeviceCard` | — | phone treats a PC with `appVersion` < the version that ships this (PC 0.1.107) as "Update AccountabiliBuddy on <PC>" for control actions |

Freshness: `start` and `endEarlyBegin` older than 2 minutes, `endEarly` later than `allowedFrom` + 10 minutes, and `dayPlanEdit` older than 1 day are refused (`ok:false`, "Too late — try again"), never applied late.

## Tasks

### Task 1: PC side (Core records + SyncActionsService + running/goal fields)
**Files:** `src/AccountabiliBuddy.Core/Sync/` (`SyncAction.cs` kinds, new `SyncActionResult.cs`, new `SyncEndEarlyOffer.cs`, `SyncFile.cs`, `SyncRunning.cs`), `src/AccountabiliBuddy.App/Services/Sync/SyncActionsService.cs` (handlers + results; stop acking unknown kinds without a result), `Services/Sync/SyncCoordinator.cs` (write `actionResults`, `endEarly`, running fields, goal), the logic now in `Services/Link/LinkSessionHost.cs` (`StartAsync` ~330-356, `GetEndEarlyTermsAsync` ~358-383, `EndEarlyAsync` ~415-433, the plan edit ~389-412 via `Core/Engine/DayPlanEditDecision.cs`) moved into small shared services (e.g. `RemoteSessionControl`, `RemotePlanEdits`) that both `LinkSessionHost` and `SyncActionsService` call — the link's behaviour unchanged.
- End-early offer per ADR 42; the end notice like the link's (`App.axaml.cs` ~1054, `EndedFromDevice`); the reason is recorded as "reason given on <phone>, N characters".
- Plan edits: locked during a counted focus session (result `ok:false` + message; the phone keeps it waiting like ADR 19), conflict → `ok:false` with `planVersion`/`planEditedAt` so the phone asks; applied → `ok:true` and the plan goes out in the next file.
- [ ] Commit `Sync files: start, end early and plan edits from the phone (ADR 42), PC side`.

### Task 2: Phone side
**Files:** `android/shared/.../sync/SyncFile.kt` + `SyncFileJson.kt` (all contract fields), `android/app/.../sync/` (action creation, results handling, `ActionStore.kt`), the PC card / start + end-early UI (`ui/PcCard.kt`, `EndEarlyDialog`, `CompanionViewModel.kt` ~398-437), the plan edit path (`link/DayPlanSync.kt` → a file-based twin used when the link isn't connected; pending edit in `CompanionStore.kt` ~145-197; clash dialog reused), live view from files (`PhoneSnapshot.kt`, `RunningTasks.kt`, `HeaderSource.kt`, `FocusDayCombine.kt`: plan parts + planned minutes from the imported plan, goal from `goalMinutes`), `DrivePoll`/`driveBusy` reading the running state from files when there's no link (`CompanionApp.kt` ~289-297), the watch's "/hello" doing `driveSource.syncNow()` instead of a link connect when there's no link (`WatchBridge.kt` ~184-198), the PC to control = the newest PC file's, a picker in Devices when several.
- Countdown from `allowedFrom` + 3 s margin for clock skew; the reason box requires `minReasonLength`; only the length is sent.
- "Update AccountabiliBuddy on <PC>" for PCs older than 0.1.107 (by `appVersion`).
- With the link connected, keep using the link exactly as today.
- [ ] Commit `Phone: start, end early and plan edits through sync files (ADR 42)`.

### Task 3: Watch noise device switching through the phone (W 1)
After Tasks 1-2. **Files:** `android/wear/.../ui/NoisePage.kt`, `link/NoiseLink.kt`, the phone's watch bridge, a new action kind `noise` (`Text` = `{ "op": "toggle|next|prev|volume", "value": 0.5? }`) for PCs, PC `NoisePlayerService` applying it, the PC's noise state (playing, shown name, volume, locked) in its file (`noise` object), the watch page's device switcher (This phone → <PC 1> → <PC 2>…), with "a few seconds" lag shown.
- [ ] Commit `Watch: control a PC's noise player through the phone`.

## Execution
Task 1 and Task 2 in parallel (the contract above is the interface), then Task 3. Phase 4b (delete the link) waits for the user's OK.

## Where things are today (research, 2026-10-26)
- Link-only today: start (`LinkServer.cs:339` → `LinkSessionHost.StartAsync`), end early (`LinkServer.cs:346-404`, `LinkSessionHost.cs:358-433`), plan edits (`LinkSessionHost.cs:389-412`; phone `DayPlanSync.kt` sends only while connected, its own file sends `dayPlan = null`), live view gaps (currentTask, profile ids, goal; `FocusDayCombine` gives empty parts and no goal without the link).
- Already in files: settings (`PcFileSettings`), ticks/park/step away/come-backs (actions + acks), running session (`SyncRunning`), sessions/drift, calendar, sleep, lunch, reminders, blockers.
- `SyncActionsService.ApplyAsync` (~86-90) acks every action, unknown kinds included — an old PC would silently "accept" new kinds; hence `actionResults` + the version check.
- Poll intervals: PC Drive 10 s in a session/work part else 5 min (`SyncDriveService.cs:18-19`); PC writes 5 s after a change; phone Drive 30 s in a session or with the timeline pinned else 15 min (`DrivePoll.kt:7-8`); a phone action uploads at once (`CompanionApp.kt:197-203`).
- Must survive Phase 4b: `LinkFraming.JsonOptions` (used by `SyncFileCodec`), `DeviceCalendarEvents`, `DayPlanEditTimeService`, `FocusDayBuilder.CountedRecords`, `DayPlanEditDecision`, `SyncDeviceCard.MethodLink`, Android `TokenVault`.
