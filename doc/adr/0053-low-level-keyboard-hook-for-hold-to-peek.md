# 53. Low-level keyboard hook for hold-to-peek

Date: 2026-10-30

## Status

Accepted

## Context

The session overlay docked over the taskbar (ADR 51) opens its panel while the user holds Ctrl+Alt and closes it when they let go ("hold-to-peek"). Windows has no event for a modifier-only chord: `RegisterHotKey` needs a non-modifier key, so it cannot report Ctrl+Alt being held or released. The old on-screen window found out by polling the key state every 100 ms for as long as it was up, which goes against the rule that detection is event-driven and keeps CPU and battery low.

The only event-driven source for a held modifier from any app in front is a low-level keyboard hook (`WH_KEYBOARD_LL`). Such a hook sees every key the user types in every app, which sits badly with the rule that private text never leaves the device and is never kept beyond what the app needs. Windows also removes, silently, a hook whose callback is too slow once, and an exception thrown out of the callback ends the process.

## Decision

We will watch Ctrl+Alt with a low-level keyboard hook (`Win32CtrlAltWatcher`, behind the `ICtrlAltWatcher` port), and limit it so that it can only ever tell the app "Ctrl and Alt are both down" or "they are not":

The hook is installed only while hold-to-peek is on in the overlay settings and the overlay tile is on screen; turning hold-to-peek off, hiding the tile or quitting removes it. It is reinstalled at each session start and end, so a hook Windows dropped comes back. The callback looks only at whether a Ctrl or Alt key went down or up. It never stores, logs, syncs or forwards any key, and it passes every key on to the next hook untouched. Nothing inside it may throw out of the native callback. The panel's own letter shortcuts (Ctrl+Alt+D and so on) are ordinary registered hotkeys (`Win32PanelKeys`), registered only while the panel is open and released when it closes. They are not read through the hook.

## Consequences

Peeking is instant and costs nothing while no key is pressed: the 100 ms poll is gone. While a non-sticky peek is open, the panel still re-checks the key state once a second, so a release Windows never delivered (Ctrl+Alt+Del, a UAC prompt) cannot leave it stuck open.

The app now holds a system-wide keyboard hook part of the time, which security tools and careful users may notice. Its limits (Ctrl/Alt only, nothing kept, only while the tile shows) are recorded in `doc/hard-rules.md` and must be kept by any change to the watcher. A change that reads any other key through this hook needs a new ADR. If Windows drops the hook between session boundaries, hold-to-peek stops working until the next session start or end. The sticky panel shortcut (Ctrl+Alt+Space) still opens it in the meantime.
