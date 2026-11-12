# 80. Send goal values to Beeminder

Date: 2026-11-12

## Status

Proposed

Amends [43. Read Beeminder goals](0043-read-beeminder-goals.md) (reading stays as it was; this adds one write path)

## Context

ADR 43 lets the PC read the user's Beeminder goals and says nothing is ever written and that sending data points would need a new ADR. UI round 2 lets a goal link to a Beeminder goal, and the owner decided (2026-11-11) that the link's direction should be Both, with reading shipping first: AccountabiliBuddy measures the goal (focus minutes, ticks, steps) and Beeminder should track what it measures, while Beeminder's own datapoints stay readable. Beeminder's API adds a datapoint with an HTTPS POST to `/api/v1/users/me/goals/<slug>/datapoints.json` using the same personal auth token the user already pasted.

## Decision

When the user sets a linked goal's direction to Send or Both, the PC posts that goal's counted value for a day as one Beeminder datapoint once the day has closed, and posts again (updating its own datapoint by its id) if a back-fill later changes that day's value. Only the number, the date and a fixed comment ("from AccountabiliBuddy") are sent: no goal name, note, task title, label or app usage. Calls go only through `CalendarNetwork`, with the existing DPAPI-sealed token, and only for goals the user linked with Send or Both. The app records the datapoint ids it created (device-local) and never changes or deletes a datapoint it did not create. A Beeminder datapoint never overwrites a value the user typed in AccountabiliBuddy. Only one PC sends for a goal: the lowest-DeviceId PC with a token (the same rule as task numbering). Removing the token or setting the direction back to Read stops all sending; datapoints already sent stay in Beeminder.

## Consequences

Beeminder can enforce what AccountabiliBuddy measures without the user typing it twice. A second kind of outbound call exists while a token is set, and Beeminder learns daily totals for linked goals. A wrong count now reaches a service that can charge the user money, so the value is sent only after the day closes and back-fills correct it by updating the same datapoint. Until this ADR is accepted, Send and Both are shown greyed as "Coming soon".
