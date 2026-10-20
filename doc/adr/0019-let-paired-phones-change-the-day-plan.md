# 19. Let paired phones change the day plan

Date: 2026-10-20

## Status

Accepted

## Context

ADR 14 made the PC the hub for paired devices. A phone can start a session and end one early, by the PC's own rules, and since L2 the PC pushes its profiles, sleep, day bar settings and events to the phone, but nothing on the phone changes the PC's data. The day plan (ADR 18, schema 5) lives in the PC's profile document, so on the phone it is read-only. On 2026-10-20 the user asked to edit the whole day plan from the phone (the Workdays and Other days plans, which days are workdays, one-off days and categories). Edits made away from home must wait until the phone reaches the PC again. When the plan changed on both sides in the meantime, the user wants to be asked, with each version's edit time (the newer one highlighted) and the changes listed as bullet points. The phone only talks to the PC while the app is open (L1/L2), and the relay for off-network sync isn't built yet.

## Decision

We will add one request from a paired device to the PC, `dayPlanEdit`, that carries a complete day plan in the profile document's JSON shape, the version of the PC's plan it was edited from, and when it was edited. The PC answers with `dayPlanAnswer`:

- **Applied:** the edit was based on the PC's current plan. The PC checks it like its own Day plan tab does and saves it at once (the plan has no looser-change delay). The usual settings push then brings it back to the phone.
- **Locked:** a focus session the user started is running, so the plan is locked as on the PC. The phone keeps the edit and sends it again once no session runs.
- **Conflict:** the PC's plan changed since the phone's copy. The answer carries the PC's current plan, version and edit time. The phone shows both versions with their times, highlights the newer one, lists each side's changes from the shared starting point, and the user picks one. Picking the phone's sends it again on top of the PC's version; picking the PC's drops the phone's edit.
- **Invalid:** the plan breaks the editor's rules (overlaps, a part under 15 minutes, a missing category); nothing changes.

The PC adds its plan's version (a hash it computes) and the time of its last plan change to the settings it pushes. An edit made while the phone can't reach the PC waits on the phone and is sent on the next connection. Only the day plan travels this way; profiles, rules and every other setting stay read-only on the phone. Merging two edits automatically is out of scope.

## Consequences

The plan can be fixed from wherever the user is, and nothing is lost silently: a clash always asks. The PC stays the single owner of the profile document and checks every edit, but it now accepts writes from paired devices, which widens what a stolen phone token can do from "start or end a session" to "change the day plan". The lock during focus sessions limits that to quiet times. The phone needs its own copy of the editor's rules (snapping, overlaps, nights) and must stay in step with the PC's. A PC that predates this answers "Unknown request", which the phone shows as "update the PC app". Conflicts are only found on the phone today, because only the phone sends edits; once the relay exists, the PC may need the same prompt.
