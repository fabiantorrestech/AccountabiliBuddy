# 15. Share activity history between paired devices

Date: 2026-10-20

## Status

Accepted

## Context

ADR 8 stores activity history with readable totals (times, app, site domain, classification, session context) and encrypts the details (window titles, full URLs, OCR text, reasons). The project's hard rule (ADR 9) says activity data never leaves the device. The user wants to see usage from every linked device on each of them, per device and combined (PC, phone, tablet), so that switching to another device during focus shows up as it happens and in History, and agreed on 2026-10-20 that times, app, site and on/off-track label may travel between their own paired devices on their Wi-Fi, encrypted, while titles, full addresses and typed reasons may not. The link itself is ADR 14.

## Decision

We will let each device send the readable part of its activity history, as ADR 8 defines it (start and end times, app or process name, website domain, classification, profile, whether it was during focus, and on the phone the chosen check-in answer), to the user's paired devices over the ADR 14 link only, and keep everything ADR 8 encrypts on the device where it was recorded. The PC stores what it receives in its activity history with the sending device's id (a new column, where empty means this PC) under the same 90-day retention and the same Setup controls: turning history off or deleting it also stops sending and removes received history. The phone keeps its own and the received segments in its private storage for 90 days. History on every device can show one device or all of them combined. Nothing is sent to any other destination.

## Consequences

Device switching during focus becomes visible on both sides, and History answers "how much was I on my phone during focus" without guessing. The local-only rule changes from "never leaves the device" to "never leaves the user's paired devices", and the CLAUDE.md hard rule must say so. A paired device that is lost or stolen holds up to 90 days of readable history from the others, so unpairing must also delete what that device received, and the phone's storage stays in its private app folder. The PC's database gains a migration (device id column), with the usual copy before it. Encrypted details staying put means a phone can't show the PC's window titles, which is the intended limit.
