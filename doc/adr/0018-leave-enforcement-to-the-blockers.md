# 18. Leave enforcement to the blockers

Date: 2026-10-20

## Status

Accepted

## Context

AccountabiliBuddy was built as a blocker: during focus it escalates from a toast to an overlay to minimizing or redirecting, on a weekly grid that assigns profiles to hours. The user also runs Cold Turkey on the PC and Curbox on the phone, which enforce the same lists, so two tools police the same thing, and AccountabiliBuddy's own waits (such as the 10-minute end of a scheduled block, or the 10-minute delay on looser profile changes) got in the way of real needs without adding protection. On 2026-10-20 the user redefined AccountabiliBuddy as a body double: it notices drift, asks about it, keeps score of the focus sessions they start, and hands what it learns to the blockers, which do the enforcing. The design is in `doc/plans/2026-10-20-body-double-design.md`.

## Decision

We will remove AccountabiliBuddy's own enforcement. It will no longer minimize, redirect or block windows or tabs, and it will no longer run scheduled blocks from a weekly grid of profiles. It will notice drift, ask about it, nudge with a toast and then the on-screen window (never further), log, and hand sites to the blockers (a separate decision covers how). The weekly grid is replaced by the day plan, whose categories either watch for drift, rest or mark sleep; the plan is stored in the shared profile document as schema version 5, migrated from the grid. Profiles become a read-only view of the blockers' blocks plus AccountabiliBuddy's own lists of on-track and off-track sites, which it uses to judge drift. Focus sessions the user starts stay, and those that count down to a set end keep their end-early wait and reason. During a focus session the user started, Profiles, Settings and Setup's Revoke and Delete stay locked, so the buddy can't be switched off mid-session. The delay on looser profile changes is dropped, since AccountabiliBuddy no longer blocks anything it could protect.

## Consequences

There is one police officer instead of two, and no AccountabiliBuddy wait stands between the user and a genuine need. Without a blocker installed nothing is hard-blocked, only nudged, so the setup assistant has to say so plainly. Rules that existed only to stop AccountabiliBuddy being bypassed lose their force: browser windows the extension can't see, and browsers whose extension isn't allowed in private windows, become nudges instead of minimized windows. The schema change reaches the Android companion, which reads the same document, so both sides need the migration. A large part of the enforcement code (escalation to minimize and redirect, scheduled sessions, unlock options, the looser-change delay) is removed; reversing this decision means rebuilding it.
