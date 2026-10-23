# 31. Guard Pre-Work parts

Date: 2026-10-23

## Status

Accepted

## Context

The user sometimes gets up early or reaches the office early. In that stretch they are not in work mode yet, but they don't want to get pulled into work early, and they don't want to drift into distractions on their own laptop either (2026-10-23). The day plan has Work, Comms, Rest and Sleep behaviours. None of them describes a guarded warm-up. AccountabiliBuddy blocks nothing outside a session the user started (ADR 18). The only exception so far is the opt-in work-hours guard (ADR 28). The user chose this shape (2026-10-23):

- every computer enforces it;
- starting a session ends it;
- a first question asks whether today is a work day, and a day off costs a ten-minute wait spent planning the day;
- a rare, deliberately painful emergency unlock exists, which reminds the user to schedule off days instead;
- the phone shows a quiet notice.

## Decision

We will add a day plan behaviour, Pre-Work (profile schema v7; an existing plan gets the category only, with no parts). Pre-Work is not planned work: it doesn't count toward planned focus, the start nudge ignores it, and comms windows don't apply in it.

On every computer, the first time the user is active during a Pre-Work part with no session running, a window asks "Is today a work day?".

- **Yes.** Until the part ends or a session starts, the work-hours guard's apps are minimized and the default profile's distracting apps and sites are minimized, while everything else stays free. Starting a session ends Pre-Work on that computer for the rest of the day.
- **No.** The window waits ten minutes with a box for what the user wants to do with the day, kept as come-backs. After the wait, today is a day off on that computer: no Pre-Work, no work-hours guard and no start nudge.
- **Emergency unlock.** The window has a way out that costs a reason of at least 40 characters and retyping a random 12-character code. It is limited to two a week, and each use is kept with its reason DPAPI-encrypted. It lifts Pre-Work until the part ends and reminds the user to schedule off days in the Calendar tab instead.

The answers and unlocks live in the app-owned document "prework" on each computer. The phone shows a quiet "Pre-work until …" notice during the part, with pickups counted. This amends ADR 18 ("no scheduled blocking") for Pre-Work parts.

## Consequences

An early start at home or at the office gets a guarded warm-up on every computer without starting a session, and a day off costs a short planning wait instead of nothing. In exchange, blocking now happens outside sessions on every computer whose plan has Pre-Work parts. That includes the work computer, where a blocked app is only minimized. Sites are held back by minimizing their browser window, not by the session's overlay. Whether today is a work day is asked once per day on each computer, since the answer doesn't sync. Reversing this means dropping the behaviour, which a v7 plan would then read as Rest. The document "prework" holds nothing else.
