# 75. Let ProductaBuddy keep an optional night

Date: 2026-11-09

## Status

Accepted

Amends [68. Add a synced Companion mode that quiets guarding](0068-add-a-synced-companion-mode-that-quiets-guarding.md)

## Context

Under ADR 68, ProductaBuddy (Companion mode) has no night on the phone. AB stops asking LockMyPhone-Extended for the bedtime-to-wake lock, shows no bedtime essentials screen and sends no bedtime reminders. On 2026-11-09, while planning the phone's lock setup (ADR 73), the owner asked for the night to be available in ProductaBuddy too, as an option that is off unless chosen. They also want it offered in the setup assistant when ProductaBuddy is picked there, along with the morning lock. ProductaBuddy is meant to be the quiet mode, so it must not start a night nobody asked for.

## Decision

We will let ProductaBuddy keep the night's lock (LockMyPhone-Extended, or the bedtime screen when the built-in screen is the choice) as an opt-in.
- **Where it lives:** a phone setting under ProductaBuddy's own settings (ADR 69), off by default, offered on the setup's "When it engages" page whenever ProductaBuddy is the mode.
- **When it's on:** the night runs from bedtime to wake time as in AccountabiliBuddy.
- **What stays off:** bedtime reminders and the rest of ADR 68's quiet list still don't run in ProductaBuddy.
- **The morning lock:** an LMP feature that AB only sets up (ADR 73), so it is available in both modes.

Switching the night on is stricter and applies at once. Switching it off is looser and follows the usual delay, never during a session.

## Consequences

Someone in ProductaBuddy who wants a locked phone at night no longer has to switch to AccountabiliBuddy and take all its guarding with it.

The mode gains one exception to "ProductaBuddy keeps no night", which `doc/hard-rules.md` has to name, and every night check now reads the mode's setting rather than the mode alone. Undoing this means removing one setting and its checks.
