# 41. Get a new spare key with friction

Date: 2026-10-26

## Status

Accepted

Amends [39. Hold Cold Turkey's keys during AB's windows](0039-hold-cold-turkeys-keys-during-abs-windows.md) (a new key only after one was used)

## Context

Under ADR 39 a new spare key is made only after the old one is used, in Emergency release or in Cold Turkey. A user who loses the paper, or fears someone saw it, has no way to replace it short of using it, and using it releases every held block for the day. The user asked (2026-10-26) for a way to get a new key that is possible when truly needed, also during work hours, but costs "substantial friction", because a key that can be shown or remade at will is a way out of a session. They chose the same friction as Pre-Work's emergency unlock (ADR 31).

## Decision

"Get a new spare key" (About, beside Emergency release, and the Cold Turkey card in Integrations) asks for a reason of at least 40 characters and the retyping of a shown 12-character code (no pasting), then waits 10 minutes with a countdown and Cancel. It is refused while a focus session the user started is running (they end it first, with its own wait and reason), and when two new keys were already made in the last 7 days; it works in work hours and Pre-Work. The wait survives a restart and counts by the trusted clock. When the wait ends the app makes a new password, re-locks the blocks it holds with it, and shows the new key once to be written down and retyped, as in ADR 39; the old key stops working. Releasing nothing, it never lifts a block by itself. The current key is never shown again. Each request is logged with its time and its reason, the reason DPAPI-encrypted, kept 90 days, never sent.

## Consequences

A lost key can be replaced without releasing the day's blocks, at the cost of ten minutes and a written reason, at most twice a week. Someone who wants out of a held block gains nothing from it: the new key only arrives after the wait and replaces the lock rather than lifting it, so Emergency release stays the one way to release. The keys document grows a short request log.
