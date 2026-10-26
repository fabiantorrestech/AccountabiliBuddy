# 37. Re-arm blocks by rules in the day plan

Date: 2026-10-25

## Status

Accepted

## Context

The user turns their blockers off now and then (Curbox on the phone, Cold Turkey on the PC) and forgets to turn them back on. They asked (2026-10-25) for AccountabiliBuddy to notice when a block is off and turn it back on at a set time (midnight by default), with rules that differ for workdays and days off, granular but easy, set as part of the day plan and through a day planner walkthrough. Until now the app only read Cold Turkey's state (never changing its settings) and Curbox's status. Curbox's public API (v2) lists its app blocker, keyword and grayscale groups and the keyword and reel blockers with their on/off state, and turns each on or off; it doesn't expose a group's own schedule. Cold Turkey's command line starts and stops a block by name without touching its settings file.

## Decision

A day template and a one-off day may carry block rules (`blocks`, profile schema v10): the blocks the day keeps on (all the device saw on when rules were first set, unless chosen), a re-enable time (midnight by default), a heads-up before blocks come back on (5 minutes, on by default, shown on the blocker's device), and lift windows (start, end, the blocks, and a mode: Allow, the default, where the user may turn them off themselves, or Auto, where the app turns them off at the start). Blocks come back on at the end of a lift window and at the day's re-enable time; a block turned off outside a window stays off until then. The rules travel with the day plan to every device.

Each device enforces the rules for its own blockers: the phone through Curbox's API (`SET_APP_BLOCKER_GROUP`, `SET_KEYWORD_GROUP`, `SET_GRAYSCALE_GROUP`, `SET_KEYWORD_BLOCKER`, `SET_REEL_BLOCKER`), each PC through Cold Turkey's command line (`-start`, `-stop` for an unlocked block). Only on/off is ever set: a blocker's settings, schedules and locks are never changed, and a block its blocker keeps off is left to it. Each device's sync card carries a `blockers` list (kind, id, name, on/off of its Curbox groups or Cold Turkey blocks; names only), so the rules editor and the walkthrough on any device can list every device's blocks.

## Consequences

Blocks the user turned off come back on by themselves, on the schedule the day plan says, with a warning first. AccountabiliBuddy now acts on the blockers instead of only reading them, within on/off. Curbox's own schedules still run inside a group, out of the app's reach. The schema bump means the PC and the phone update together. Block names now reach the user's other devices, inside the sealed files.
