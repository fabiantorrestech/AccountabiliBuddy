# Setup assistant flow: phone, PC and watch (spec for review)

Date: 2026-10-30. Status: **draft for the user's review**. The phone's new screens are built in the Android phone UI run, restyled to the design boards. The PC's Restore step and the unlinked paths are built with the device-removal work (ADR 54, 55).

Legend: **[exists]** is there today; **[new]** is to build; **[changed]** exists but changes. Every screen lists its purpose, controls, what it saves, when it is skipped, and its focus lock.

Rules that apply everywhere:
- Times are 12-hour.
- Every step has Back, except the first, Done and the Unlinked screen.
- "Skip for now" never skips a step that only reads (Welcome, Done).
- A step that loosens a guard follows the looser-change wait and never runs during a focus session the user started.
- Nothing is sent anywhere until the user picks a sync method.

---

## 1. Phone (also the tablet layout)

Opens on first run (prefs `ui.setupShown` false), from Settings › "Open the setup again", and after Start from scratch.

| # | Screen | Purpose and controls | Saves | Skipped when | Status |
|---|---|---|---|---|---|
| P1 | **Welcome** | What the app does on the phone, in three lines. **Get started**. | none | never | [new] |
| P2 | **Your other devices** | "This is my first device" / "I have AccountabiliBuddy on my PC or another phone". The second shows **Scan the QR on the other device** (system camera, `accountabilibuddy://join` deep link; scanning inside the app is a phone-UI-run choice) and **Type the link code**. | the join → `sync.json` | never | [new]; today linking is only in the Devices tab |
| P3 | **Restore from an older device** (only after a join) | Lists backups found on Drive under `Backups/<device>/` (newest first, with the date and device name), devices marked removed in the group, and **Choose a backup file…** (system picker). **Restore** or **Not now**. Restoring brings that device's history (sessions, pickups, sleep) back under its old name. A backup from another group shows "made in a different group; link to that group to restore it". | the restored copy goes into received devices | first device (no join); nothing found and no file chosen | [new] (ADR 55) |
| P4 | **How it syncs** | Google Drive (sign in) · Sync folder (pick a folder) · Your own relay (scan its QR) · Not now (QR and file only). Joined phones see the PC's methods pre-ticked when the link carried them (relay). | `PhoneSyncSettings` | first device: may choose "Not now" | [new] as a step; the settings exist |
| P5 | **Permissions** | Today's list (`ui/PermissionsList.kt:54-91`): notifications, unrestricted battery, usage access, display over other apps, nearby devices, Health Connect sleep. Each row has Allow; **Skip for now**. | system grants | never; individual rows can be skipped | [exists] (page 0 today) |
| P6 | **Curbox** | What to set up in Curbox, allowing the app, re-enable times, hard mode (`CurboxSetupPage`). | Curbox setup prefs | Curbox not installed: one line, "Install Curbox to block apps", and Skip | [exists] (page 1) |
| P7 | **The night** | Bedtime essentials and a LockMyPhone-Extended lock (`BedtimeSetupPage`). | sleep prefs | none | [exists] (page 2) |
| P8 | **Pair your watch** | Shown when a Wear OS watch is connected: **Link this watch** (`WatchBridge.linkWatch`) or Not now. | `watch.json` | no watch found | [new] as a step; the action exists in Settings |
| P9 | **Done** | Summary: linked to <PC name> / syncs by <method> / restored <device> / watch linked. **Open Today**. | `setupShown = true` | never | [new] |

Focus lock: during a PC focus session the user started (synced), P4 can't turn sync off and P5–P7 can't loosen any guard. Those steps show "after your session".

### Phone: the Unlinked path [new] (ADR 54)
Opens when the phone finds its own id in a rekey file, at app start or on any sync. It is a full-screen state with no Back and nothing syncing.
- **Title:** "This phone was removed from your synced devices". **Line:** "by <device name> on <date, 12-hour>". No default choice.
- **Add this phone again:** the P2 linking screen (scan or type a code from a device that is still linked). On success it rejoins with the same device id, and the others clear its removed mark when its file arrives. Then P3 (restore) if wanted, then Done.
- **Continue unsynced here:** leaves the group. All local data, settings and history stay; sync methods turn off. A one-time note: "You can link again from Devices". It lands on Today.
- **Start from scratch:**
  - A typed confirm ("start over").
  - It is offered only when no focus session the user started is running; otherwise "after your session".
  - Local files and prefs are cleared, the watch gets `UNLINK`, and setup opens at P1.
  - A local backup of the phone's data is offered first.
