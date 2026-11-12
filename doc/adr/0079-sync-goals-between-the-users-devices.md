# 79. Sync goals between the user's devices

Date: 2026-11-12

## Status

Proposed

Amends the hard rule "The phone's Stats tab" in `doc/hard-rules.md` (phone-measured data never synced), limited to a goal's derived day value.

## Context

UI round 2 (`doc/design/round2/`, brief §5) adds Goals: user-made areas, goals of four kinds (habit, amount, time, milestone), each with an icon, colour, target, sources, notes, reminders and a weekly review. Until now the only goal was the single daily focus target (`GoalSettings.DailyMinutes`, document `goal`), which ADR 29 shares with paired devices. The owner wants the same goals on the PC and the phone: a goal created on one should appear on the other, ticks and hand logs made on either should count, and a steps or screen-time goal measured on the phone should show its progress on the PC.

The hard rules forbid syncing anything new without an ADR, treat names and notes as private text (ADR 24, 48, 50), and say the phone's Health Connect and usage data is read on the phone and never synced. The watch's snapshot is limited by ADR 21. The sealed per-device sync files (ADR 22) and the item-merge machinery of ADR 48/72 (stable ids, edit stamps, positions, 30-day tombstones, newest edit wins), extended field by field for tasks in ADR 76, already carry the user's private lists between their own devices.

## Decision

Goals, areas, goal entries, goal reminders and weekly review records travel between the user's own devices as new optional shared lists in the sealed sync files (`goals`, `goalAreas`, `goalEntries`, `goalReminders`, `goalReviews`), merged item by item with the ADR 48/72 machinery: stable ids, edit stamps, positions and tombstones, newest edit wins, with a hand edit of a day's value always winning over a later synced reading. Names, area names, notes, reminder reasons and review notes are private text: DPAPI-sealed at rest on the PC and Keystore-sealed on the phone, they leave a device only inside the sealed sync files and are never logged. Per-device UI state (collapsed areas, list width, the chosen list view) is not synced. QR files carry none of these parts. Older apps ignore the unknown parts; a newer app reading a file without them keeps its own goals unchanged.

Phone-measured sources stay on the phone: Health Connect steps, sleep and weight records and the phone's usage records are never synced raw. For a goal that uses one of them, the phone syncs only the goal's derived day value (for example "steps 8,412 on 11/10", or "38 min of <app> on 11/10") as a goal entry with source HealthConnect or ScreenTime. This amends the phone's Stats rule for that one derived number per goal per day. The PC's Beeminder data reaches the phone only as the derived summary ADR 43 already sends. The Pixel Watch gets nothing new.

The built-in Deep work goal stays a view over the `goal` document for its daily target (ADR 29 unchanged); its weekly target and its goal record travel in the new `goals` part.

## Consequences

Goals look and count the same on every device, and the PC can show phone-measured goals. More private text travels in the sealed files (goal names, notes, reasons), with the same protection as task titles. A derived health or usage number per goal per day now leaves the phone inside the sealed files, which the user opts into by creating a goal with that source. Until this ADR is accepted, each device keeps its own goals and the PC shows phone-measured goals as "Measured on your phone". The Kotlin and C# merges must give identical results, as for tasks.
