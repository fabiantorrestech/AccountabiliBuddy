# 7. Use versioned JSON for profiles and rules

Date: 2026-10-15

## Status

Accepted

## Context

Focus profiles (allow and block rules) and schedules are the data that future companions must share: an Android app written in Kotlin and a Linux desktop app need to read the same profiles and sync them across devices. These apps will not share code with the C# Core (ADR 4), so they need a common, language-neutral format that can evolve without breaking older app versions.

## Decision

We will define profiles, rules and schedules as a JSON document with an explicit `schemaVersion` field. This JSON is the export/import and sync format, and it is stored as-is in SQLite. Changes to the shape bump the version, and readers must handle every version they claim to support.

## Consequences

Any language can read and write profiles, sync can send whole documents, and users can back up or hand-edit their profiles. Rules cannot be queried in SQL column by column, and every schema change needs a migration path in each app that reads the format. Changing the format after companions ship means coordinated releases across platforms.
