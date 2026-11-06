# 66. Let a waited end run in the background once confirmed

Date: 2026-11-05

## Status

Accepted

## Context

Several ways out of a guard cost a wait and a written reason: ending a focus session early, ending the work block early from Start shutdown (ADR 65), the work-hours guard's "Stop for today" (ADR 27), Pre-Work's "No, day off", and the phone ending a PC's session (ADR 42). Each holds the user on the window until the countdown ends: the PC dialogs are modal and topmost and start the wait over when closed, "Stop for today" drops the request if it isn't confirmed within two minutes of the wait, and the phone's dialog doesn't even take the reason until the wait is over. With a long wait the owner sits and watches a timer instead of wrapping up their work.

Other waits in the app already run without a window: looser settings changes wait in the background and show "Change waiting · applies h:mm" on the overlay tile, and the phone's "Queue it" waits apply on their own. Those are safe because the stricter state stays in force until the change applies. The rules that still matter: nothing shortens a wait, a wait cannot be served ahead and spent later, and a session's escape hatches stay where ADR 25 put them.

Some waits are the guard themselves and must stay as they are: the work gate before a held app, the away card's +5, the phone's work-nudge takeover and the phone-down screen. Freeing the window there would free the thing it guards.

## Decision

We will let the five waited ends above be confirmed early: the reason is typed during the wait, and "Confirm · ends at h:mm" arms the end and closes the window. An armed end applies itself when its wait is over, through the same action the dialog used to take, with the reason given. It can be cancelled at any time, which is free because it is the stricter direction. It is never banked: there is no "do it later" after the wait, and an armed end whose occasion is gone (the session already ended, the work block is over) is dropped. The wait still starts when the window opens, so its length is unchanged. The old path of waiting in the window and pressing End stays.

On the PC, armed ends are kept in the `documents` table (`armedEnds`, the reason DPAPI-sealed), so a restart neither resets nor skips a wait, and time comes from `TrustedClock`. Every running wait shows in one place: the overlay tile's line (ahead of "Change waiting"), a Today banner with Cancel and the tray tooltip. The unlock-option wait and the Cold Turkey new-key wait, which already run in the background, show there too.

The phone shows its own armed ends and every PC's in the header of the pinned timeline notification, with a Cancel button, and on Today. For that, a PC's sealed sync file carries an optional `armedEnds` list (id, kind, ready time; times only, never the reason), and the phone can send a new sync action, `armedCancel`, which the PC applies at once while the end is still armed and otherwise answers as already applied. The phone's own armed end of a PC session is held on the phone and sent as the existing end action once the PC's wait allows it, so the PC still checks its own wait.

## Consequences

The owner confirms once and keeps working, and the countdown stays in view on every device. The guard is unchanged during the wait, because the session, the work block or the hold stays in force until the armed end applies, and the phone stays in Essentials as before.

What gets harder: an end can now happen while the owner is in another window, so it needs a clear notice when it applies, and an armed end must notice its occasion going away. Sync lag means a cancel from the phone can arrive after the end applied; the PC's answer says so. The sync file gains one field and the action set one action, both listed in `doc/hard-rules.md`. Older apps ignore the field and cannot send the action, so on them a PC's armed end is cancelled only on the PC. Reversing this means migrating stored armed ends and dropping a sync field that newer phones read.
