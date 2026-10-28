# 51. Dock the session overlay over the taskbar

Date: 2026-10-28

## Status

Accepted

## Context

The on-screen session window floats in a screen corner today. It dodges the apps it would cover, and holding Ctrl+Alt reveals more. The Claude Design redesign (boards 10a–16b, final spec in 15a–15c) moves it onto the Windows taskbar, right of the app icons and ending where the tray starts. It also adds:
- a Ctrl+Alt panel that rises from it like a Windows flyout;
- a corner clock when an app is full screen;
- a vertical tile when the taskbar sits on a side.

The design tested the corners against browsers, code editors and the desktop (10a) and dropped them: they collide with tabs, title buttons and scrollbars.

Windows 11 has no public API for putting a widget in the taskbar. Deskbands are gone, and taskbar internals change between builds.

## Decision

We will draw the overlay as our own borderless, topmost, no-activate tool window. It is placed over the taskbar's free area, never inside the taskbar process.

A Windows adapter in `Platform.Windows`, behind a Core port, reports where it may go:
- the taskbar's edge, bounds and auto-hide state, from `SHAppBarMessage`;
- where the tray and the app icons end, from UI Automation on the taskbar, with a safe fallback width when they can't be read;
- whether the foreground app is full screen.

It recalculates on these events, never by polling:
- `ABN_*` appbar notifications;
- display and DPI changes;
- taskbar setting changes;
- foreground changes.

The same adapter serves:
- the floating placements;
- the side-taskbar tile;
- the click-through corner clock.

The Ctrl+Alt panel is a separate flyout window anchored to the overlay. The existing escalation and session logic is unchanged; only where and how it is drawn changes.

## Consequences

The overlay sits where the user already looks for the time, and it stays off app content.

It depends on taskbar geometry the app reads from outside. A Windows update that moves the tray or icons can misplace it until the fallback kicks in or the adapter is fixed, so the adapter keeps every guess in one place.

Watching appbar and display events adds a little event-driven work and no timers.

Going back to the floating corner window stays possible: it is one of the placements.
