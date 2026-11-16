# 92. Control any own device's noise player through the sync files

Date: 2026-11-16

## Status

Accepted (owner, 2026-11-16, by approving the plan: "a small dropdown in the pc app and mobile app where if we press the volume toggle button (to reveal the volume slider), we can also see what devices (as they will now need to report to the sync), are playing white noise and not. And this will be able to toggle their white noise player play/pause from any other device."; rows hidden behind their own icon by default; no "play here, pause others" button.)

Amends [42. Control PC sessions and plan edits through sync files](0042-control-pc-sessions-and-plan-edits-through-sync-files.md) and [36. Control the phone's noise player from the watch](0036-control-the-phones-noise-player-from-the-watch.md).

## Context

The owner uses multipoint Bluetooth headphones with a PC and a phone. Headphones of that kind switch to a second device only once the first one stops sending sound, and the noise player on the first device keeps the audio channel busy. To move the headphones, the owner has to walk to the first device and pause its player there.

ADR 42 (W 1) already lets the phone send `noise` actions to a PC through its sync file, and a PC's file already carries its player's state (`noise`: playing, shown name, volume, locked, at). Three things are missing. A phone's file carries no player state. A PC never sends actions. And a phone applies no actions sent to it. The only op that changes playing is `toggle`, which flips the wrong way when a command meets a state that changed while it waited for a sync.

## Decision

Every device's file may now carry the `noise` object, with the same fields and rules as the PC's. A phone writes it while a sound folder is set; its `at` is when its player last changed, so the file doesn't change at every write.

Any device may send a `noise` action to any other device of the group: phone to PC, PC to phone, PC to PC and phone to phone. The action keeps ADR 42's shape, its `acks` and `actionResults` answers, its 2-minute freshness from the time the target first sees it, and its 10-minute give-up on the sender's side. Two ops join `toggle`, `next`, `prev` and `volume`: `play` and `pause`, which say which way to go. A target older than them answers "unsupported", and the sender then says to update that device. A sender offers play and pause only to a PC at 0.1.147 or later, or a phone at 0.1.82 or later. A phone now applies the noise actions sent to it and answers in its own file, as a PC does.

The target applies them by its own buttons' rules. Pausing is always allowed. Playing is refused while the headphone lock blocks it ("Connect headphones on <device>"), or when the device has no sounds. Noise control is allowed during a focus session, since it never weakens enforcement.

While its player plays, and for 10 minutes after a noise command it applied, a phone syncs at its busy pace: Drive every 30 seconds, and the relay's push stream. A PC already polls every 10 seconds while noise plays.

Nothing new leaves the devices. The name is the sound's shown name, never a path, and it travels only inside the sealed sync files. There is no new connection.

## Consequences

The owner can pause a phone's or a PC's noise from any other device's volume popover, so the headphones can move without walking over. Play and pause don't flip the wrong way after a slow sync.

How long a command takes depends on how the target syncs. A PC playing noise sees it within about 10 seconds. A phone sees it within about 30 seconds through Drive, about a second through the relay, and only as fast as the folder app copies files through the sync folder. A phone that is playing noise syncs at its busy pace, which costs some battery and network, as the PC's fast poll already does.

A phone's file is now written when its player starts, stops, changes sound or changes volume, which is a few more writes. Older apps keep working. They ignore a phone's `noise`, and they answer `play` and `pause` "unsupported".