- **Watch:** shows "Your phone was removed from your synced devices. Choose what to do on your phone." It keeps its snapshot until the phone chooses.

### Phone: removing another device [new]
Devices tab › a device's ⋯ › **Remove this device…**:
1. What removal does.
2. Its data: Keep / Erase.
3. Backup: Drive or a file.
4. Warning about devices on old versions.
5. Confirm.

Locked during a focus session the user started.

---

## 2. PC

### First run (quick) [changed]
Today it runs ComputerType → LinkFirst → Goal → Done (`AssistantFlow.cs:21-37`). New order:

| # | Screen | Purpose and controls | Saves | Skipped when | Status |
|---|---|---|---|---|---|
| C1 | **What kind of computer** | Local only / Work / Personal, plus Guard work hours. | `"setup.computerType"` | never | [exists] |
| C2 | **Your other devices** | First device / I have AccountabiliBuddy elsewhere + link code. A first device can **Create the group now** (shows the QR for the phone), so the phone can join during its own P2. | `"sync.identity"` | never | [changed]: add Create now |
| C3 | **Restore from an older device** | As P3: Drive `Backups/`, removed devices, or **Choose a backup file…** (default `Documents\AccountabiliBuddy backups\`). | received devices | first device with nothing found | [new] |
| C4 | **Daily goal** | 30 / 60 / 120 / 240 min. | goal | none | [exists] |
| C5 | **Done** | Summary + "More setup is in Settings › Setup". | `HasBeenShown` | never | [exists] |

### Full setup (Settings › Setup) [changed]
Welcome → LinkFirst → **Restore** [new] → ComputerType → Import → Usage → Review → Browsers → Basics → Workdays → Cold Turkey keys → Spare key → Done (`AssistantFlow.cs:39-55`). Joining still skips Import, Usage and Review.

### PC: the Unlinked path [new]
A modal dialog at start or on sync, with the same three choices as the phone. **Start from scratch:**
- typed confirm
- an automatic DB backup first (ADR 10's mechanism)
- the data folder is cleared, the app restarts, and the first run opens at C1
- not during a focus session the user started

### PC: removing a device [new]
Settings › Devices & sync › row ⋯ › **Remove this device…**, the same five steps as the phone. Watch rows stay "managed on the phone"; removing a phone takes its watch off the list.

---

## 3. Watch

The watch has no group membership; it rides on its phone (ADR 21).

| # | Screen | Purpose | Status |
|---|---|---|---|
| W1 | **Open the app on your phone** | Shown until a phone links it. One line plus a phone icon. | [exists] (`LinkScreens.kt`) |
| W2 | **Linked** | "Linked to <phone name>", then the rings. | [exists] |
| W3 | **Your phone was removed** | See the phone's Unlinked path; keeps the snapshot. | [new] |
| W4 | **Unlinked by the phone** | After `UNLINK` (the phone chose start from scratch, or unlinked the watch): "Forget data" / "Keep for now". | [changed]: today it wipes at once |

---

## Open questions for the user
1. Phone P2: keep using the system camera plus the deep link as today, or scan inside the app?
   - The app has ZXing core (it draws QR codes) but no camera library.
   - In-app scanning would mean adding CameraX (a new dependency, needs your OK) and the camera permission.
   - The draft keeps the system camera.
2. Restore: should a restored device's history appear under its old name ("Pixel 7") or merge into this device ("this phone, including Pixel 7's history")? The draft says its old name.
3. Should "Continue unsynced here" on the phone also turn off sending pickups/sleep to the watch? The draft says no; the watch link is separate.
4. Start from scratch on the PC: also uninstall the browser extension link / Cold Turkey keys, or only app data? The draft says app data only.
