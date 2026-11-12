# 80. Send goal values to Beeminder

Date: 2026-11-12

## Status

Proposed

Amends [43. Read Beeminder goals](0043-read-beeminder-goals.md) (reading stays as it was; this adds one write path). Depends on [79. Sync goals between the user's devices](0079-sync-goals-between-the-users-devices.md) for a link made on one PC to be known on the others.

## Context

ADR 43 lets the PC read the user's Beeminder goals and says nothing is ever written and that sending data points would need a new ADR. UI round 2 lets a goal link to a Beeminder goal, and the owner decided (2026-11-11) that the link's direction should be Both, with reading shipping first: AccountabiliBuddy measures the goal and Beeminder should track what it measures, while Beeminder's own datapoints stay readable. Beeminder's API creates a datapoint with an HTTPS POST to `/api/v1/users/me/goals/<slug>/datapoints.json` and updates one with a PUT to `/api/v1/users/me/goals/<slug>/datapoints/<id>.json`, using the personal auth token the user already pasted. Health and phone-usage values may leave the phone only inside the user's own sealed sync files (hard rules; ADR 79, 81).

## Decision

When the user sets a linked goal's direction to Send or Both on a PC, that PC posts the goal's counted value for a day as one Beeminder datapoint once the day has closed, and updates its own datapoint (PUT by id) if a back-fill later changes that day's value. Only values the PC itself measures or the user typed are sent: focus minutes, ticks and hand-entered values. Steps, sleep, weight and phone-usage values are never sent to Beeminder. Only the number, the date and a fixed comment ("from AccountabiliBuddy") are sent: no goal name, note, task title, label or app usage. Calls go only through `CalendarNetwork`, with the existing DPAPI-sealed token, and only for goals linked with Send or Both. The PC where the link's direction was set is the one that sends; it records that and the datapoint ids it created device-local, and never changes or deletes a datapoint it did not create. A Beeminder datapoint never overwrites a value the user typed in AccountabiliBuddy. Removing the token or setting the direction back to Read stops all sending; datapoints already sent stay in Beeminder. The "Local only" lists in `CLAUDE.md` and `doc/hard-rules.md` name this write path when it ships.

## Consequences

Beeminder can enforce what AccountabiliBuddy measures without the user typing it twice. A second kind of outbound call exists while a token is set, and Beeminder learns daily totals for linked goals. A wrong count now reaches a service that can charge the user money, so the value is sent only after the day closes and back-fills correct the same datapoint. Goals measured on the phone can't be sent. Until this ADR is accepted, Send and Both are shown greyed as "Coming soon".
