# 68. Add a synced Companion mode that quiets guarding

Date: 2026-11-06

## Status

Accepted

## Context

AccountabiliBuddy watches the visible window on every PC monitor and escalates when the owner drifts (toast, overlay, minimize or redirect). On the phone it takes over the screen during work time with no session, shows Pre-Work and parked-phone notices, and from bedtime to wake time asks LockMyPhone-Extended for its Hardcore profile. The owner finds this guarding gets in the way. It isn't yet designed to work alongside the other tools they use, LockMyPhone-Extended in particular, and they have no better design for it yet. The rest of the app is what they want to keep: focus sessions, declaring the day (lunch, sessions), the sound player, the on-screen window, tasks and their sync, stats, steps, the focus goal, and the blockers AB drives (Cold Turkey on the PC, Curbox on the phone).

Two ways out were weighed: fork the project into a companion-only app, or add a mode to this one. A fork would share the sealed sync format, the Drive folder and the phone's application id with this app, so it would clash with it unless all of those were renamed. Every later fix to sessions, sync or tasks would also have to be made twice. The app already has the hook a mode needs: `EnforcementLimitService` caps escalation per device, and `EnforcementEngine.MaxLevel` can be `None`. The phone already has a master switch (its pause) that silences its nudges.

Turning guarding off is a looser change. Under `doc/hard-rules.md` it waits the looser-change delay and never applies during a session. The owner wants the choice made once and followed by every linked device, and wants a new device to start in the quiet mode.

## Decision

We will add a mode to this app, not fork it. One setting, `GuidanceMode` (`Guarding` or `Companion`), is app-owned: the `guidance` document on a PC and the `guidance` preferences on the phone. It is never part of the profile JSON, so the schema stays at v13.

In Companion mode:
- **PC:** it stops watching windows and sites for drift, which means no evaluation, no screen reading, no activity segments and no private-windows reminder. The work-hours guard's minimizing, gate and lights out, Pre-Work, the start nudge and the bedtime reminders are quiet. The start-of-day assistant may still greet the first arrival of a workday, when switched on for ProductaBuddy (ADR 69).
- **Phone:** it is quiet the same way its pause is, with no takeover card, start nudge, Pre-Work, parked notice, bedtime reminders (including ones already scheduled) or lunch hint. AB stops asking LockMyPhone-Extended for the bedtime-to-wake lock and shows no bedtime essentials screen.

Sessions still count, and the session's no-escape-hatch locks still hold. The Cold Turkey and Curbox blocks, the session locks AB asks LockMyPhone-Extended and Curbox hard mode for, the quit guard and Start shutdown all stay as they are.

The app shows itself as ProductaBuddy in Companion mode, and as AccountabiliBuddy in Guarding. This covers the window title, the title bar, the tray tooltip, the phone's header and notification titles, and any wording that names the app. The About tab keeps AccountabiliBuddy, the product's name. So do names that must match something outside the app: the browser extension, the Start menu shortcut, the Google consent screen, backup and sync file types, the releases page, the Android launcher label and the Windows notification sender. The code keeps `Guarding` and `Companion`, so the brand can change without touching the sync format.

Switching to Companion waits the looser-change delay and applies only with no session running. During a focus session it asks: after this session ends (the same wait, then no session running; for a session with no set end, when it is ended by hand), or now, which first costs what ending the session early costs (its wait, armed in the background once confirmed as in ADR 66, and a reason kept sealed on the device for 90 days), then waits the delay and applies with the session still running. A session that ends on command costs nothing extra, as ending it does. That paid "now" is the one exception to looser changes never applying during a session; it never travels, so another device still holds the mode until its own session ends. Switching back to Guarding applies at once, always.

The mode and its edit time go in each device's sealed sync file, and the newest edit wins, as with lunch. A device that receives Guarding applies it at once. A device that receives Companion applies it once its own looser-change delay has passed since the edit time (a stamp counting at most 2 minutes ahead), and holds it until any session there has ended.

A fresh install's first run offers Companion, preselected, with no wait, since nothing was guarded before. That default is stored with no edit time. Such a device never sends its mode and takes any mode it receives at once, so a new device joining a guarded group becomes guarded and never loosens the group. An install upgraded from before this decision reads as Guarding with a fixed edit time of 2000-01-01: it is sent, so a fresh device joining a group that was guarding all along follows it, and any real choice is newer and beats it. It is offered the choice through SetupFeatures, and choosing Companion there waits as usual.

## Consequences

The owner keeps the companion features without the guarding they find in the way, on every device, from one switch. The guards stay in the code and can be redesigned and turned back on one at a time later, which a fork would have given up. There is still one codebase, one sync format and one phone app.

While in Companion mode the PC records no per-app or per-site history and no drift, and Cold Turkey's block-page sightings stop. History, drift marks and stats built from them go thin, though focus-session totals do not. Every guard now has one more condition to respect, and a guard added later that forgets to check the mode would nudge in Companion mode; `doc/hard-rules.md` lists what the mode covers. The sync file gains one field, mode and time only. A device on an older version ignores it and keeps guarding. Reversing this decision means deleting the setting and its sync field, which is cheap. The real cost is in the gates spread across the guards.
