# 64. Classify events from the timeline, and let a hand choice lift Pre-Work

Date: 2026-11-04

## Status

Accepted

## Context

AB's guards can't tell that a meeting is a meeting, so they get in the way of it. ADR 58 lets an event carry a day plan category, but the owner rarely reaches it: on the PC only event rows inside a work block take a click, and the day bar takes none; the phone can only set rules per calendar. The owner wants to right-click or tap any event where they see it and say what it is.

ADR 58 also left a gap. An event can't cut a Pre-Work part, so a meeting at 8 AM during Pre-Work still has its work tools minimized, whatever it is classified as. That was chosen because it can't loosen a guard, and left for the owner's review. Offering Pre-Work as a category wouldn't help, since it would minimize the tools during the meeting. The owner decided that their explicit choice should win.

The phone needs a way to classify an event the PC owns. A PC's sync file names an unclassified event only by title and times; its series and occurrence keys stay on the PC (ADR 58). Classifications are also kept on the device that made them.

## Decision

We will open one event menu from every place an event shows: right-click on the PC (Plan timeline and day bar), tap on phone and tablet. It holds Classify as (whole series by default, or just this one), Don't classify, Back to automatic, Always classify like this, and Add a task for it. Every change goes through the rules ADR 58 already has: stricter applies at once, looser waits and needs notice before the event starts, and nothing changes during a session the user started. The watch gets no menu, because that would mean sending meeting titles to it (ADR 21).

An event classified by hand, on itself or its series, may lift Pre-Work for that event's time. It always counts as a looser change. Keyword and every-event rules still can't lift Pre-Work, because a rule made once shouldn't quietly open a guard on every later day. Nothing cuts Sleep. Pre-Work and Sleep stay out of the categories an event can have.

For an event in a PC's file, the phone sends a new sealed `classifyEvent` action. The PC finds the event by title, start and end, as the phone already matches events across sources, and applies the choice itself. No series or occurrence key has to leave the PC, and the result returns in the PC's file like any classification. That file gains an optional `categoryByHand` on an event, so the phone's own guards lift Pre-Work the same way; an older phone ignores it and keeps the guard, which is the stricter side.

For an event only in the phone's own calendar, the phone keeps its own classifications, Keystore-sealed, never synced or backed up, with the same wait as its calendar rules.

## Consequences

A meeting can be marked in two taps, and AB stops fighting it from then on. A morning meeting no longer needs an emergency unlock to get past Pre-Work, as long as it was classified the looser-change delay ahead. That is a real loosening of ADR 31's guard, on purpose, and the notice rule is what keeps it from being a way out: a meeting invented two minutes before work buys nothing.

Classifying a meeting that starts in under the delay still doesn't take effect at its start. The menu says when it starts counting. The phone's classification reaches the PC only when the files sync, so a PC that isn't reachable leaves the event as it was until then. Two events with the same title and times in different calendars can't be told apart from the phone; the PC applies the choice to the first it finds.
