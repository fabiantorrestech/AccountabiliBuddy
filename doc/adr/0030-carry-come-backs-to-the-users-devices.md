# 30. Carry come-backs to the user's devices

Date: 2026-10-23

## Status

Accepted

## Context

Come-backs are thoughts that must not drift: parked with Ctrl+Alt+N, the work-hours gate or Focus mode's capture line, and listed on the PC's Today until ticked. The approved design (section F) lists them "on Today on the PC and the phone until ticked" and says they travel in the sealed sync files "like task names (ADR 24)", with an ADR extending ADR 24. Until now they stayed on the PC (document "capture.comebacks", DPAPI-encrypted). ADR 24 already lets guided-session task names go to the user's paired devices over the ADR 14 link and in the ADR 22 sync files, and nowhere else. Notes, the other half of capture, are drift-OK and have no reason to leave the PC.

## Decision

We will send the PC's open come-backs (an id, the text and when it was parked; ticked ones never) to the user's paired devices in the link's state message and in the PC's sealed sync file, as optional fields, under the same rules as task names in ADR 24: only to the user's own paired devices and sync files, sealed in transit and at rest on the phone, never to the watch. The phone shows them read-only on Today under "Come back to". Ticking or adding one from the phone waits for the action list in the sync files (redesign phase 3), so the PC stays the only place that changes them. Notes are not sent.

## Consequences

A thought parked at the PC is visible on the phone on the way home, which is where it is most likely to be acted on, without opening any new connection. More typed text now leaves the PC, though only to the user's own devices and only as long as it is open; a device on an older version ignores the fields. The phone can't tick a come-back yet, so a done one stays listed there until it is ticked on the PC. Reversing it means dropping one optional field from the state message and one from the sync file.
