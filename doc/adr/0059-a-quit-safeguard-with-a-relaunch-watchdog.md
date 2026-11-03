# 59. A quit safeguard with a relaunch watchdog

Date: 2026-11-03

## Status

Accepted

Amends [39. Hold Cold Turkey's keys during AB's windows](0039-hold-cold-turkeys-keys-during-abs-windows.md) and [41. Get a new spare key with friction](0041-get-a-new-spare-key-with-friction.md) (the spare key gets a second use)

## Context

AB guards only while it runs, and until now the tray's Quit ended it at any time outside a session's friction. Ending it from Task Manager was easier still: a killed app resumes its session only at the next launch, which may be the next sign-in. The user asked for an optional safeguard like Cold Turkey's, off by default, that can hold during chosen day plan categories, all the time, or during a session, each chosen separately. No official Cold Turkey documentation was found; third-party listings and reviews say it resists quitting during a block and can block Task Manager, so that behaviour is taken as the rough model and not as a specification.

Any answer has to keep Windows shutdown, sign-out and app updates working, must not trap the user's PC if the app faults at startup, and must stay cheap when idle. There is no service or elevated part in this app, and nothing here may add a dependency or an outbound connection.

## Decision

We will refuse the tray's Quit while the safeguard's conditions hold, and run a hidden second copy of the app (`--watchdog`) whenever the safeguard is on, which relaunches AB if it is killed while locked. The watchdog waits on the app's existing single-instance mutex and two named events (locked, stand down), so it costs no CPU while waiting and needs no process id or state file. It relaunches only if the app holds the locked state, nothing asked it to stand down and Windows isn't shutting down, at most three times in ten minutes. Every clean exit, update and uninstall signals stand down first, so the watchdog can't hold the executable open or fight an exit. The app restarts the watchdog if it is killed.

We chose this over blocking Task Manager. A Windows policy that disables it would stay in force if AB crashed, would touch a system tool the user may need for other reasons, and still wouldn't stop `taskkill`. We also chose it over a Windows service, which would need elevation and a much larger install change for a personal tool.

The settings are device-local (documents `quit.guard`, `quit.guard.pending`), neither synced nor backed up, so restoring a backup can't undo them. They follow the existing change rule: stricter parts apply at once, looser ones wait the looser-change delay and only apply with no session running, and all of it is locked during a focus session. Leaving "All the time" also needs the spare key typed before that wait starts, and ticking it needs a confirmed spare key, so hardcore can't be entered with no way out. Typing the key doesn't replace it, since that would disturb Cold Turkey's locks; a lost key uses "Get a new spare key" (ADR 41). While the safeguard is on, starting at sign-in is kept on and its switch is disabled.

## Consequences

Quitting during a chosen window now takes real effort, and a killed app comes back within seconds and resumes its session instead of waiting for the next sign-in. The cost is a second idle .NET process while the safeguard is on, and more exit paths that must signal stand down: a new way of ending the app has to do so too, or it would be relaunched.

This is friction and not a security boundary. Killing both processes quickly, the Task Manager Startup tab, safe mode and uninstalling all still get past it, and the settings say what it does. The spare key now has two uses, so a key written down for Cold Turkey also opens hardcore mode. The single-instance check had to change from "the mutex is new" to "the mutex can be taken", since the watchdog keeps a handle on it.
