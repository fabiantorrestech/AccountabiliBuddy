# Brief 1c: newly linked device sees nothing, findings

Date: 2026-11-03. Read-only investigation of the project log's bug brief; nothing changed or run. Today's phone scanner (ADR 57) changed nothing about what a link carries: it hands a string to the existing `JoinLink.parse`.

**Root cause.** A code gives a device the group's key and no way of reaching anyone. Devices are only known from files received through a method both use, so a new device sits silent. Nothing says why, and Sync now reports success regardless. The log's hypothesis holds on the code. Not reproduced on devices.

## Investigate first

**1. Connect a Google account changes no sync state.**
- `CalendarSyncService.ConnectGoogleAsync` signs in with calendar.readonly and saves a `CalendarSource`. It touches neither `SyncSettings` nor `DriveAccountService`, which has its own sign-in (drive.file) and token ("sync.drive").
- Only indirect effect: the new calendars' events enter this PC's own file (`calendars.Changed`), which still has no way out.
- Its wording doesn't imply sync: "Google account connected", and the hint speaks of calendars. Sync now's does (point 3).
- ADR 26 says the PC reuses the Calendar sign-in "with the scopes united". The code doesn't. The ADR and the code have drifted.

**2. What a link carries.**
- Typed code (the PC's only join): the 40-character key and nothing else. `JoinAsync` takes only `SyncKeyCode`; a pasted link is refused as not a code.
- QR or join link (phone): code, name, and relay `r`/`p`/`k` only while the relay is on (`RelayJoinParameters`, `JoinLink`). Never folder or Drive.
- So setup spec P4's "pre-ticked when the link carried them" is relay only, on phones only. A PC joining a relay group gets nothing.

**3. Sync now with nobody reachable.**
- Devices page (`DevicesViewModel.SyncNowAsync`, `App.axaml.cs` `devices.SyncNow`): writes on folder, Drive and relay; each returns silently when off; no read. The message is always "Sent this PC's sync file.", even with no method on. The tooltip adds "hand it to connected devices".
- Title bar (`RefreshAll`): writes and reads; its tip says "Up to date · refreshed …" with no method on and no device heard.
- Phone (`CompanionApp.refreshNow`): same "Up to date", and in a toast.

**4. "Unlinked" and "disconnected".**
- "Unlinked" has two sources: this PC's own row while it has no key (`DevicesViewModel.Refresh`, `InGroup` false), and a watch the phone can't find ("Not found", state `Unlinked`).
- "Disconnected" appears nowhere in sync wording. Other rows say Up to date, Behind by…, Removed; the phone says "Not linked yet".
- After a join the own row is "This PC / Now" with a blank line, and `SyncLightViewModel` hides with no others, so the top bar says nothing either.
- **Doesn't match the report:** another device's row only comes from a received file, and a join to a new group forgets those (`ChangeCodeAsync`). A code alone can't produce a row for the source PC. Most likely the owner read the PC's own row before the join finished, or a file came another way. A screenshot would settle it.

## Objection (a): holds

Evidence: official docs plus code; confidence medium-high.
- Google's scope table limits drive.file to files created with, opened with, or shared with the app through its picker ([docs](https://developers.google.com/workspace/drive/api/guides/api-specific-auth)). They don't discuss Drive for desktop by name.
- ADR 26 says "only files the app made". `GoogleDriveFiles` finds its folder "among the files the app made"; the status line says the same. The code has no picker.
- So files placed by Drive for desktop aren't visible to the Drive method. The owner's devices sync by folder with the Drive method off, so even a united sign-in plus the tick would have left this PC alone unless they also write through the Drive method. The reported case wouldn't be fixed.

## Smallest fix meeting Acceptance

No change to what sync files carry, so no hard-rules check for files. Steps 1 to 3 cover a typed code; step 4 covers "if the link can carry it".

1. **Says so in plain words.** `DevicesViewModel.cs` and `Views/DevicesView.axaml`: a line while in a group with no other device received. No method on: "Linked, but this PC can't reach your other devices yet. Turn on the sync method they use: a sync folder, Google Drive or your relay." Method on: "Nothing has come through {methods} yet. Your other devices must use the same one." The same text replaces "Linked to your other devices." after Join.
2. **Sync now stops claiming success.** `DevicesViewModel.SyncNowAsync`, `App.axaml.cs` (`devices.SyncNow`, `RefreshAll`), `SyncFolderService`, `SyncDriveService`, `SyncRelayService` (`WriteNowAsync` says whether it wrote). Read as well as write, then report: "No sync method is on, so nothing was sent."; "Sent through {methods}. No other device has answered yet."; otherwise as today. Fix the tooltip.
3. **Phone twin.** `CompanionApp.refreshNow`, `ui/DevicesScreen.kt`, same words.
4. **Link offers the method, names only.** Add `&m=drive,folder` in `RelayJoinParameters` and the phone's link builder in `ui/DevicesScreen.kt`; `JoinLink.kt` reads it, and older phones already ignore unknown parameters. After joining, the phone's setup P4 and Devices page pre-tick or point to those. A folder name says "use a sync folder", never where. The PC's Join box would need to accept a pasted link (a `JoinLink` twin in Core plus a Copy link button); a typed code can never carry it. This is the only step adding a surface, so it could ship second.

Optional one-liner: `CalendarFeedsView.axaml`'s hint adds "This is for calendars; it doesn't sync your devices." That can wait for the rule below, which would make it false.

## Waits for the owner's Google rule

Connect asking for both scopes with "Sync my devices through this account" pre-ticked. Open: objection (a) means it doesn't fix this case; objection (b), a work calendar account; setup spec line 12; ADR 26 amendment. Steps 1 to 4 stand either way. It overlaps 1a's sync step, so plan them together.

## Flagged, not fixed

- `RefreshAll`'s `ToString("t")` follows the system format and the phone's `refreshNow` prints a raw `LocalTime` (24-hour), against the 12-hour rule.
- `DriveStatusText` and `DriveAccountService` send users to "Settings › Your day › Calendars", while the calendars now live in Settings › Connections per `CalendarFeedsViewModel`. Check the wording when step 1 is done.
