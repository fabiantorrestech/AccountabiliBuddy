# 65. Let Start shutdown end the work block early

Date: 2026-11-05

## Status

Accepted

## Context

The shutdown routine was built as a closing ritual that enforces nothing. A finish in the middle of the workday changed nothing until the planned work end (`doc/hard-rules.md`, the shutdown routine), so that pressing Start shutdown at 10 AM could not quiet the start nudge and the phone's work card for the rest of the day.

In use, that reads wrong. The owner presses Start shutdown when they are done working, and expects AB to agree: the block should end, the nudges should stop, and the timeline should show that work finished early. Today the timeline keeps the block as planned and the guards keep steering toward work the owner has already wrapped up.

Ending work early loosens a guard, so it cannot be free. AB already has a shape for that: ending a session early takes a wait and a written reason (the profile's bypass settings).

## Decision

We will let Start shutdown, pressed during a work part with no session running, end that work block early. It first asks for the same wait and reason as ending a session early, using the default profile's bypass settings; the reason is sealed on the PC like end-early reasons and never synced. When another work part comes later the same day, the same card asks whether to end that too, with only the current block selected by default. Outside a work part Start shutdown is unchanged.

From the moment it is confirmed, the rest of the block counts as not work everywhere: the work-window engine on both the PC and the phone clips the window, so the start nudge, the start-of-day assistant, the phone's work card and notice, and the shutdown card's work end all agree. The work-hours guard (ADR 28), the quit safeguard and the Cold Turkey key holds stop for the ended time too, because they guard work time; that is looser, and the work-hours guard's own "stop for today" already costs the same wait and reason, so ending work early buys nothing cheaper. "Back to work" on Today undoes it at once, since that is the stricter direction.

The ended stretch is drawn lighter on every timeline (PC day bar and Plan, phone day bar, widget and timeline card, watch tile). It reaches the phone and watch as a new optional field in the PC's sealed sync file: per date, the ended windows and when they were last edited, newest edit winning as with lunch. An empty list after an edit means it was undone. It carries times only, no reason. An older phone ignores the field and keeps nudging, which is the stricter side.

## Consequences

Start shutdown now means what the owner means by it, and the timeline tells the truth about the day. The cost is a new way out of the work-start guards. The wait and the reason are what keep it from being free, and it never applies during a session, which keeps "no escape hatches during focus" intact.

The rule in `doc/hard-rules.md` that a mid-day finish changes nothing is replaced. The sync file gains one field, which `doc/hard-rules.md` lists with what the sync file may carry. Focus totals are unchanged, since they only count sessions. A Budget day's current block is its whole guarded window, so ending it early ends that day's budget work.
