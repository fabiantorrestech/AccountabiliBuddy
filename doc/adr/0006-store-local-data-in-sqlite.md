# 6. Store local data in SQLite

Date: 2026-10-15

## Status

Accepted

## Context

The app needs durable local storage for profiles, rules, schedules, session history, activity segments and user labels. The activity log grows continuously and will later be queried for habit statistics and used as classifier training data. Under ADR 2, everything stays on the device and no server database is possible.

## Decision

We will store all local data in a single SQLite database per device, accessed through Microsoft.Data.Sqlite. OCR text is encrypted at rest with Windows DPAPI before it is written.

## Consequences

SQLite is embedded, needs no service, runs on Windows, Linux and Android, and handles the query patterns habit analysis needs. Schema migrations must be managed by hand. DPAPI encryption is Windows-specific, so other platforms will need their own key store, and encrypted columns cannot be searched in SQL. Moving to a different store later would require migrating every user's history.
