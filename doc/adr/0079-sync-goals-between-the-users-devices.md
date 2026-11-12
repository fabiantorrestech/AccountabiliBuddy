# 79. Sync goals between the user's devices

Date: 2026-11-12

## Status

Accepted (owner, 2026-11-14: "we want our goals synced across devices")

Amends the hard rule "The phone's Stats tab" in `doc/hard-rules.md` (phone-measured data read on the phone and never synced), limited to a goal's derived day value. Amends [76. One task list with bins, labels and tags](0076-one-task-list-with-bins-labels-and-tags.md) by adding an optional goal link to a task.

## Context

UI round 2 (`doc/design/round2/`, brief §5) adds Goals: user-made areas, goals of four kinds (habit, amount, time, milestone), each with an icon, colour, target, sources, notes, reminders and a weekly review. Until now the only goal was the single daily focus target (`GoalSettings.DailyMinutes`, document `goal`), which ADR 29 shares with paired devices and lets only a PC set. The owner wants the same goals on the PC and the phone: a goal created on one should appear on the other, ticks and hand logs made on either should count, and a steps or screen-time goal measured on the phone should show its progress on the PC. Milestone goals and the "a task carries this goal" source need a task to point at a goal.

The hard rules forbid syncing anything new without an ADR, treat names and notes as private text (ADR 24, 48, 50), and say most of the phone's Health Connect and usage data is read on the phone and never synced (the daily step count is the existing exception). The watch's snapshot is limited by ADR 21. The sealed per-device sync files (ADR 22) and the item-merge machinery of ADR 48/72 (stable ids, edit stamps, positions, 30-day tombstones, newest edit wins), extended field by field for tasks in ADR 76, already carry the user's private lists between their own devices.

## Decision

Goals, areas, goal entries, goal reminders and weekly review records travel between the user's own devices as new optional shared lists in the sealed sync files (`goals`, `goalAreas`, `goalEntries`, `goalReminders`, `goalReviews`), merged item by item with the ADR 48/72 machinery: stable ids, edit stamps, positions and tombstones, newest edit wins, with a hand edit of a day's value always winning over a later synced reading. They follow the "Sync task lists" switch and are part of the task-lists backup kind (ADR 71). Names, area names, notes, reminder reasons and review notes are private text: DPAPI-sealed at rest on the PC and Keystore-sealed on the phone, they leave a device only inside the sealed sync files and are never logged. Per-device UI state (collapsed areas, list width, the chosen list view) is not synced. QR files carry none of these parts. Older apps ignore the unknown parts; a newer app reading a file without them keeps its own goals unchanged.

A task may carry an optional `goalId` (a field-stamped task field under ADR 76) for the "a task carries this goal" source and milestone goals; old phones that don't know it never clear it.

Phone-measured sources stay on the phone: Health Connect steps, sleep and weight records and the phone's usage records are never synced raw. For a goal that uses one of them, the phone syncs only the goal's derived day value (for example "38 min of <app> on 11/10") as a goal entry with source HealthConnect or ScreenTime. This amends the phone's Stats rule for that one derived number per goal per day. Focus-based goal progress needs nothing new: each device counts it from the sealed session data it already receives. The PC's Beeminder data reaches the phone only as the derived summary ADR 43 already sends. The Pixel Watch gets nothing new.

Forward compatibility: a device that reads a goals part written by a newer app keeps any field it doesn't know as raw JSON and writes it back unchanged, and skips a single rule, entry or item whose enum value it doesn't know (keeping it as raw JSON) instead of refusing the whole list, so a newer device never locks an older one out of its goals or has its data stripped on the older device's next write. This lands in both the C# and Kotlin readers before the parts are synced.

Screen-time sources (owner, 2026-11-14) name the devices allowed to count them (one, several or all of the user's AB devices). Each chosen device, PCs included, syncs only its own derived day value for that goal as a goal entry marked with its device id; raw usage records, app lists and activity history never leave a device. The receiving device adds the chosen devices' day values. Beeminder entries read on a PC under ADR 84 travel the same way, as derived day values with source Beeminder, so the phone and other PCs count a Beeminder-linked goal without a token of their own; only the PC that reads Beeminder writes them.

The built-in Deep work goal stays a view over the `goal` document for its daily target (ADR 29 unchanged: set on a PC only); its weekly target and its goal record travel in the new `goals` part. A lowered Deep work target that arrives from another device takes effect on the receiving device only after that device's own looser-change wait and never during a session there (outside PB). Guards, the goal unlock and `focusDay` keep reading counted focus only; goal counts never feed a guard.

## Consequences

Goals look and count the same on every device, and the PC can show phone-measured goals. More private text travels in the sealed files (goal names, notes, reasons), with the same protection as task titles. A derived health or usage number per goal per day now leaves the phone inside the sealed files, which the user opts into by creating a goal with that source. Tasks gain one optional synced field. Until this ADR is accepted, each device keeps its own goals, task goal links and milestone task lists are "Coming soon", and the PC shows phone-measured goals as "Measured on your phone". The Kotlin and C# merges must give identical results, as for tasks.
