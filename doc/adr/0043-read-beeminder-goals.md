# 43. Read Beeminder goals

Date: 2026-10-26

## Status

Accepted

## Context

The user uses Beeminder and asked (2026-10-26) to see their goals and what each needs today inside AccountabiliBuddy, read only for now; sending focus minutes to Beeminder is a later idea. The hard rules allow no outbound connection without an ADR, and keep every PC connection in one auditable class (`CalendarNetwork`). Beeminder's API takes a personal auth token in the query string and returns the user's goals as JSON.

## Decision

The PC reads the user's Beeminder goals with HTTPS GET calls to `https://www.beeminder.com/api/v1/users/me/goals.json`, made only from `CalendarNetwork`, only after the user pastes their personal auth token in Integrations → Beeminder, about every 30 minutes and when Today opens; nothing is ever written to Beeminder and nothing about the user's activity is sent. The token is DPAPI-encrypted in the document "beeminder" and can be removed at any time, which stops the calls. The PC keeps the last answer and puts a derived summary (each goal's title, what's needed today, safe days and deadline) in its sealed sync file, so the phone shows the same Today card without a token or a connection of its own. Removing the token removes the summary.

## Consequences

The user sees their Beeminder commitments next to their focus without leaving the app. A new outbound connection exists while a token is set, and Beeminder learns that the token was used from this PC's address and when. The phone and watch stay connection-free for it. Sending data points (focus minutes) would need a new ADR.
