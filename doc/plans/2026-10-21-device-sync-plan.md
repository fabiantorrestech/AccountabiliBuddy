# Sharing progress between your own devices: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** All of the user's devices share one running log of focused time, the day plan and settings: through a sync folder (Google Drive for desktop), QR codes, sync files and today's network link. The Devices tab shows where each device stands, and the setup assistant offers computer types (Personal, Work computer, Local only) with "link first".

**Architecture:**
- **One sealed file per device.** Each device writes its own encrypted "sync file": device card, its last 14 days of counted sessions and drift, its day plan and its shared settings. Every method only carries those sealed bytes.
- **Receiving devices** merge each file into a per-device copy and resolve day plan clashes with a popup.
- **Where the code lives:**
  - the format, key, merge and clash rules are pure code in Core (C#) and `android/shared` (Kotlin), kept byte-compatible by cross-language checks;
  - folders, QR screens, notices and tabs are in App, Platform and the phone app.

**Tech Stack:**
- PC: .NET 10, Avalonia 12, QRCoder 1.8.0 (already used), built-in `AesGcm`, `HKDF`, `DeflateStream`, `FileSystemWatcher`.
- Android: Kotlin, `javax.crypto`, `java.util.zip`.
- **New libraries, approved by the user on 2026-10-21:**
  - ZXing.Net 0.16.11 (PC, read QR pictures);
  - `com.google.zxing:core` 3.5.3 (phone, draw QR pictures);
  - `com.google.android.gms:play-services-auth` 22.0.0 (phone, only if Task 14 picks way A).

**Spec:**
- [2026-10-21-device-sync-design.md](2026-10-21-device-sync-design.md)
- [2026-10-21-devices-and-computer-types-design.md](2026-10-21-devices-and-computer-types-design.md)
- ADR 14, 15, 19, 21; project CLAUDE.md hard rules.

## Global Constraints

- **Never leaves a device:** window titles, full addresses, OCR text, early-end reasons, notes on added sessions (ADR 8, 15). Sync files carry readable fields only.
- **Everything that leaves a device is sealed:**
  - format: `"ABS1"` + 4-byte group id + 12-byte nonce + AES-GCM(Deflate(UTF-8 camelCase JSON)) with a 16-byte tag;
  - key: 32 bytes from HKDF-SHA256(ikm = the 200-bit link code, salt = `"AccountabiliBuddy sync"`, info = `"key"`);
  - group id: the first 4 bytes of HKDF(same ikm and salt, info = `"group"`).
- **The link code:** 40 Crockford base32 characters (`0-9A-HJKMNP-TV-Z`), shown in groups of 5. Spaces, dashes and lower case are ignored on entry. Stored only under DPAPI (PC) and TokenVault (phone).
- **Nothing is written or sent by a method until its switch is on.** Defaults: every method off, except the network link (as today).
- **Sync folder files:** `<folder>/<12 hex>.absync`, where the 12 hex are the first 6 bytes of SHA-256(group id + device id). Never a device name in a file name.
- **Intervals:** `Instant` (own change, 5 s debounce) / 1 / 5 / 15 min (default 15). Always written when a session ends.
- **Battery:** the phone checks faster than 15 min only while its pinned timeline notification runs.
- **Clash rule** (user-decided): a popup offers **Keep the one I was using** / **Use the latest**; closed or ignored → latest by edit time. A plan arriving during a focus session waits until it ends.
- **Received settings** apply by the device's own rules: profiles through `ConfigurationService.UpdateAsync` (stricter now, looser after the delay, never during a session).
- **No tests in the repo.** Scratch checks live in `%TEMP%\ab-sync-checks`:
  - `synccheck`: a C# console app referencing `src/AccountabiliBuddy.App`;
  - `ktcheck`: copy `%TEMP%\ab-watch-checks\ktcheck` and add the new shared sources and check files;
  - `linkcheck`: copy from `%TEMP%\ab-watch-checks`.

  RED first for every behaviour.
- **New libraries** only at the versions above (approved 2026-10-21). `play-services-auth` is added only if Task 14 picks way A.
- **Devices:** never install on the Pixel 7 (`29071FDH200AFV`) or the Pixel Watch without the user's OK in that session. Always use `adb -s`.
- **Commits:** email `46858378+fabiantorrestech@users.noreply.github.com`. Trailers: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and the `Claude-Session` line. Never commit `handoffContext/`.

## Order and lanes (for a multi-agent run)

| Lane | Tasks | Needs first |
|---|---|---|
| Foundation | 0 → 1 → 2 → 3 | Nothing |
| PC sync | 4 → 5 → 6, 7, 8 | Task 3 |
| Phone sync | 9 → 10 | Tasks 3 and 8 (the link message) |
| PC Devices tab | 11 | Tasks 4, 5, 7 |
| Computer types | 12 (any time) → 13 | Tasks 4, 5 for 13 |
| Watch Tile | 16 | Nothing (independent) |
| On-screen window | 17 | Nothing (independent) |
| The phone's watch on the PC | 18 | Tasks 9 and 11 |
| With the user | 14 (phone ↔ Drive spike), library OKs for 7 and 10 | The user present |
| Ship | 15 | Everything else |

Tasks in different lanes touch different files, except `App.axaml.cs` (service wiring: 4, 5, 7, 11, 12) and `MainWindow.axaml` (7, 11): merge those by hand.

## Review Focus

1. **A file that isn't ours or is damaged** (another group, wrong key, truncated, tampered, an older format) is ignored without a crash, and the Devices tab shows "Can't read a file from <folder>". Pinned in Task 1 (C#), Task 2 (Kotlin) and Task 5.
2. **The same day arriving twice** (QR, then the folder, then relayed by the phone) never doubles sessions. A file older than the stored one is ignored. Pinned in Task 3.
3. **Activity history turned off on a device** that shared before: its next file says `historyOff`, and receivers delete all of that device's sessions and drift (ADR 15). Pinned in Task 3.
4. **Sessions across midnight and DST days** count in both days, cut at midnight, as everywhere else. Pinned in Task 6.
5. **A very busy day for QR**: more than 4 codes' worth of data drops drift detail before sessions, and never shows more than 4 codes. Pinned in Task 3.

---

### Task 0: ADR 22 (sharing progress through carried files and your own cloud folder)

**Files:**
- Create: `doc/adr/0022-share-progress-between-your-own-devices-through-sealed-files.md` (Accepted, when the user approved the designs).
- Modify: `doc/adr/README.md` (via the adr skill's index script).

- [ ] **Step 1:** Write it with the adr skill. Its decision:
  - progress, the day plan and shared settings may leave a device only as sealed sync files, to the user's own devices;
  - carried by QR codes, files the user shares, a cloud folder the user picks, or the ADR 14 link;
  - Google never reads them.

  It amends the "local only" rule and supersedes [device-sync.md](device-sync.md)'s "never third-party services" for this purpose.
- [ ] **Step 2:** Commit with the first code task (adr skill rule).

### Task 1: Core sync format and key (C#)

**Files:**
- Create in `src/AccountabiliBuddy.Core/Sync/`, one type per file:
  - `SyncFile.cs`, `SyncDeviceCard.cs`, `SyncSession.cs`, `SyncDrift.cs`, `SyncDayPlan.cs`, `SyncDocument.cs`;
  - `SyncKey.cs`, `SyncKeyCode.cs`, `SyncFileCodec.cs`.
- Test: `%TEMP%\ab-sync-checks\synccheck\Program.cs`.

**Interfaces (produces):**
- **Records:**
  - `SyncFile(int FormatVersion, SyncDeviceCard Device, IReadOnlyList<SyncSession> Sessions, IReadOnlyList<SyncDrift> Drift, SyncDayPlan? DayPlan, IReadOnlyList<SyncDocument> Documents)`, with `FormatVersion` = 1.
  - `SyncDeviceCard(Guid DeviceId, string Name, string Kind, string AppVersion, DateTimeOffset WrittenAt, IReadOnlyList<string> Methods, bool HistoryOff, IReadOnlyDictionary<Guid, DateTimeOffset> Seen)`.
    - `Kind`: `"pc"`, `"phone"` or `"watch"`.
    - `Methods` ⊆ `link`, `folder`, `qr`, `file`.
    - `Seen`: the newest `WrittenAt` this device holds from each other device.
  - `SyncSession(DateTimeOffset Start, DateTimeOffset? End, string? Profile, bool Added)`: a null `End` means it was running at `WrittenAt`.
  - `SyncDrift(DateTimeOffset Start, DateTimeOffset End, string App, string? Site)`.
  - `SyncDayPlan(string PlanJson, string Version, DateTimeOffset EditedAt, string? BasedOn)`: `PlanJson` comes from `ProfileDocumentSerializer.SerializeDayPlan`.
  - `SyncDocument(string Key, string Json, DateTimeOffset EditedAt)`, where `Key` ∈ `profiles`, `sleep`, `daybar`.
- **`SyncKey`:**
  - `public byte[] Key` (32 bytes) and `public string GroupId` (8 lower-case hex characters);
  - `public static SyncKey FromSecret(ReadOnlySpan<byte> secret25)`.
- **`SyncKeyCode`:**
  - `public static string NewCode()`: 40 characters from 25 random bytes;
  - `public static bool TryParse(string text, out SyncKey? key)`;
  - `public static string Display(string code)`: groups of 5 joined by `-`.
- **`SyncFileCodec`:**
  - `public static byte[] Seal(SyncFile file, SyncKey key)`;
  - `public static SyncFile? Open(ReadOnlySpan<byte> sealedBytes, SyncKey key)`: null for a wrong group, bad tag, bad header, bad JSON or a `FormatVersion` above 1.
  - JSON uses `LinkFraming.JsonOptions`.

- [ ] **Step 1: Write the failing checks** in `synccheck`:
  - a round trip of a full file is field-for-field equal;
  - `TryParse` accepts `NewCode()` with dashes, spaces and lower case, and rejects 39 characters and `I`/`L`/`O`/`U`;
  - the same code gives the same key and group id;
  - Open returns null for: another code, a flipped byte, a truncated file, `"ABS2"`, `FormatVersion: 2`;
  - Open after Seal under a different key returns null (wrong group);
  - the sealed bytes don't contain a session's profile name in plain text.

  Then write the cross-language sample: `%TEMP%\ab-sync-checks\sample-cs.absync` and `sample-code.txt`.
- [ ] **Step 2:** Run. Expected: compile errors.
- [ ] **Step 3:** Implement the records, key and codec. Deflate with `CompressionLevel.Optimal`.
- [ ] **Step 4:** Run. Expected: all pass, and the sample is written.
- [ ] **Step 5:** Commit (with Task 0).

### Task 2: Kotlin sync format and key (android/shared), byte-compatible

**Files:**
- Create in `android/shared/src/main/java/com/accountabilibuddy/shared/sync/`:
  - `SyncFile.kt` (all the records above, the same names and fields);
  - `SyncKey.kt` (with `SyncKeyCode` as an object);
  - `SyncFileCodec.kt`, `SyncFileJson.kt`.
- Test: `ktcheck/src/main/kotlin/SyncChecks.kt`.

**Interfaces (produces):** the same records as Task 1, plus:
- `SyncKey(key: ByteArray, groupId: String)`;
- `SyncKeyCode.newCode()`, `parse(text: String): SyncKey?`, `display(code: String)`;
- `SyncFileCodec.seal(file, key): ByteArray`, `open(bytes, key): SyncFile?`;
- `SyncFileJson.write(file): JsonObject`, `read(json): SyncFile?`;
- times as `Instant`, ids as `java.util.UUID`.

- [ ] **Step 1: Write the failing checks.** The same checks as Task 1, plus:
  - Kotlin opens `sample-cs.absync` with `sample-code.txt`, field-for-field as the C# check printed;
  - Kotlin writes `sample-kt.absync`.

  Add to synccheck: C# opens `sample-kt.absync`.
- [ ] **Step 2:** Run ktcheck. Expected: RED (missing types).
- [ ] **Step 3:** Implement. HKDF via `Mac("HmacSHA256")`, AES-GCM via `Cipher("AES/GCM/NoPadding")`, raw Deflate via `Deflater(…, nowrap = true)`, so it matches .NET `DeflateStream`.
- [ ] **Step 4:** Run ktcheck and synccheck. Expected: all pass both ways.
- [ ] **Step 5:** Commit.

### Task 3: Merge, clash and QR chunks (both languages)

**Files:**
- Create in `src/AccountabiliBuddy.Core/Sync/`: `SyncMerge.cs`, `DayPlanClash.cs`, `DayPlanSyncDecision.cs`, `SyncQr.cs`, `SyncQrCollector.cs`, `SyncFileBuilder.cs`.
- Create the Kotlin mirrors in `shared/sync/`: `SyncMerge.kt`, `DayPlanClash.kt`, `SyncQr.kt`.
- `SyncFileBuilder` is PC-only.

**Interfaces:**
- **`SyncMerge.Merge(SyncFile? stored, SyncFile incoming, DateTimeOffset now) → SyncFile`:**
  - the incoming file's window is its earliest session start or `WrittenAt − 14 days`;
  - stored sessions and drift before the window are kept, those inside are replaced;
  - anything older than 90 days is dropped;
  - `HistoryOff` → no sessions or drift at all;
  - an `incoming.WrittenAt` older than the stored one → `stored` unchanged.
- **`DayPlanClash.Decide(SyncDayPlan local, SyncDayPlan incoming) → DayPlanSyncDecision`**, one of `Same`, `TakeIncoming`, `KeepLocal`, `Ask`:
  - `Same` if the versions are equal;
  - `TakeIncoming` if `incoming.BasedOn == local.Version`;
  - `KeepLocal` if `local.BasedOn == incoming.Version`;
  - otherwise `Ask`.
- **`DayPlanClash.Latest(local, incoming) → SyncDayPlan`:** by `EditedAt`, the incoming one on a tie.
- **`SyncQr.Links(byte[] sealedBytes, string groupId, int maxBytesPerCode = 1200) → IReadOnlyList<string>`:** `accountabilibuddy://sync?g=<groupId>&p=<i>-<n>&d=<base64url>`.
- **`SyncQrCollector`:** `bool Add(string link)`, `int Received`, `int Total`, `byte[]? Complete`, and a reset when a different group or `n` arrives.
- **`SyncFileBuilder.ForQr(SyncFile file, string groupId, int maxCodes = 4) → SyncFile`:** today only. It drops drift first, then documents, until the sealed size fits `maxCodes × 1200` bytes.
- **`SyncFileBuilder.Build(SyncDeviceCard card, IReadOnlyList<SessionRecord> records, IReadOnlyList<DriftStretch> drift, Func<DateTimeOffset, bool> isOmitted, Func<Guid?, string?> profileName, bool sessionRunning, SyncDayPlan? dayPlan, IReadOnlyList<SyncDocument> documents, DateTimeOffset now, TimeZoneInfo zone) → SyncFile`:**
  - 14 days;
  - the same counting rules as `FocusDayBuilder` (omitted and scheduled out, added in, only the newest recorded open one runs);
  - drift merged with the 2-minute gap;
  - `card.HistoryOff` → no sessions or drift.

- [ ] **Step 1: Write the failing checks** in synccheck, and in ktcheck for the mirrors:
  - **Merge:**
    - the same file twice gives no duplicates;
    - QR, then folder, then relayed copies give one copy;
    - an older file is ignored;
    - a session from 20 days ago is kept from the stored copy;
    - a 91-day-old one is dropped;
    - `HistoryOff` wipes that device.
  - **Clash:** all four decisions; `Latest` breaks ties toward incoming.
  - **QR:**
    - 3000 bytes give 3 links that `SyncQrCollector` reassembles in any order;
    - another group resets the collector;
    - a busy day (500 drift stretches) gets `ForQr` ≤ 4 codes with every session kept.
  - **Build:**
    - an omitted session isn't in the file;
    - an added one has `Added = true` and no note field exists;
    - the running one has a null `End`.
- [ ] **Step 2:** Run. Expected: RED.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run synccheck and ktcheck. Expected: all pass.
- [ ] **Step 5:** Commit.

### Task 4: PC sync identity, settings and received devices

**Files:**
- Create in `src/AccountabiliBuddy.App/Services/Sync/`:
  - `SyncSettings.cs` and `SyncInterval.cs`;
  - `SyncSettingsService.cs` (document `"sync.settings"`);
  - `SyncIdentityService.cs` (document `"sync.identity"`);
  - `ReceivedDevice.cs`, `ReceivedDevicesService.cs` (document `"sync.received"`);
  - `SyncEditTimesService.cs` (document `"sync.editedAt"`).

**Interfaces (produces):**
- **`SyncSettings`:** `(bool LinkOn = true, bool FolderOn = false, string? FolderPath = null, SyncInterval Interval = SyncInterval.FifteenMinutes, bool QrOn = false, TimeOnly? ReminderAt = null, int SnoozeMinutes = 15, bool FileShareOn = false)`.
  - `ReminderAt` null → when the day plan's last Work part ends.
  - `SyncInterval { Instant, OneMinute, FiveMinutes, FifteenMinutes }`.
- **`SyncIdentityService`:**
  - `Guid DeviceId`, `string DeviceName` (`Environment.MachineName`), `SyncKey? Key`, `string? CodeForDisplay()`;
  - `Task CreateGroupAsync()`, `Task<bool> JoinAsync(string code)`, `Task LeaveAsync()`;
  - the code is stored DPAPI-protected via `IDataProtector`.
- **`ReceivedDevice(SyncFile File, DateTimeOffset ReceivedAt, string Method)`.**
- **`ReceivedDevicesService`:**
  - `IReadOnlyList<ReceivedDevice> Devices`;
  - `Task AcceptAsync(SyncFile file, string method)`, which calls `SyncMerge.Merge`;
  - `Task ForgetAsync(Guid deviceId)`, `Task ForgetAllAsync()`;
  - `event EventHandler? Changed`;
  - `string? LastProblem` ("Can't read a file from …").
- **`SyncEditTimesService`:**
  - `DateTimeOffset? EditedAt(string key)`;
  - stamps the trusted time when `ConfigurationService`, `SleepSettingsService` or `DayBarSettingsService` change, unless `using (SuppressStamping())` is active while applying a received change.

- [ ] **Step 1:** Failing checks in synccheck (App-level, temp DB, fixed clock like `privacyfocus`):
  - the identity survives a restart, and the code isn't readable in the documents table;
  - a joined device has the creator's group id;
  - leaving forgets the key and every received device;
  - a received file survives a restart and is merged;
  - a local profile edit stamps `profiles`, and a suppressed one doesn't.
- [ ] **Step 2:** Run. Expected: RED.
- [ ] **Step 3:** Implement. Register the services in `App.axaml.cs` next to the link services.
- [ ] **Step 4:** Run. Expected: pass.
- [ ] **Step 5:** Commit.

### Task 5: Sync folder and the coordinator (PC)

**Files:**
- Create: `src/AccountabiliBuddy.App/Services/Sync/SyncCoordinator.cs`, `SyncFolderService.cs`, `DayPlanClashRequest.cs`.
- Create: `src/AccountabiliBuddy.App/Views/DayPlanClashDialog.axaml(.cs)` and `ViewModels/DayPlanClashViewModel.cs`.

**Interfaces:**
- **Consumes:** Tasks 1–4.
- **`SyncCoordinator`:**
  - `Task<SyncFile> BuildOwnFileAsync()`, from the activity log as `LinkSessionHost.BuildFocusDayAsync` does, with `HistoryOff` when `PrivacySettings.ActivityLogEnabled` is false;
  - `Task<string?> AcceptSealedAsync(byte[] sealedBytes, string method)`, which returns null when accepted, else why not;
  - `event EventHandler<DayPlanClashRequest>? ClashRaised`;
  - `event EventHandler? OwnDataChanged`, raised on session start/end, History changes, plan or settings edits.
- **`DayPlanClashRequest`:**
  - `(SyncDayPlan Local, SyncDayPlan Incoming, string FromDevice, IReadOnlyList<string> Changes)`;
  - `Changes` comes from the PC's day plan change wording (the PC counterpart of the phone's `DayPlanChanges`);
  - a dialog result is `KeepMine` or `UseLatest`; closed → `UseLatest`.
- **`SyncFolderService`:**
  - writes `<folder>/<file id>.absync` at the interval, on session end, and on `OwnDataChanged` when `Instant` (5 s debounce);
  - reads the other `*.absync` on `FileSystemWatcher` events (2 s debounce) and on each interval tick;
  - ignores its own file.

- [ ] **Step 1:** Failing checks in synccheck (a temp folder as the "Drive" folder, two App instances with two data dirs, fixed clocks):
  - **A writes, B reads:** B's `ReceivedDevices` has A's sessions.
  - **A plan edit reaches B:**
    - B's plan changes when B didn't edit (`TakeIncoming`);
    - both edited → `ClashRaised` with both edit times;
    - closing gives the latest;
    - during B's focus session the plan waits until it ends.
  - **Profiles:** a looser profile change from A becomes a pending change on B (looser-change delay), not applied at once.
  - **Damaged files:** a junk `.absync` in the folder → `LastProblem` set and no crash.
  - **Switch off:** with the folder switch off nothing is written.
- [ ] **Step 2:** Run. Expected: RED.
- [ ] **Step 3:** Implement. Put the dialog on the main window when it's open, else a notice that opens it.
- [ ] **Step 4:** Run. Expected: pass.
- [ ] **Step 5:** Commit.

### Task 6: The running log on the PC (day bar, History, totals)

**Files:**
- Modify: `ViewModels/DayBarViewModel.cs` (`ActualSessions`), `ViewModels/HistoryViewModel.cs` (rows, totals, a new "This PC / All devices" filter, default All).
- Modify: `Views/HistoryView.axaml`, `Engine/FocusDayBuilder.cs` (unchanged rules; it stays this PC's own day).
- Create: `src/AccountabiliBuddy.App/Services/Sync/ReceivedSessions.cs`.

**Interfaces:**
- **`ReceivedSessions.In(IReadOnlyList<ReceivedDevice> devices, DateTimeOffset from, DateTimeOffset to) → IReadOnlyList<ReceivedSession>`**, where `ReceivedSession(string DeviceName, DateTimeOffset Start, DateTimeOffset End, string? Profile, bool Added)`.
  - A null `End` becomes the sender's `WrittenAt`.
  - Sessions overlapping a local session aren't double-counted in totals (union by time).
- **Focus row:** received sessions are drawn in the device's own shade with "work PC · 9:00–10:30" tips.
- **History:** received sessions show a device tag.

- [ ] **Step 1:** Failing checks:
  - a received session across midnight counts 30 min on each day in History totals;
  - a received session overlapping a local one isn't counted twice;
  - the "This PC" filter hides received rows;
  - a received running session ends at its `WrittenAt`, not now.
- [ ] **Step 2:** Run. RED.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Pass.
- [ ] **Step 5:** Commit.

### Task 7: QR codes, reminder and import on the PC

ZXing.Net 0.16.11 is approved for the picture part.

**Files:**
- Create: `Services/Sync/QrSyncService.cs`, `Services/Sync/SyncImportService.cs`.
- Create: `Views/SyncQrWindow.axaml(.cs)`, `ViewModels/SyncQrViewModel.cs`.
- Modify: `ViewModels/NudgeToastViewModel.cs` and `Views/NudgeToast.axaml` (an optional second button and a "Later ▾" menu).
- Modify: `MainWindow.axaml` (a QR button in the top bar when `QrOn`), `Views/SessionOverlayWindow.axaml` (a QR button in the Ctrl+Alt details).

**Interfaces:**
- **`QrSyncService`:**
  - `IReadOnlyList<Bitmap> CodesForNow()`, via `SyncFileBuilder.ForQr` + `SyncQr.Links` + QRCoder;
  - `Task ShowReminderIfDueAsync(DateTimeOffset now)`: due at `ReminderAt` or when the plan's last Work part ends today. Skipped when no paired phone's `Seen[thisDevice]` is older than the last own change, or when it was already shown today.
  - Reminder text: "Scan today's code to sync your phone". Buttons: **Show code** and **Later ▾** with 5 min / 15 min / 30 min / 1 h; a plain Later uses `SnoozeMinutes`.
- **`SyncImportService`:**
  - `Task<string> ImportFileAsync(string path)` (`.absync`);
  - `Task<string> ImportPicturesAsync(IReadOnlyList<string> paths)`: decodes each picture's QR codes, feeds `SyncQrCollector`, and returns "2 of 3 codes, add the rest" until complete;
  - files can also be dropped on the main window.

- [ ] **Step 1:** Failing checks:
  - the codes for a busy day are ≤ 4;
  - the reminder isn't due when the phone has seen the last change;
  - it's due at the end of the last Work part;
  - Later with no choice snoozes 15 min;
  - importing a `.absync` from `sample-kt.absync` is accepted;
  - with the library, a PNG of 3 codes (rendered by QRCoder in the check) is reassembled.
- [ ] **Step 2:** Run. RED.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Pass.
- [ ] **Step 5:** Commit.

### Task 8: The network link carries sync files (PC side)

**Files:**
- Modify: `Core/Link/LinkMessages.cs` (`SyncFile = "syncFile"`, body `LinkSyncFile(string Data)`, base64 sealed bytes).
- Modify: `Core/Link/ILinkSessionHost.cs` (`Task<IReadOnlyList<byte[]>> GetSyncFilesAsync()`, `event EventHandler? SyncFilesChanged`, `Task AcceptSyncFileAsync(byte[] sealedBytes)`).
- Modify: `Core/Link/LinkServer.cs`, `App/Services/Link/LinkSessionHost.cs`.
- Create: `Core/Link/LinkSyncFile.cs`.

**Behaviour:**
- After sign-in the PC sends its own file and every received one (each hashed per connection; only changed ones are re-sent).
- A phone may send `syncFile` (files it carries).
- A watch gets none, and its `syncFile` is refused like other watch requests.

- [ ] **Step 1:** Failing linkcheck checks:
  - after sign-in a phone gets `syncFile` messages;
  - an unchanged file isn't re-sent;
  - a phone's `syncFile` reaches `AcceptSyncFileAsync`;
  - a watch's is refused.
- [ ] **Step 2:** Run. RED.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Pass.
- [ ] **Step 5:** Commit.

### Task 9: The phone joins, scans, carries and shares

**Files:**
- Create in `android/app/src/main/java/com/accountabilibuddy/companion/sync/`: `SyncIdentityStore.kt`, `ReceivedDevicesStore.kt`, `SyncCoordinator.kt`.
- Modify: `MainActivity.kt` and `AndroidManifest.xml` (deep links `accountabilibuddy://sync` and `accountabilibuddy://join`; open and share `.absync`).
- Modify: `link/LinkMessages.kt` and `link/LinkClient.kt` (`syncFile` both ways), `CompanionApp.kt`.

**Interfaces:**
- **`SyncIdentityStore`:** `deviceId: UUID`, `key: SyncKey?`, `join(code): Boolean`, `leave()`, the code sealed with `TokenVault`.
- **`SyncCoordinator`:**
  - `acceptSealed(bytes, method): String?`;
  - `onQrLink(uri): String`, e.g. "2 of 3 scanned", "Synced work PC";
  - `ownFile(): SyncFile`, which carries the device card and `Seen` only;
  - `filesToCarry(): List<ByteArray>`;
  - `received: StateFlow<List<ReceivedDevice>>`.
- **The phone as carrier:** files received by QR or Drive go to the PC over the link, and the PC's files go to Drive (Task 14) or out as a share.

- [ ] **Step 1:** Failing ktcheck checks for `SyncCoordinator`'s pure parts:
  - scanning 3 links out of order completes;
  - a link from another group is ignored with "This code is from another device group";
  - a received file isn't carried back to its sender;
  - `Seen` holds the newest `WrittenAt` per device.
- [ ] **Step 2:** Run. RED.
- [ ] **Step 3:** Implement. Joining from the PC's link code: scanning `accountabilibuddy://join?c=<code>` asks "Join <PC name>'s devices?" first.
- [ ] **Step 4:** Run ktcheck and `assembleDebug`. Pass.
- [ ] **Step 5:** Commit.

### Task 10: Combined day on the phone and watch; the phone's Devices tab

`com.google.zxing:core` 3.5.3 is approved for "Show as picture".

**Files:**
- Create: `android/shared/.../sync/FocusDayCombine.kt`.
- Modify:
  - `shared/FocusDay.kt` (`FocusSession` gains `device: String? = null`; JSON key `device`, optional);
  - `shared/WatchSnapshot.kt` (`sources: List<DataSource> = emptyList()`, `DataSource(name: String, dataAt: Instant)`; JSON key `sources`);
  - `app/.../watch/PhoneSnapshot.kt`;
  - `wear/.../ui/LinkScreens.kt` (lists the sources with their age).
- Create: `app/.../ui/DevicesScreen.kt`. Modify `ui/MainScreen.kt`: tabs Today, Profiles, Devices, Settings, About. The PC card and the Pixel Watch section move from Settings to Devices.
- Not in this plan: a History view on the phone. It comes with the queued phone Stats tab; until then the phone shows the combined totals in Devices and feeds them to the watch.

**Interfaces:**
- **`FocusDayCombine.combine(linkedPc: FocusDay?, linkedPcName: String?, received: List<Pair<String, SyncFile>>, today: LocalDate, zone: ZoneId) → FocusDay?`:**
  - the union by device;
  - the linked PC's live `FocusDay` wins over its own sync file;
  - totals are recomputed over the union;
  - each session tagged with its device.
- **Devices tab:**
  - **rows:** this phone, then devices, each with a state pill: Up to date / Behind (interval + 5 min) / Waiting for first sync / Unlinked;
  - **the totals line:** "Today: 5h 10m focused across 3 devices";
  - **actions:** Scan code (opens the camera hint), Share sync file, Show as picture (gated), Link a device (show this group's join QR and code), Leave group;
  - **method switches** with one-line descriptions, safest first.

- [ ] **Step 1:** Failing ktcheck checks:
  - the home PC's live day plus the work PC's file give one combined day with both tagged;
  - the home PC's own sync file isn't counted on top of its live day;
  - a 1-minute-old file is "Up to date" at a 1-minute interval, and a 7-minute-old one "Behind";
  - the snapshot JSON round trip keeps `sources` and session `device`, and older JSON without them reads.
- [ ] **Step 2:** Run. RED.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run ktcheck and `assembleDebug`. Pass.
- [ ] **Step 5:** Commit.

### Task 11: The PC's Devices tab

**Files:**
- Create: `ViewModels/DevicesViewModel.cs`, `ViewModels/DeviceRowViewModel.cs`, `Views/DevicesView.axaml(.cs)`.
- Modify: `MainWindow.axaml` (new tab after History).
- Modify `ViewModels/MainWindowViewModel.cs` and `Services/Search/SearchTabs.cs`. The new indices are: Session 0, Profiles 1, DayPlan 2, Exceptions 3, History 4, **Devices 5**, Setup 6, Integrations 7, Settings 8, About 9; Activity (dev) is last.
  - Grep for every hard-coded `SelectedTab =` number and fix it.
  - Add "Devices" to `UiSearchSource`.
- Modify: `Views/SetupView.axaml`: the Linked devices card moves into Devices.

**Interfaces:**
- **`DeviceRowViewModel`:** `(string Name, string Kind, string MethodsText, string LastText, DeviceSyncState State, string StateText)`, with `DeviceSyncState { ThisDevice, UpToDate, Behind, Waiting, Unlinked }`.
- **`DevicesViewModel`:**
  - rows;
  - `CombinedTodayText`;
  - commands Sync now, Show QR code, Import…, Link a device (this group's code + QR), Leave group, Unlink a paired phone;
  - method switches, interval, folder picker, reminder time and snooze default;
  - locked during focus, like other Revokes.

- [ ] **Step 1:** Failing synccheck checks:
  - the state pill rule (as Task 10, same thresholds);
  - Leave and the switches are refused while a focus session runs;
  - `SearchTabs.Settings == 8`, and opening a Settings search result lands on Settings.
- [ ] **Step 2:** Run. RED.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Pass; build the app.
- [ ] **Step 5:** Commit.

### Task 12: Settings the computer types need

**Files:**
- Modify:
  - `Services/PrivacySettings.cs`: `bool KeepTitlesAndAddresses = true`;
  - `Core/Domain/ActivitySegment.cs`: `bool KeepProtectedText = true`;
  - `Core/Storage/SqliteActivityLog.cs`: when false, title, url, OCR and channel ciphertext are null, while domain and section are still stored;
  - `Services/ActivityRecorder.cs`.
- Modify: `Core/Engine/EnforcementEngine.cs` (`EscalationLevel MaxLevel { get; set; } = EscalationLevel.Enforce`, which caps decisions). Create `Services/EnforcementLimitService.cs` (document `"enforcement.limit"`).
- Modify: `Services/UpdateSettings.cs` (`bool CheckForUpdates = true`) and the updater (no check when false).

- [ ] **Step 1:** Failing checks:
  - with `KeepTitlesAndAddresses` false a segment's protected columns are null and its domain and section are present;
  - `MaxLevel = Overlay` never yields `Minimize` or `RedirectTab` after 10 minutes of drift;
  - `CheckForUpdates = false` makes no check.
- [ ] **Step 2:** Run. RED.
- [ ] **Step 3:** Implement. The Settings UI gets the three switches under their groups.
- [ ] **Step 4:** Run. Pass.
- [ ] **Step 5:** Commit.

### Task 13: Setup assistant: link first and computer types

**Files:**
- Modify: `ViewModels/AssistantStep.cs` (`LinkFirst`, `ComputerType` after `Welcome`), `ViewModels/SetupAssistantViewModel.cs`, its view.
- Create: `Services/Setup/ComputerType.cs`, `ComputerTypePreset.cs`, `ComputerTypeSummary.cs`.

**Interfaces:**
- `ComputerType { Personal, Work, LocalOnly }`.
- `ComputerTypePreset.For(ComputerType) → PresetValues`, with the exact values in the computer-types design's table.
- `ComputerTypePreset.ApplyAsync(PresetValues, …services)`.
- `ComputerTypeSummary.Lines(PresetValues, SyncSettings) → IReadOnlyList<string>`, using the design's sentences. Includes "Makes no network connections of its own" only when every connecting setting is off.
- Document `"setup.computerType"`. Settings → App & data shows "Set up as: …" and "Apply a type again…".
- **Link first:**
  - "I have AccountabiliBuddy on another device" → enter or scan the code → `SyncIdentityService.JoinAsync`, then copy settings from the first received file (Task 5's apply path);
  - "Bring your blocks" defaults to skipped when profiles arrived.

- [ ] **Step 1:** Failing checks:
  - Work → OCR off, titles not kept, max level Overlay, link off, folder and QR on, Google Calendar off, updates on;
  - Local only → updates off and every sync method off;
  - the summary for Work with updates off includes "Makes no network connections of its own", and with updates on "Checks GitHub for updates";
  - applying a type again doesn't touch profiles.
- [ ] **Step 2:** Run. RED.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Pass.
- [ ] **Step 5:** Commit.

### Task 14: The phone and the Drive folder (test first, with the user)

**Gate:** the user's OK, in that session, to install a throwaway build on the Pixel 7. Way A also needs a Google Cloud Android OAuth client and ADR 23 (`play-services-auth` 22.0.0 is already approved).

- [ ] **Step 1: Spike (not committed).** Try:
  - **A:** the authorization client with Drive read access to the folder → list, read and write `.absync` (note whether a "Testing" app's access lapses after 7 days);
  - **B:** Autosync (or similar) mirroring the folder plus `ACTION_OPEN_DOCUMENT_TREE` on the mirror.

  Record the delay seen at each interval.
- [ ] **Step 2:** The user picks A or B. Record it in the sync design and, for A, in ADR 23.
- [ ] **Step 3:** Implement `app/.../sync/DriveFolderSource.kt` behind the phone's "Sync folder (backup)" switch, off by default. Faster than 15 min only while `PinnedTimelineService` runs; `WorkManager` every 15 min otherwise. Checks for the pure scheduling rule.
- [ ] **Step 4:** Commit.

### Task 16: The watch Tile shows the day plan with a moving "now" tick (independent of Tasks 0–15)

The user asked for this on 2026-10-21: the Tile shows how far along the workday is, like the app's Today ring (plan parts in their colours, focus, drift, a now hand).

**Files:**
- Modify: `android/wear/.../tile/FocusTileService.kt`.
- Create: `android/shared/.../TileRing.kt` (pure).

**Interfaces:**
- **`TileRing.arcs(view: TodayView): List<TileArc>`:**
  - `TileArc(startDegrees: Float, sweepDegrees: Float, color: Long, ring: Int)`, where `ring` 0 is the plan and 1 is focus;
  - drift ticks are 1-degree arcs on ring 1;
  - midnight is at 0° (the top), clockwise, 0.25° per minute.
- **`TileRing.nowDegrees(nowMinute: Int): Float`.**
- **The tile layout:** ProtoLayout `Arc`/`ArcLine` elements for the two rings and the now tick, with the existing glance texts in the middle.
- **The ticker moves** by one timeline entry per 5 minutes for the next 2 hours (my call: robust on every Wear OS version), each entry with its own `TimeInterval` validity and its own now tick.

- [ ] **Step 1:** Failing ktcheck checks:
  - 9:00–12:00 → start 135°, sweep 45°;
  - now at 15:00 → 225°;
  - an empty day gives no arcs;
  - yesterday's snapshot gives no arcs, as on the Today page.
- [ ] **Step 2:** Run. RED.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run ktcheck and `:wear:assembleRelease`. Pass. With the user's OK, install on the watch and add the Tile.
- [ ] **Step 5:** Commit.

### Task 17: Ctrl+Alt + double-click on the on-screen window opens the app (independent of Tasks 0–16)

The user asked for this on 2026-10-21. While Ctrl+Alt is held, double-clicking an empty spot of the on-screen window (the timer or timeline, not a button or slider) opens and focuses the main window, also when it's only in the tray. An "Open AccountabiliBuddy" button sits next to Hide in the Ctrl+Alt details. Without Ctrl+Alt the window stays click-through, as today.

**Files:**
- Create: `src/AccountabiliBuddy.App/Services/DoubleClickDetector.cs` (pure).
- Modify: `Views/SessionOverlayWindow.axaml.cs`, `Views/SessionOverlayWindow.axaml`, `ViewModels/OnScreenOverlayViewModel.cs`, `Services/SessionOverlayService.cs`, `App.axaml.cs` (subscribe → the existing `ShowMainWindow()`).
- Check: `%TEMP%\ab-sync-checks\syncapp\Checks\Task17Checks.cs`.

**Interfaces:**
- **`DoubleClickDetector(TimeSpan maxGap, double maxDistance)`:**
  - `bool Press(TimeSpan at, Point position)` returns true for a second press within `maxGap` and within `maxDistance` pixels of the first;
  - `Reset()`.
- **Values:** the gap is Windows' double-click time (`GetDoubleClickTime`, read once through `IOverlayInput` or a small Platform helper; 500 ms if unknown). The distance is 4 px.
- **The drag starts when the mouse moves** more than 4 px with the button down, not on the press (my call). This is because `BeginMoveDrag` on the first press can swallow the second click on Windows. The implementer confirms the drag still feels the same.
- **The event path:**
  - `SessionOverlayService.OpenAppRequested` (EventHandler), raised by the window's double-click and by `OnScreenOverlayViewModel.OpenAppCommand` (the new button);
  - `App.axaml.cs` calls `ShowMainWindow()`.

- [ ] **Step 1:** Failing checks in Task17Checks:
  - two presses 200 ms and 2 px apart → true;
  - 700 ms apart with a 500 ms gap → false;
  - 10 px apart → false;
  - a third press after a double-click starts over;
  - `OpenAppCommand` raises `OpenAppRequested` once.
- [ ] **Step 2:** Run. RED.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run run-all and build the app. Then try it by hand once in a dev build: Ctrl+Alt-drag still moves the window, Ctrl+Alt + double-click opens the app, and plain clicks still pass through.
- [ ] **Step 5:** Commit.

### Task 18: The phone's linked watch on the PC's Devices tab (after Tasks 9 and 11)

The user asked for this on 2026-10-21. The PC's Devices tab shows the phone's linked watch indented under the phone. It is display only: pairing and unpairing the watch stays on the phone. It's kept current: every time the phone writes its sync file (on each connection to the PC, both ways, and whenever its watch link changes), it first checks that the watch link is still valid.

**Files:**
- Create: `src/AccountabiliBuddy.Core/Sync/SyncCompanion.cs`; the Kotlin `SyncCompanion` goes in `android/shared/.../sync/SyncFile.kt`.
- Modify: `SyncDeviceCard` in both languages, `SyncFileCodec`/`SyncFileJson` (read and write), `app/.../sync/SyncCoordinator.kt` (the phone's own file), `app/.../watch/WatchBridge.kt` (link changes raise a resync), `ViewModels/DevicesViewModel.cs` and `Views/DevicesView.axaml` (the indented row).
- Checks: `synccore/Checks/Task18Checks.cs`, `ktcheck/src/main/kotlin/SyncT18Checks.kt` (both already wired into their runners), and `syncapp/Checks/Task11Checks.cs` (extend).

**Interfaces:**
- **`SyncCompanion(string Name, string Kind, bool Linked, DateTimeOffset? LastHeard)`**, where `Kind` is `"watch"`.
- **`SyncDeviceCard` gains `IReadOnlyList<SyncCompanion> Companions`**, last, defaulting to empty.
  - JSON key `companions`, **optional**: a file without it reads as empty, and `FormatVersion` stays 1.
  - Older apps ignore the key. Check that the PC's reader and Task 2's Kotlin reader both accept an unknown key, and change them if not, with a Ruling.
- **The phone's own file:** `companions` holds the linked watch when `WatchLinkSettings.linkedNodeId` is set. `Linked` is true while that node is still among the Data Layer's known watches (`CapabilityClient.FILTER_ALL` for `accountabilibuddy_watch`). `LastHeard` is the newest of the watch's last "/hello" and the last delivered snapshot.
  - Unlinked on the phone → no companion. The PC's row disappears at the next sync.
  - The capability check lives in an Android-only helper. The pure rule `companionsFor(settings, knownWatchIds, lastHeard)` lives in `shared` and is checked in ktcheck.
- **The PC row:**
  - under its phone, indented: "⌚ <watch name> · linked to <phone name> · last update <age>", with "Pair or unpair on the phone" as its detail;
  - `Linked = false` shows "not found on the phone — open AccountabiliBuddy on the watch";
  - **one row per watch:** if a `PairedDevice` of kind `"watch"` (Wi-Fi fallback) has the same name, it isn't listed separately. The indented row adds "also paired with this PC" and the PC-side Unpair (my call; names match exactly).

- [ ] **Step 1:** Failing checks:
  - a card with two companions round-trips in C# and in Kotlin, and across languages (extend the samples);
  - a file without `companions` reads as empty in both;
  - `companionsFor` with no linked node → empty; with a linked node that isn't known → `Linked = false`;
  - the PC rows: the phone's watch appears indented under the phone and not on its own;
  - a Wi-Fi-paired watch with the same name merges into that row.
- [ ] **Step 2:** Run. RED.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** run-all, `assembleDebug`, the PC build. Pass.
- [ ] **Step 5:** Commit.

### Task 15: Docs, version and ship

- [ ] **Step 1: Project CLAUDE.md:**
  - the hard rule's connection list (ADR 22: sealed files, the cloud folder the user picks, QR, file share; the work desktop opens no connection; Cold Turkey's expected blocks never sync);
  - the documents list (`sync.settings`, `sync.identity`, `sync.received`, `sync.editedAt`, `enforcement.limit`, `setup.computerType`);
  - the Layout (`Core/Sync`, `android/shared/.../sync`);
  - the Roadmap.
- [ ] **Step 2:** Version 0.1.52 (PC and phone, 152); build both release APKs; check `android_wear_capabilities` is still in both with aapt2.
- [ ] **Step 3:** A fresh opus review of the whole branch, then one fix pass (RED → GREEN each).
- [ ] **Step 4:** Commit, push, install the PC app. The phone and watch installs need the user's OK.
