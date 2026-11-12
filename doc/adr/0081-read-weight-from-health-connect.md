# 81. Read weight from Health Connect

Date: 2026-11-12

## Status

Proposed

Extends the Health Connect reads of [11. Allow read-only Google Health sleep data](0011-allow-read-only-google-health-sleep-data.md) and [34. Learn a smart bedtime on the phone](0034-learn-a-smart-bedtime-on-the-phone.md) with one more record type.

## Context

The phone reads Health Connect on the device for sleep and steps (permissions READ_SLEEP, READ_STEPS and READ_HEALTH_DATA_IN_BACKGROUND, used by `SleepReader` and `StepsReader`; ADR 34 and the phone's Stats rule in `doc/hard-rules.md`). UI round 2 adds amount goals, and the owner wants a weight goal (for example "175 lb by Mar 1") to fill itself from the scale or watch data that already lands in Health Connect (owner, 2026-11-11). Reading weight needs a new permission, READ_WEIGHT, which the hard rules allow only with an ADR. The Pixel Watch app itself reads no sensors (ADR 21); its data reaches the phone through Health Connect.

## Decision

The phone asks for READ_WEIGHT only when the user creates or edits a weight goal with Health Connect as a source, in a setup step that says what is read and why, and reads weight records on the phone with a `WeightReader` next to `StepsReader`, also in the background under the READ_HEALTH_DATA_IN_BACKGROUND permission it already holds. The readings stay on the phone. For the goal it keeps every reading as an entry and uses the day's first reading by default; a value the user typed always wins. Only the goal's derived day value may leave the phone, and only under ADR 79 (inside the sealed sync files); without ADR 79 the weight goal lives on the phone alone. Weight entries are private data: Keystore-sealed at rest and included in the phone's own backups like the goal they belong to. Nothing about weight goes to the watch or to Beeminder (ADR 80). Turning the source off stops reading; revoking the permission in Android does the same and the goal falls back to hand entries.

## Consequences

Weight goals fill themselves without typing. The phone holds one more Health Connect permission, granted only by users who want a weight goal. A body measurement becomes part of the goal data, protected as private data on the phone and, with ADR 79, inside the sealed files. Until this ADR is accepted, weight goals take hand entries only.
