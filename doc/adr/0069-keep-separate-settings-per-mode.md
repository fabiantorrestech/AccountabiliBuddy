# 69. Keep separate settings per mode

Date: 2026-11-06

## Status

Accepted

## Context

ADR 68 added ProductaBuddy, a companion-only mode beside AccountabiliBuddy's guarding, switched for every linked device at once. The owner wants each mode to keep its own values where that makes sense. Examples: a calmer on-screen window, different sounds, or the start-of-day assistant off in ProductaBuddy while it stays on in AccountabiliBuddy. They also want a side-by-side view of the settings whose values differ between the modes, where they can make one side win everywhere ("Sync AB → PB", "Sync PB → AB") or pick left or right per setting, as when resolving a merge conflict, or cancel.

Settings live in about ninety app-owned documents on the PC, and in several preference files and one store on the phone. A few of them sync between devices: profiles, day plan, sleep, day bar and goal. Doubling every setting would double the storage, the sync format and the Settings pages on both platforms. Most settings (profiles, the day plan, sleep times, tasks, sync, connections) have no reason to differ by mode. The owner and Claude went through the settings one by one.

## Decision

We will keep three kinds of setting:
- **Shared:** one value.
- **Per-mode:** an AccountabiliBuddy value and a ProductaBuddy value.
- **AccountabiliBuddy-only:** hidden while ProductaBuddy is in effect, because nothing in that mode reads them.

The list is recorded in `doc/hard-rules.md`.

**Per-mode settings:**
- **PC:** the start-of-day assistant, the hyperfocus reminder, the time-up sounds, play when headphones connect, the on-screen window (timer, timeline, overlay page), and appearance (theme, light or dark, colour).
- **Phone:** the timeline notification, play when headphones connect, and appearance.

Every per-mode setting is device-local, as it was. Nothing that syncs today becomes per-mode, so the sync format and the profile schema do not change. Bedtime reminders, which sync, become AccountabiliBuddy-only.

**Storage:**
- **PC:** a per-mode document keeps ProductaBuddy's copy under the same key plus `.pb`. Its service reads and writes the active mode's copy through `ModeSettings`, and reads it again when the mode switches.
- **Phone:** the values in effect stay where they lived. On a switch they are put aside for the mode left and the other mode's are put in (preferences "modeSettings").
- **First use:** ProductaBuddy's values start as a copy of AccountabiliBuddy's, except the start-of-day assistant, which starts off.

**Compare view (PC and phone):**
- It lists only the per-mode settings whose two values differ.
- "Sync AccountabiliBuddy → ProductaBuddy" and the reverse make every row agree.
- A left or right pick per row makes both modes keep that value; rows left unpicked stay as they are.
- Cancel changes nothing.

The mode switch sits at the top of Settings on both apps, with the compare view beside it.

## Consequences

Each mode can look and sound the way the owner wants, and switching mode carries those settings with it on every device. The guard settings no longer clutter ProductaBuddy's Settings. Keeping only device-local settings per mode avoids any sync conflict between two copies.

The cost is a second place each per-mode setting can live. A new per-mode setting must be added to the registry (`PerModeSettings` on the PC, `PhoneModeSettings` on the phone) and its service must read the active copy, or the compare view will not show it. On the PC, a per-mode document's whole content follows the mode, including parts the compare view does not list, such as the overlay's position and the stored place for sunset times. On the phone, a crash between putting values aside and putting the new ones in could leave the old mode's values in effect until the next switch. Turning a per-mode setting back into a shared one later means picking which copy survives.
