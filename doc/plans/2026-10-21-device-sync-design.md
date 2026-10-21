# Sharing progress between your own devices: design

Date: 2026-10-21. Status: **approved by the user on 2026-10-21**, with the calls below confirmed. Where this differs from the unstarted [device-sync.md](device-sync.md) (its goal, transports and "never third-party services"), this design wins; the rest of that plan stays as background for live session sync later.

Diagram: the "Sync Streams Map" artifact (work desktop, home laptop, Pixel 7, Pixel Watch and the six streams between them).

## Why

The user is putting AccountabiliBuddy on a **work desktop**:
- it has admin rights and a camera-less setup;
- it is always on the office network (Ethernet, rarely off);
- it has **Google Drive for desktop signed in to the user's personal account**.

On that machine they want focus session tracking, the on-screen window, the noise player and spotting distracting sites. They want to keep almost everything else too, above all **shared progress across all their devices**:
- the home laptop (FABTZEPHYRUSG15);
- the Pixel 7;
- the Pixel Watch 3.

Today's link (ADR 14) can't reach that desktop:
- the PC only listens on networks Windows marks Private, and office networks are usually "Domain";
- a program accepting connections on a company network is what IT flags;
- the phone's Wi-Fi at the office is usually kept apart from the desks' Ethernet.

So progress has to travel by routes that need no incoming connection.

## Goals

