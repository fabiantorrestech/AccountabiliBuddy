# 21. Add a Pixel Watch companion fed by the phone

Date: 2026-10-21

## Status

Proposed

## Context

The user wants a Pixel Watch app that sits alongside the phone and the PC and shows the running focus session, the time focused today and the day's timeline, drawn as rings rather than the straight bars the PC and phone use. They asked for a starting mockup to refine later and gave no further specifics.

The watch has no link of its own today. The project's hard rules keep everything local except a short list of connections, and ADR 14 makes the PC the hub with the phone as its only paired device. A watch app therefore raises two separate questions: where its code lives, and how its data reaches it.

The mockup was built before this decision, with only the libraries the phone app already pins (Compose, Material 3, Activity). Wear-specific libraries (Compose for Wear OS, Tiles, complications) and any link library need the user's approval first.

## Decision

The watch app is a Wear OS app in `android/wear`, a second module of the Android project, with the phone app's application id so Wear OS treats the two as one app. It shows three swipe pages: today on a 24-hour dial (the day plan as the outer ring, focus sessions and drift marks as the inner ring, a red now dot), the running session as one ring that fills as it goes, and the week as seven small rings. It is not standalone: everything it shows comes from the phone, which already has it from the PC over the ADR 14 link. The watch never talks to the PC directly.

How the phone reaches the watch is left open. There are three options:

- **Wearable Data Layer** (`play-services-wearable`, recommended). This is the standard path. It uses Bluetooth or Wi-Fi through Google Play services, and the phone only has to send a small snapshot (plan parts, focus times, running session, week totals). The open question is whether Google relays data through its own servers when the two devices are apart. Synced data items may do so. If they do, that would breach the local-only rule, so the app should use direct messages only, and that behaviour has to be checked before this ADR is accepted.
- **The watch joins the ADR 14 link itself** over Wi-Fi to the PC. This needs no Google services, but watch Wi-Fi is often off and drains the battery, and pairing by QR code doesn't work on a watch screen.
- **A plain Bluetooth socket** between the phone and watch apps. This needs no new library, but the pairing, reconnection and power handling that the Data Layer already provides would all have to be written by hand.

## Consequences

The mockup can be built, refined and tried on an emulator or the watch now, on sample data, without touching the phone or the PC. Choosing the Data Layer adds a Google Play services dependency on both the phone and the watch, and it needs the cloud question settled first. Tiles and a watch-face complication (a ring on the watch face) would add further Wear libraries later. Whichever link is chosen, the watch receives only what the phone already holds, never window titles, URLs or reasons, so the privacy rules stay the same. The project CLAUDE.md's list of allowed connections must name the link once this is accepted.
