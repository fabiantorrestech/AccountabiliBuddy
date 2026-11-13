# 85. Run focus sessions on the phone

Date: 2026-11-14

## Status

Accepted (owner decisions, 2026-11-14)

Amends [42. Control PC sessions and plan edits through sync files](0042-control-pc-sessions-and-plan-edits-through-sync-files.md) (the phone's Start no longer always goes to a PC) and the hard rule "Start shows only when no linked PC runs a session and always goes to the controlled PC" in `doc/hard-rules.md`. Builds on [68. Add a synced companion mode that quiets guarding](0068-add-a-synced-companion-mode-that-quiets-guarding.md), [20. Keep hand-added focus sessions in the activity history](0020-keep-hand-added-focus-sessions-in-the-activity-history.md) and [22. Share progress between your own devices through sealed files](0022-share-progress-between-your-own-devices-through-sealed-files.md).

## Context

Every real focus session runs on a PC. The phone can only ask a PC to start one (a `start` action in its sealed file, ADR 42), and its own test session is never counted or synced. The owner, planning from ProductaBuddy's side, wants to start a session from the phone with or without a PC, have it count and show on the other devices, and choose what the phone enforces during it. The phone already has the parts such a session needs: the Today session card and sheets (round 2 R2b), the This session list on the new task model (R7), the noise player, LockMyPhone-Extended's companion hold (ADR 73, on branch `lmp-lock`) and Curbox hard mode, phone usage and pickup readings, and the trusted network clock (ADR 70). Focus counting on every device already reads whatever session pieces arrive in the sealed files, without a device-kind filter; only "is a session running" checks look at PCs alone.

## Decision

The phone runs real focus sessions of its own, in both AccountabiliBuddy and ProductaBuddy mode (owner, 2026-11-14).

- **Where it runs.** The phone's Start sheet offers "On <PC>" and "On this phone". When a PC is online the default is the PC (owner); with none, the session runs on the phone. The choice is remembered.
- **One at a time.** While any device in the group runs a session, Start on every other device shows "Running on <device> · 32 min left" instead. If two devices start offline at the same moment, both sessions are kept, and overlapping minutes count once in Stats, goals and focus totals (the per-device pieces are joined as one timeline before counting).
- **What it does on the phone.** The ring timer, the This session tasks with Done, Step away, Pause timer, Add 10 minutes and End; the noise player; and, per session profile, the enforcement the owner chose. A phone session profile names which apps are usable during the session and which count as pickups or distraction, which LockMyPhone-Extended profile to hold (through the companion hold of ADR 73 once that branch is merged; until then the phone's existing LockMyPhone-Extended session lock), and which Curbox blocks to turn on. The profiles and their app lists stay on the phone.
- **Rules by mode.** AccountabiliBuddy mode keeps the "no escape hatches during focus" rules on the phone: ending early needs the wait and a reason, and Plan, Settings and setup revokes are locked; time comes from the trusted clock. ProductaBuddy mode ends at once and locks only what the chosen profile turns on, as ADR 68 already does for PC sessions.
- **What syncs.** The phone writes its running session (`running`) and its finished session pieces (`sessions`) in its own sealed file, in the same shapes a PC writes, with the session's tasks and labels as the PC sends them; it never writes window titles, URLs, app names or the profile's app lists. Other devices show the phone's session ("Session on Pixel · 32 min left" in the PC title bar and on Today; the watch shows the timer within its limited snapshot, ADR 21) and count its minutes like a PC's. The phone's pickups and distraction minutes during the session are its own data (the phone's Stats rule) and do not leave the phone, except as a derived number a goal asks for under ADR 79.
- **Control.** The phone controls its own session directly; a PC does not control a phone's session in this ADR. The test session stays as it is: never counted, never synced.

## Consequences

The phone becomes a full session device, so a user without a PC nearby still gets counted focus, and PB users get a light, phone-first way to work. Session pieces now arrive from phones as well as PCs: every "is a session running" check and the earned-time unlock's "other devices' sessions" must treat a phone's session like another PC's, and overlapping sessions must be joined before counting. A phone session in AccountabiliBuddy mode is as binding as a PC one, which is new for the phone and must be covered by the same checks (no apply-now, trusted time, refused setting changes). The phone's enforcement depends on the companion apps the user installed; without them a phone session is a timer with tasks.
