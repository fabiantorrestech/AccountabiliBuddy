# 88. Split the lunch into counted pieces, and let Discard be switched off per mode

Date: 2026-11-13

## Status

Accepted (owner, 2026-11-13: "Count 20 of these and keep counting...", "Any time, both modes - give users the option to disable this for PB or AB", "I'm back = Count it")

Amends the lunch behaviour of [77. Step away keeps the timer running with blocking on](0077-step-away-keeps-the-timer-running-with-blocking-on.md) (lunch still pauses a session as a step away; it now has pieces) and the lunch row of [61. Choose what syncs row by row on each device](0061-choose-what-syncs-row-by-row-on-each-device.md) (the record carries more). Uses the per-mode storage of [69. Keep separate settings per mode](0069-keep-separate-settings-per-mode.md).

## Context

The flexible lunch (schema v8) is one record, a start and an end, that every device takes by newest edit. "I'm back" ended it for the day. The owner wants a ✕ on a running lunch with two choices: Count it (the minutes taken count, and the rest of the day's allowance stays available for a later piece, for instance 20 of 60 minutes counted and 40 left) and Discard it (the piece never happened).

Review of that brief found the points this decision has to settle:
- **Discard is a way to get unlimited lunch.** "As if it never started" gives the full length back, and lunch lifts the phone's LockMyPhone session lock and suspends Takeover, placement nags and the work nudge, and holds a PC session's step away. Start then Discard, repeated, would be an escape hatch under the hard rules.
- **"Left" could not be computed the same everywhere.** The lunch length is a per-device setting, and a fixed lunch's length is its window's remainder, so two devices would show different minutes left.
- **A discard on a fixed-lunch day restarted lunch at once.** A fixed lunch starts by itself when no lunch was taken today; a discard that cleared the record made it start again within a second.
- **Older apps drop new fields.** An older phone's +5 or End on piece 2 rewrites the lunch from its three fields, and newest-wins would then wipe the counted minutes, which is more lunch.
- **C# record equality over a list is by reference**, while Kotlin data classes compare lists by value, so a list in `SyncLunch` would make the same-lunch checks and the "did it change" test behave differently on the two sides.
- The per-piece cap (start plus 120 minutes) no longer bounds the day.

The owner answered the Discard question: it is allowed at any time, in both modes, with a setting per mode to turn it off; and "I'm back" is the same as Count it, so the two buttons do not mean different things.

## Decision

We will model the day's lunch as an allowance taken in pieces, with one pure rule on both platforms (`LunchDay` in Core and in the shared Kotlin, the same rule, checked against shared JSON fixtures).

**The record.** `SyncLunch` keeps `start`, `end` and `editedAt`, which stay the current piece (or the last one once it is over), so an older app still reads a running or ended lunch. Three optional fields follow, left out when not set:
- `allowance`: the day's lunch in minutes, fixed when the first piece starts (the length then, or a fixed lunch's window remainder) and moved by +5 and -5. Every device computes what is left from it, never from its own length setting. A record with none (an older app) uses this device's length.
- `counted`: the earlier counted pieces as start and end. Missing means unknown (an older app); an empty list means none.
- `discarded`: true when the only piece was discarded.

**The actions.**
- **Count it** (and "I'm back"): the running piece ends now and counts. The rest stays for later today; a new piece starts from "Start lunch" and runs for what is left, the counted piece moving into `counted`. A running session that lunch stepped away returns, as "I'm back" does now.
- **Discard it**: the running piece never happened. The first piece becomes a zero-length record marked `discarded`; a later piece goes back to the last counted one, which leaves the list. Either way the full remainder is available again.
- The day's total (counted pieces plus the current one) is never over 120 minutes, whatever +5, another device or a stale file says.
- A discarded record still marks the day as taken, so a fixed lunch never starts by itself again; only an explicit Start lunch starts one.

**Discard is allowed any time, and each mode can switch it off.** The setting "Allow discarding a lunch" is on by default for AccountabiliBuddy mode and for ProductaBuddy mode. It follows the pattern of the other per-mode settings (ADR 69, 77): device-local, one copy per mode (PC: `tasks.settings` and `tasks.settings.pb`; phone: preferences "lunchSettings"), never in the profile JSON, so the profile schema stays at v13. Turning it off is stricter and applies at once, also during a session. Turning it on is looser: it waits the looser-change delay, is refused while a focus session runs and takes effect only when none runs, through the existing pending-change mechanism (`LunchDiscardSetting`, pure, the same rule on both platforms). A backup restore goes through the same rules. It is not in the mode compare view, as with ADR 77's settings, because "make both agree" would skip the wait. Because it is device-local like the other per-mode settings, a device with Discard turned off hides it while another device may still offer it; the record a device writes is the same either way.

**Older apps.** A record whose `counted` is missing and whose start is ours keeps our counted pieces and allowance (the allowance grows to cover the piece if the older app lengthened it). A record whose start is one of our counted pieces is a stale view of the day and changes nothing. It stays discarded only while it is still zero length (a discarded lunch always has End == Start), so an older app's +5 on a discarded lunch makes a running lunch whose minutes count, never a "discarded" one that runs. The other way round, when our own pieces or allowance are unknown (an older app's edit was read first) and an older or equal record with the same start carries them, they are taken whatever its stamp; records with the same stamp end on one fixed order on every device. Minutes are rounded one way on both sides: the allowance up to a whole minute, +5/-5 half away from zero, exact to the tick. A discarded lunch looks to an older app like a lunch of zero length that is over, so it cannot start one; this is accepted and goes in What's new.

**Equality.** `SyncLunch` equality is by value on both sides, the list included (C# gets an explicit `Equals` and `GetHashCode`). `LunchDelivery` compares the pieces (current piece, counted pieces, allowance), judging an older app on the current piece alone.

Lunch never shortens a focus wait or unlocks anything beyond what it already did, and every time comes from the trusted clock on the PC and the trusted time on the phone. The screens (the ✕ on Today, the title bar and the overlay on the PC; the card, the notification and the watch on the phone) are separate work after this one.

## Consequences

- One meaning for "I'm back" and Count it, and "40 left" is the same on every device.
- Discard stays an escape hatch where it is switched on: any user can start and discard a lunch repeatedly, and lunch lifts the phone's session lock and holds a step away while it runs. That is the owner's choice, and the per-mode switch is the control. Turn it off for the mode where guarding matters.
- Turning Discard on again is slow (the looser-change delay, never during a session) and turning it off is immediate, so the stricter direction is always cheap.
- The sync file's lunch grows by a few small fields. Older apps ignore them and keep reading a running or ended lunch; an older app can still make a discarded lunch look over and cannot start another, and its edits no longer wipe the counted minutes.
- Every reader of the lunch must go through `LunchDay` (current piece, counted pieces, remaining, can start, can discard) instead of reading start and end alone; the PC and phone screens and the watch snapshot follow in later tasks.
- LU1c (phone and watch): the phone's ✕, "End lunch" notification action and Start the rest use `LunchOps` over `LunchDay`; Count it and Discard it re-lock the session lock through `LunchLockLift` like a lunch that ends, Start the rest lifts it for the remaining piece only. The watch snapshot's lunch gains a `discardAllowed` mark and the day's minutes (`allowanceMinutes`, `countedMinutes`, `restMinutes` for "Start the rest", worked out by the phone); the phone decides every Discard it against its own mode setting, and the watch waits for the phone's answer to Discard it and Start the rest.
- Reversing this means removing three optional fields and the setting, which is cheap while no screen depends on them.
