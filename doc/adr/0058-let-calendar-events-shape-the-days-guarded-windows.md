# 58. Let calendar events shape the day's guarded windows

Date: 2026-11-02

## Status

Accepted

Amends [47. Day plan modes](0047-day-plan-modes.md) and [28. Close the work-hours guard's free exits](0028-close-the-work-hours-guards-free-exits.md)

## Context

The day plan decides when guards run, through one resolver and one work-window engine (ADR 47), and any change that frees today's work hours on a guarded computer waits (ADR 28). A plan part lives in a day type, so a single meeting can only be planned by changing every workday or by making a one-off day by hand, and neither follows the calendar when the meeting moves or is cancelled. The owner wants guards to follow real events. An event is outside the app's locks: anyone with the calendar can create one at any moment, so taken naively this is a way out of a guard.

## Decision

A calendar event can carry a day plan category, chosen on the event or its series, or given by a per-calendar rule of keywords or every event. The priority is this event, then its series, then keywords, then every event, then the plan; a "Don't classify" choice overrides a rule, and all-day events are ignored. Any category except Sleep and Pre-Work can be chosen. Where two classified events overlap, the stricter wins.

Classified events overlay the date's template when it is resolved, into an effective plan that exists only in memory, so every guard reads the result and no guard reads events directly. The stored plan, its hash, the sync file and the plan editors never see the overlay.

A change that makes the day stricter applies at once. A change that makes it looser counts only if the app knew of the event at its current time for the looser-change delay before it starts, by the trusted clock, and never during a session the user started. That notice stands in for the ADR 28 wait for events. A meeting cancelled or moved away returns its old slot to the plan at once. Changing a rule is a looser change: it waits and is locked during a session. Every event-driven change shows on Today. On the PC, cached events keep applying while the calendar is offline, until a refresh says otherwise.

An event-made Comms part doesn't close comms elsewhere that day. On a Free day a Work event adds a window. On a Budget day a Work event counts toward the budget, without ever counting as focus time (ADR 20).

Two points were settled when work started and await the owner's review, both chosen because they cannot loosen a guard. An event never cuts a Sleep or Pre-Work part: without this, a Work event at night would remove the bedtime shading. A Work event counts toward a Budget only for the time that has already passed, so a meeting frees nothing before it happens.

The phone reads its own calendar through the Android calendar permission, and applies its own rules to it. For calendars it can't see, the PC resolves categories and sends each event's category and first-known time in the sealed sync file; PC rules and keywords stay on the PC. A loosening event from the file counts only if the file is under 24 hours old and the phone has itself held the event for the delay.

## Consequences

The plan matches the real day and one meeting no longer changes every workday. A real meeting added at the last minute takes up to the delay to loosen anything. A fake event made in advance still works, as editing tomorrow's plan does today; the Today line makes it visible rather than preventing it. Guard behaviour now depends on a remote calendar. On the phone, a calendar it can't see depends on sync freshness, so a cancelled meeting there can leave the phone loose until the next file arrives, and a slow sync makes the phone stricter than the PC. The phone gains a calendar permission and a second place to set rules. Event ids become stored keys, so a provider that changes ids drops a classification.