1. **One running log of focused time across all devices** (the user's words: "a synchronized log of our focused time across all our devices"). Every device's History, day bar, watch rings and totals include the sessions from every device, each marked with where it ran ("work PC").
2. **The day plan travels both ways**, to and from the work desktop.
3. **Works on a locked-down work computer.** On the work desktop, AccountabiliBuddy itself opens no connection and listens for none.
4. **Several methods at once, each a switch.** Every method can be turned on or off per device, now or later. The setup presets (a later project) only choose the starting switches.
5. **The watch shows work sessions during the workday.** It may lag by up to the chosen interval (fastest about a minute).

## Non-goals (for this design)

- Starting or ending a session on one PC from another. Live control stays on today's network link, phone ↔ PC.
- Blocking on other devices. Each device keeps enforcing its own.
- Phone-to-phone linking. It comes later on the same model.
- USB file transfer and an internet relay. They are designed for (see "Methods") but not built in this pass (my call).

## Anti-goals (failure even if it "works")

- Google, the employer or anyone with access to the Drive folder can read anything. **Everything that leaves a device is end-to-end encrypted.**
- Window titles, full addresses, OCR text, early-end reasons or notes on added sessions leave a device (ADR 8, 15).
- A sync silently throws away a day plan edit.
- A device keeps sharing history after Activity history was turned off there (ADR 15).
- Battery drain on the phone or watch from checking too often when nothing is happening.

## What travels

Each device writes **one sync file of its own**. No device ever edits another's.

| Part | Contents | Notes |
|---|---|---|
| Device card | device id, name, kind (PC, phone, watch), app version, written-at, which methods it uses | Feeds the Devices tab (project 2) |
| Progress | the device's own focus sessions (start, end, profile name, added by hand or not, omitted or not), drift stretches (times, app, site and its section) and day totals | Readable fields only (ADR 15); the last **14 days** (my call), so the week view and recent History fill in. Once received, a session is kept on the other devices like their own history, for 90 days |
| Day plan | the whole v5 plan, its version, its edit time, and the version it was based on | The clash rule below |
| Settings to share | profiles, sleep, day bar settings | Copied when a new device links ("Copy settings from…"), then **kept in step both ways** (my call; the user deferred this). Each receiving device applies a change by its own rules: a stricter profile change at once, a looser one only after its own looser-change delay (10 min by default) and never during a session, so syncing is never a way around the friction. Otherwise the newest edit wins. **Each device's Cold Turkey blocks stay its own** ("coldturkey.expected" never syncs), because blocklists can differ between devices (the user, 2026-10-21) |

- A device with Activity history **off** shares only its device card and day plan.
- **My call:** a snapshot file per device, not the old plan's signed event log. A reader replaces its copy of that device's part wholesale, so edits and deletions just work. It needs no clock merging, and a QR code can carry just today's part.

## Encryption

- **One sync key for all the user's devices.**
  - The first device makes it: 256 bits, AES-GCM, built into .NET and Android, so no library.
  - The key itself is kept under DPAPI on Windows and in the Android Keystore.
- **How a new device gets the key:**
  - **Phone:** scans it as a QR code.
  - **Home PC:** over the existing link.
  - **Work desktop** (no camera): the user **types a 40-character code** once, shown on the phone or another PC. The code is 200 random bits, and the encryption key is derived from it.

  The key never passes through Drive.
- **File names in the folder are opaque** (a random id). Device names are inside the encrypted contents.

## Methods

Every method is a switch per device, and nothing is sent before one is on.

| Method | Direction | When | Builds on |
|---|---|---|---|
| **Network link** (ADR 14) | phone ↔ PC, same network | live while the phone app is open | Unchanged. It now also carries the **other devices' sync files**, so the phone can bring the work desktop's day home to the laptop and back: the phone as carrier |
| **Sync folder** | both ways, every device that has it | **Instant** (on every change) / **1** / **5** / **15 min** (default 15) and always when a session ends | PCs: plain files in a folder the user picks inside Google Drive for desktop; AccountabiliBuddy opens no connection |
| **Sync folder on the phone** (optional backup, off by default) | both ways | same choices | See "The phone and the Drive folder" below: how the phone reaches the folder is decided by a test first. Faster than 15 min only while the pinned timeline notification runs (Android limits background work otherwise) |
| **QR code** | PC → phone | Quick button (main window and the on-screen window's Ctrl+Alt details) and an end-of-day reminder | Today's part of the PC's file as `accountabilibuddy://sync?…` links, so the phone's own camera app opens the companion. **No library on the phone**; the PC already draws QR codes (QRCoder 1.8.0). Two or three codes in a row when the day is busy |
| **QR picture import** | phone → PC | When the user shares the phone's code image to the PC and imports it (or drops it on the window) | Needs a **QR drawing library on the phone** and a **QR reading library on the PC** (two new dependencies, the user's OK) |
| **Sync file share** | either way | The phone's "Share sync file" / the PC's "Import sync file…" | The same file without a picture, no library |
| USB file transfer *(later)* | both ways | When the phone is plugged in, in File transfer mode | Windows Portable Devices; often blocked at work |
| Internet relay *(later)* | both ways | live | Its own ADR |

### The phone and the Drive folder (to be settled by a test first)

The PCs only see a plain folder. The phone can't do that: the Drive app on Android doesn't keep folders on the phone, and doesn't offer its folders to other apps' folder pickers (my understanding, to be checked). That leaves two ways:

| Way | How | Catch |
|---|---|---|
| **A. The phone app talks to Drive** | A one-time Google sign-in (Play services' authorization client), then Drive's web API over HTTPS for that one folder | Reading files that Drive for desktop created needs a "restricted" Drive permission. For a personal app that isn't verified by Google, that means the "Testing" status, where my understanding is the permission lapses after 7 days, so the phone would ask you to sign in again about weekly. It also needs a Google Cloud OAuth client for the phone (the PC's Calendar setup already has a project) and an ADR |
| **B. A sync app keeps the folder on the phone** | e.g. Autosync for Google Drive mirrors the folder into the phone's storage; AccountabiliBuddy reads and writes it through Android's folder picker. No Google sign-in in AccountabiliBuddy | A third-party app; how often it syncs depends on that app (and possibly its paid tier) |

The first task of the plan is a short test of both on your Pixel 7, with your OK to install a throwaway build. The design then records which one is built. Either way the setting stays an optional backup, off by default.

### The end-of-day QR reminder (decided)

- **When:** when the day plan's last Work part ends; the time can be adjusted in Settings.
- **Text:** "Scan today's code to sync your phone".
- **Buttons:**
  - **Show code:** opens the code full size.
  - **Later ▾:** Windows' own snooze list (5 min · 15 min · 30 min · 1 h). The default (15 min) is set in Settings.
- **Skipped** when the phone already has today's part by another method, or when nothing changed since the last scan.
- **Only** when QR is one of the ticked methods.

### The watch at the office (decided)

With the phone's sync folder on, the phone picks up the work desktop's file at the chosen interval and merges it into what it sends the watch (ADR 21). So the watch's rings and Session page include work sessions during the day.

## Clashes

- **Focus progress never clashes.** Each device owns its sessions.
- **Day plan changed on two devices before they synced (decided by the user):**
  - Shows a popup.
  - It lists both edits with their times and what changed, as the phone's dialog does (ADR 19).
  - **Keep the one I was using** / **Use the latest**.
  - Ignored or closed: **the latest edit wins**.
  - "Latest" is the newest edit time (my call).
  - A plan arriving during a focus session waits until it ends, since the plan is locked during focus (my call).

## Where devices stand (hook for the Devices tab)

Every device remembers, for each other device:
- when that device last wrote;
- by which method its file arrived;
- the newest data it has from it.

The Devices tab (project 2) shows this and marks a device as **behind** when its file is older than its interval plus a margin.

## Constraints

- **New dependencies need the user's OK, at versions at least two weeks old.** This design needs at most three:
  - a QR drawer on the phone and a QR reader on the PC, both only for picture import;
  - Google sign-in on the phone, for the optional phone sync folder.

  Encryption, compression (Deflate) and QR drawing use what's already there.
- **Core stays free of OS code (ADR 4):** the sync file format, merge and clash logic live in Core. The folder watcher, Drive web calls, QR screens and notifications live in Platform and App.
- **Decision records:**
  - a new ADR amends the "Local only" hard rule: progress may go to the user's own devices through files they carry or a cloud folder they choose, end-to-end encrypted;
  - a second records the phone's Google sign-in.
- **Battery:** checking faster than 15 minutes only while a session runs on some device, or while the timeline notification runs.

## Left to the builder

- the file layout and schema version;
- the QR chunk size and how a multi-code scan is collected;
- how received sessions are stored next to local ones (e.g. a "from device" column);
- the folder-watcher details;
- how today's `focusDay` on the phone becomes one more input to the merge.

## Open questions for the user

None left. The phone's way to the Drive folder is settled by the plan's first test.
