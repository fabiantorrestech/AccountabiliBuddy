# 8. Store activity history with readable totals and encrypted details

Date: 2026-10-18

## Status

Accepted

## Context

M7 adds a local activity history: what was on screen and for how long, how sessions ended, and the user's answers to the classify prompt. It feeds the History tab now and habit suggestions and the local classifier in V2. The history is valuable precisely because it is detailed, and dangerous for the same reason: window titles carry email subjects, document names and chat names; full addresses carry search terms and account ids; screen text can contain anything; early-end reasons are personal notes. ADR 2 keeps all of it on the device, and ADR 6 already chose SQLite with DPAPI for screen text. What remained open was which fields stay readable, how long anything is kept, and whether logging is on by default.

## Decision

Activity segments, labels and session events go into new tables in the existing SQLite database (migration v2). Only the fields needed to compute totals stay readable: times and durations, monitor, process name, the site's domain, classification, profile and session context. Window titles, full addresses, screen text and early-end reasons are encrypted with DPAPI in the current user's scope, with an app-specific entropy value, called directly through CsWin32 so no extra package is needed. History is kept for 90 days and pruned automatically. Logging is on by default, runs whenever the app runs (classification only during focus phases), pauses after five minutes without keyboard or mouse input, and can be revoked or wiped ("Delete my data", which also vacuums the file) from the Setup tab. Screen reading stays off until the user grants it.

## Consequences

Totals and top lists are plain SQL over readable columns, so the History tab and V2 aggregation stay fast and simple. Another Windows account, or a copy of the database on another computer, can see which apps and sites were used and when, but not titles, addresses, screen text or reasons. Anything that needs those fields (V2 training) must decrypt row by row on this machine under this user, which is slower but intended. Data encrypted by DPAPI is lost if the Windows account's credentials are reset by an administrator; for a 90-day history that is acceptable. Changing what is readable later means a migration that re-encrypts or decrypts existing rows.
