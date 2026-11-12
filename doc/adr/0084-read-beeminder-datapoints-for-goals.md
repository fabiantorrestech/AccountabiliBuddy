# 84. Read Beeminder datapoints for goals

Date: 2026-11-14

## Status

Accepted (owner, 2026-11-14)

Amends [43. Read Beeminder goals](0043-read-beeminder-goals.md): one more read-only call; everything else in ADR 43 stands.

## Context

UI round 2 lets a goal use a linked Beeminder goal as a source (Edit goal › Tracking & sources › Beeminder, direction Read). ADR 43 allows only `GET /api/v1/users/me/goals.json`. That call gives each goal's overall state but not its values day by day, so a linked source could not count anything: no streak, no heatmap, no "today" value. The owner asked (2026-11-14) that Beeminder be a real way to track a goal, since the PC already reads Beeminder. Beeminder's API lists a goal's datapoints with `GET /api/v1/users/me/goals/<slug>/datapoints.json`, using the same personal auth token, and each datapoint has a day stamp and a value. Sending values to Beeminder is a separate decision (ADR 80, still Proposed).

## Decision

For each AccountabiliBuddy goal with a Beeminder source in direction Read, the PC reads that Beeminder goal's datapoints with HTTPS GET calls to `https://www.beeminder.com/api/v1/users/me/goals/<slug>/datapoints.json`. It asks for the newest page only (no more than about 90 days on the first read, then only recent ones), on the same schedule as ADR 43's goal reads (about every 30 minutes and when Today or Goals opens). The call is made only from `CalendarNetwork` and only while a token is set. Nothing is written to Beeminder.

The PC keeps from each datapoint only its day, its value and its id. Comments and any other fields are dropped and never stored or logged. It turns each day into a goal entry with source Beeminder, using the Beeminder goal's own day rule (`aggday` from goals.json; sum when it is missing or unknown). These entries are stored with the goal data like other readings, sealed at rest. A value the user typed in AccountabiliBuddy always wins over a Beeminder reading. Unlinking the source, setting the direction away from Read, or removing the token stops the calls and removes the stored Beeminder entries.

The entries stay on the PC. The phone shows such a goal's Beeminder part as "Measured on your PC" until ADR 79 lets derived day values sync. The ADR 43 summary in the sync file is unchanged. The "Local only" lists in `CLAUDE.md` and `doc/hard-rules.md` name this read next to ADR 43.

## Consequences

A goal linked to Beeminder fills in by itself on the PC, with history, streaks and the heatmap, without the user typing values twice. The PC makes one more kind of read-only call per linked goal while a token is set, which tells Beeminder nothing it doesn't already know beyond the fact that the token was used. Beeminder values live in the PC's goal data. Deep work's sources stay locked to focus, so Beeminder never feeds Deep work, guards, the goal unlock or `focusDay`.
