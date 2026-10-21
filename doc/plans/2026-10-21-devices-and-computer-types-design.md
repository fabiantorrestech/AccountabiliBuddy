# Devices tab and computer types: design

Date: 2026-10-21. Status: **approved by the user on 2026-10-21**, including the Work computer type's "notice and overlay, never minimize" and "titles and addresses not kept". It builds on [2026-10-21-device-sync-design.md](2026-10-21-device-sync-design.md) (the sync files, methods and key). Two parts, one per project the user named:
- the **Devices tab** on the PC and the phone;
- the setup assistant's **computer types**, with **link first**.

## Why

The user wants to install AccountabiliBuddy on a work desktop without granting more than it needs, while keeping as much as possible, above all a running log of focused time shared across all their devices. They asked for:
- **"Types" of setup** (default, local-only, a work computer…). Each is a preset of settings that can all be changed later, so someone who started "Local only" can still tick the sync folder.
- **Linking to another device at the very start**, so a new device inherits the settings before any work is repeated.
- **A Devices tab on both apps**, where they can see whether every device is up to date or behind.

## Part 1: the Devices tab

**Where**
- **PC:** a new **Devices** tab in the sidebar, after History. The Linked devices card moves out of Setup into it; pairing a phone, the watch and the PC's own sync methods all live there.
- **Phone:** a **Devices** tab in the bottom bar, before Settings. The PC card and Pixel Watch section move out of Settings into it.
- **Watch:** its "where the data comes from" page also lists the devices it heard about and how fresh each is (no actions).

**What it shows**
- **This device first**, then every device in the sync group, each with:
  - name and kind icon;
  - the methods it uses;
  - when its data last arrived here, and by which method ("work PC · 14 min ago · sync folder").
- **A state pill:**
  - **Up to date**;
  - **Behind:** older than that device's interval plus a margin, with how far behind;
  - **Waiting for first sync**;
  - **Unlinked**.

  The state is shown in words and colour, never colour alone.
- **A combined line:** "Today: 5h 10m focused across 3 devices".

**What you can do there**
- **Sync now:** writes the sync file at once, pushes over the link if connected, or shows the QR code, depending on the methods.
- **Show QR code / Import sync file or picture…** (PC); **Scan / Share sync file** (phone).
- **Methods:** a switch per method, each with a one-line plain description and its interval.
  - Methods are Network link, Sync folder (folder picker), QR code (reminder time, default snooze), Sync file share; USB and relay later.
  - Shown with their **risk order**, safest first, as the user asked: Local only → QR → sync folder → USB → relay → network link at work.
- **Link a new device:** shows this group's link code as QR plus the 40-character text, and a field to enter another device's code.
- **Unlink:** locked during a focus session, like other Revokes.

**Running log:** History on every device lists all devices' sessions with a device tag and a "This device / All devices" filter (default All). Totals everywhere add up across devices.

## Part 2: computer types in the setup assistant

**New first steps:** Welcome → **Link to your other devices?** → **What kind of computer is this?** → the existing steps (bring your blocks, usage, review, browsers, basics) → done.

**Link first**
- **"I have AccountabiliBuddy on another device"** → enter or scan the link code. The new device joins the sync group, copies profiles, the day plan, sleep and day bar settings, and the other devices' recent sessions arrive with the first sync. The "bring your blocks" import is then skipped by default, since the profiles came along.
- **"This is my first device"** → as today.
- **Skip** → can be done later in the Devices tab.

**The types** (each is only a starting set of switches; Settings and the Devices tab change any of them later; Settings → App & data shows "Set up as: Work computer · Apply a type again…")

| Setting | Default (personal) | Work computer | Local only |
|---|---|---|---|
| Focus sessions, on-screen window, noise player | On | On | On |
| Spotting distracting apps and sites (window titles in memory, browser extension) | On | On | On |
| Screen reading (OCR) | Off (asks) | **Off** | Off |
| Activity history | On | On, **titles and addresses not kept** (new option: only times, apps, sites and sections are stored) | On |
| Escalation when you drift | Notice → overlay → minimize | **Notice → overlay** (never minimizes or blocks work windows; my call) | Notice → overlay → minimize |
| Run at startup | On | On | On |
| Network link (others connect to this PC) | Offered | **Off** | Off |
| Sync folder | Offered | **On**, asks for the folder | Off (can be ticked later) |
| QR code to the phone | Offered | **On** | Off (can be ticked later) |
| Google Calendar sign-in | Offered | **Off** (iCal links stay available) | Off |
| Update checks (GitHub releases) | On | On | **Off** (manual updates) |
| Cold Turkey integration | Offered | Off | Offered |
| Media keys and global shortcuts | Off | Off | Off |

**"What this computer will do" panel:** after a type is picked, the assistant lists in plain words what the app will access on this PC, so the user can show IT if needed:
- "Reads the title of the window in front, to spot distractions; titles aren't stored";
- "The browser extension reads the address of the open tab, to spot distracting sites; only the site is stored";
- "Writes an encrypted file to the folder you chose, every 15 minutes";
- "Starts with Windows";
- "Makes no network connections of its own" (Work computer, when updates are off) or "Checks GitHub for updates" (when on);
- what it does **not** do: "Doesn't read your screen. Doesn't listen for connections. Doesn't send anything to the internet itself."

## Constraints

- Every preset value is an existing or newly added setting; a type stores nothing else. Changing a setting later never asks for the type again.
- "Titles and addresses not kept" is new: the recorder still uses them in memory for spotting distractions, but doesn't write the encrypted columns.
- The Devices tab's Unlink and method switches are locked during focus, like other Revokes and Settings.
- Wording follows the app's plain style, with no internal names.

## Left to the builder

- Icons, the exact pill colours (from the app's theme), how the PC's Setup tab looks once Linked devices moves out, and the phone's bottom-bar icon.

## Open questions for the user

None. The user confirmed the Work computer defaults on 2026-10-21.
