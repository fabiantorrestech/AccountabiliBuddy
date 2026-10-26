# 39. Hold Cold Turkey's keys during AB's windows

Date: 2026-10-26

## Status

Accepted

Amends [37. Re-arm blocks by rules in the day plan](0037-re-arm-blocks-by-rules-in-the-day-plan.md) (its "locks are never changed")

## Context

The user keeps no lock on their Cold Turkey blocks, so during a focus session, Pre-Work or work hours they can open Cold Turkey and switch a block off. They asked (2026-10-26) for AccountabiliBuddy to hold the "keys" to the blocks it enforces in those windows, with one hard but always-working way out in case AccountabiliBuddy malfunctions. ADR 37 let the app only switch blocks on and off and said a blocker's locks are never changed. Cold Turkey's settings file can't be edited (it rewrites it from its own copy), but its command line can lock: tested on Cold Turkey 4.9 the same day, `-start "<block>" -lock <minutes>` locks a block until a set minute (a longer lock extends it, a shorter one is ignored, and with the block's "unblock when lock ends" off it stays on, unlocked, afterwards), `-start "<block>" -password <p>` locks it until `-stop "<block>" -password <p>`, a locked block can't be switched to another lock or stopped without its key, and one block holds one lock at a time. Cold Turkey keeps the password in its own settings file under light scrambling, so a determined user can find it: the lock is a strong speed bump, not a vault.

## Decision

During the windows the user gives it (focus sessions they start, Pre-Work, work hours, chosen day plan categories), AccountabiliBuddy locks the Cold Turkey blocks the user assigned, through Cold Turkey's command line only; its settings file is still only read. Two kinds of lock: a rolling timed lock (renewed every minute to 10 minutes ahead, never longer, so it lapses within 10 minutes of the app stopping), the default for Pre-Work, work hours and categories; and a password lock with the app's own password, the default for focus sessions, ended with that password when the window ends. The password is random, kept DPAPI-encrypted in the app's documents, and shown to the user once as a paper spare key that they must retype. That spare key is the one way out: the app's Emergency release takes it (typed, no pasting) and releases every held block for the day; with the app broken, the user types it into Cold Turkey, and timed locks lapse on their own. A spare key used outside the app (a password-held block found off during its window) releases that block for the day, and either use makes a new password and a new spare key. A block with a lock the user set themselves isn't held. ADR 37's re-arm on the PC uses the same command line (`-start`, `-stop` only for unlocked blocks).

## Consequences

Switching a held block off during an AccountabiliBuddy window needs the spare key or, for timed locks, quitting the app and waiting up to 10 minutes. The app now changes Cold Turkey's lock state, within those windows only, and a bug could keep blocks on longer than meant: timed locks bound that to 10 minutes and the spare key covers password locks. The spare key on paper is the user's to keep safe; it also travels in a backup made with secrets. Cold Turkey Pro is needed for password locks, and the behaviour rests on Cold Turkey's undocumented command line, which an update could change.
