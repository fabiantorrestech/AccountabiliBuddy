# 67. Let the phone finish work for the day with a tag or a wait

Date: 2026-11-05

## Status

Accepted

## Context

During work time with no session, the phone's work nudge covers the screen with "Plan 3 tasks and start". Its "Not working today?" choices are a day off (a 10-minute wait, then no more phone nudges today) and "I need my phone for a bit" (the emergency allowance). There is no way to say "I've finished work for today": the owner, already in the car after leaving work, is nudged to start work and has to burn the day-off wait or the emergency allowance to get the phone back. Work ended early on the PC (ADR 65) reaches the phone, but nothing goes the other way.

The owner wants a quick way out that proves they have left: an NFC tag kept in the car, tapped on the nudge card, ends work at once. Without the tag, the same 10-minute wait as a day off. The app has no NFC code. Android's NFC API is part of the platform, so no library is needed, but it needs the `NFC` permission. A tag's serial number is easy to clone, and any tag can be peeled off and moved.

Ending work is looser. Under `doc/hard-rules.md`, a receiving device applies a looser change from another device after its own delay and never during a session.

## Decision

We will add "I've finished work for the day" to the work nudge's "Not working today?" choices. On the card that opens, the tag can be tapped, which ends work at once on the phone, or the owner waits 10 minutes with the card up, as with a day off. The tag is read only while that card or the tag's set-up page is in front, through reader mode; there is no intent filter, so tapping it anywhere else does nothing. Set-up (Settings › Guards › Work nudge, and the setup assistant) writes a random 32-byte secret to the tag as an NDEF record, reads it back, and keeps the secret sealed with `TokenVault`. Only a tag carrying that secret counts. The permission is declared with NFC as an optional feature, and on a phone without NFC only the wait is offered.

Finishing ends the rest of today's guarded work everywhere. The phone stops its own nudges at once and writes the ended windows into its sealed sync file, in the shape of the PC's `workEnded` (ADR 65): plan date, windows, `editedAt`, times only, never a reason. A PC treats it as a looser change: it applies it once its looser-change delay has passed since the phone's `editedAt`, a stamp counting at most 2 minutes ahead as with other stamps, and never during a session. The newest edit for the day wins, as with lunch, so "Back to work" on either side undoes it.

## Consequences

The owner can leave work and get the phone back with one tap of a tag that stays in the car, and the PCs stop nudging too. The 10-minute route still works without the tag.

The tag is a commitment device, not proof: the secret stops other or copied tags, but nothing stops the tag being moved to the desk. The PCs follow later than the phone: the delay counted from the phone's stamp is usually over by the time a file arrives after a 10-minute wait, but after a tag tap the PCs keep their guard for up to the delay plus sync lag. That is the stricter side, and stated in the card. The app gains its first hardware permission besides health and usage data, and the sync file gains one field from the phone, listed in `doc/hard-rules.md`. An older PC ignores the phone's field and keeps nudging.
