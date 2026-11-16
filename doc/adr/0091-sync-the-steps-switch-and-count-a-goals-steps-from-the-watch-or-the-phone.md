# 91. Sync the steps switch and count a goal's steps from the watch or the phone

Date: 2026-11-15

## Status

Accepted (owner, 2026-11-15: "Steps from: a standing choice per steps goal (All / Watch / Phone). Watch falls back to the phone's steps on a day the watch recorded none. The day shows which one it used."; "Steps switch: one synced switch for all phones (turning it off on one turns it off on all; goal history stays; each phone asks its own Health Connect permission when turned on)."; planner defaults accepted: "Q2 yes" (phone only means steps Health Connect marks as from a phone), "Q5 yes = only the main phone counts steps and sleep", "Q10 yes after a device check", "both modes"; round 2, N5: "Switching a steps goal between All / Watch / Phone recounts every day still covered (the steps cover window, up to 30 days), not only today and yesterday. Older days outside the cover keep their values.")

Amends [79. Sync goals between the user's devices](0079-sync-goals-between-the-users-devices.md) (a steps source gains one field, a Health Connect entry gains one class word, both inside the sealed goal). Builds on [90. Give each Android device a role in the group](0090-give-each-android-device-a-role-in-the-group.md), [61. Choose what syncs, row by row, on each device](0061-choose-what-syncs-row-by-row-on-each-device.md) and [86. Sync receipts per method and part](0086-sync-receipts-per-method-and-part.md).

## Context

- The PC offers two tiles, "Health Connect" and "Pixel Watch", but both add the same `HealthSteps` source, and `IsSingle` / `IsAdded` go by type, so once a steps source exists both show as added. The phone has one "Daily steps" row.
- The phone reads steps with `AggregateGroupByPeriodRequest(COUNT_TOTAL)`, which Health Connect de-duplicates by the user's app priority: one whole-phone total, no split between watch and phone.
- "Track daily steps" is a device-local switch (prefs `stepsSettings`). With several phones, each has its own.
- Goal counting takes the highest Health Connect value of any device and also treats the synced `steps` total as one more device.
- Health Connect's records say where steps came from only in two weak ways: `metadata.device.type` (watch, band, ring, phone, unknown) and the data origin (an app package; `"android"` before June 2026, a per-app synthetic name `com.android.healthconnect.phone.*` since). Health Connect's rate limits are not published; its changes API is the recommended way to follow records.
- Plan: `sr1-dr1-plan.md` and `sr1-dr1-plan-check.md` (B1, B4, B5, B6, B10, S1, S2, S11, S12).

## Decision

**"Track daily steps" is one synced switch.** A new optional sync part, `stepsTracking`, is `{"on","at","by"}` in that key order, appended after `deviceRoles`, null-omitted while the local value has no stamp. It is written by Android devices only; the PC neither writes nor carries it, and it is not in a QR file. Newest wins (`SyncStepsTracking.Merge`): a tie goes to the higher `by`, then to off (the safer side for health data); a value whose stamp is over 2 minutes ahead of the receiver's trusted now is ignored until the receiver's time catches up (nothing is stored capped, so a stored stamp never moves); stamps come from `TrustedClock`; times compare as instants. It rides the ADR 61 row that covers `steps`: withheld there, it neither goes nor is taken. Receipts report it under the existing `steps` code.
- **Upgrade.** A device whose switch is on at the upgrade stamps it `2000-01-01T00:00:00Z` (as `GuidanceState.UpgradeStamp` does); an off or fresh switch is never sent. A phone that tracked steps spreads "on" without a tie turning anyone off, and any real choice beats it.
- **Receiving on:** the switch turns on and the existing card says "Allow Health Connect steps". A receiver never opens a permission dialog by itself; each phone asks for its own Health Connect permission (still `READ_STEPS`). **Receiving off:** the same as turning it off by hand: stored counts are forgotten, goal entries and history stay.
- **Only a Main or Unassigned device reads** (ADR 90). A tablet or other device shows the synced switch and never asks.
- The switch is not a guard: it applies at once and is never held during a session.
- **Backup restore.** A settings restore that changes the switch stamps it as a local edit, now. `deviceRoles` is never in a backup.
- **Works alone.** With no transport the switch stays local and behaves as today; `stepsFrom` and the split below work on a single phone with no group.

**A steps source on a goal carries `stepsFrom`.** `GoalSourceRule.StepsFrom` (`GoalStepsFrom`: `Watch` or `Phone`) is appended after `createdOn` and written by name, in the casing the goal enums use. Null means **all**, the whole-phone total that is today's count, and is omitted, so known-only JSON stays byte-identical. It is a field, never a new `GoalSourceType`: a new type would make older apps hide the source. An older app keeps `stepsFrom` in the rule's unknown fields (GS0) and counts the rule as all. An unknown value from a newer app makes the rule a kept newer item. There is one steps source per goal (`IsSingle` stays): choosing the other tile or row changes the existing rule's `stepsFrom`, never adds a second rule.
- **Tile and row mapping, both platforms** (plan check B10):

  | `stepsFrom` | Health Connect tile/row | Pixel Watch tile/row | Segmented control | Row text |
  |---|---|---|---|---|
  | null (All) | Selected | "Use this" | All | "Health Connect · steps" |
  | Watch | "Use this" | Selected | Watch | "Pixel Watch · steps (your phone's on days without watch steps)" |
  | Phone | Selected | "Use this" | Phone | "This phone · steps" |

  A tap on a selected tile does nothing; the button says "Selected" or "Use this", never "Added"; every switch restamps the Sources part once; with no steps source both tiles say "Add" and add the rule with that tile's `stepsFrom`.

**The day says which class it used.** The goal's own Health Connect entry gains an optional `stepsFrom` word (`GoalEntry.StepsFrom`, appended after `deviceId`, null for all, omitted): the class actually used, `Watch` or `Phone`. One class word inside the sealed entry; never an app or package name. For `Watch` the day's value is the wearable class when it has any record that day, else the phone class when it has any (the day then holds `Phone` and the panels say "from your phone (no watch steps that day)"), else no data. Today may flip from phone to watch when the watch's data lands later; yesterday is settled at its re-read. Older apps keep the word in the entry's unknown fields (GS0, matched by entry id).

**Counting** (`GoalCounting`, C# and Kotlin; SR1-S1 review BLOCKER-1 and SHOULD-5):
- The synced `steps` total counts only while every automatic steps rule of the goal has `stepsFrom == null`. A watch-only or phone-only goal counts its Health Connect entries only.
- **Per day, with no date in the rule.** For a goal whose steps rule picks Watch or Phone, a day that has at least one Health Connect entry with a **matching** class word counts only matching entries; a day with none counts its plain (word-less) entries and ignores entries with another word. A Phone rule matches the word `Phone`; a Watch rule matches `Watch` and `Phone` (a `Phone` word under a Watch goal is the fallback day). So once the main phone has written the class value for a day, no older "all" entry (an older phone's, a tablet's from before roles) can ever beat it, however old the day gets; and a day the phone never recounted keeps its value (owner N5: "older days outside the cover keep their values"). Nothing depends on today's date, so history never changes as days age, and the PC and phone cannot disagree at a boundary. The rule does not compare a Watch entry's word with a Phone goal beyond this: a Phone goal never counts a Watch word.
- The `steps` field of the sync file stays the whole-phone, de-duplicated total, whatever the goals pick.

**How the phone works out Watch and Phone** (a pure Kotlin split, `StepsDeviceSplit`; the PC never reads Health Connect). From raw `StepsRecord`s, held in memory only:
- *Class of a record* (first match wins): `device.type` WATCH, FITNESS_BAND or RING is **wearable**; PHONE is **phone**; the origin `"android"` or an origin starting `com.android.healthconnect.phone.` is **phone** (the API `HealthConnectManager.getCurrentDeviceDataSource()` is preferred where it exists, the prefix is the fallback: Health Connect asks that SPN values are not hard-coded); otherwise **unknown**, which counts only in all. "Phone only" counts steps Health Connect marks as from a phone, as the owner accepted.
- *De-duplication* (plan check B5, replacing the plan's per-segment maximum, which could nearly double a count when two writers use different granularity): per class and per day, sum each data origin's records (a record spread over midnight in proportion), and the class value is the **highest single origin's total**, floored to whole steps at the end. Two apps mirroring the same watch count once. One origin that writes both phone and watch records is split correctly because the class is decided per record. Limit, accepted: two different wearables recording different hours of one day under different origins under-count.
- *No data:* a day with no record of the class is no data, never 0.
- *Privacy:* origins and package names exist only in memory during the call. They are never stored, logged, shown outside a debug build, or synced. No new permission, connection or dependency. The Fitbit / Pixel Watch app fallback (owner: yes, after a device check) is a fixed list in code, never synced, added only after the hand test shows what the owner's watch writes.
- *Reading:* only when a live goal uses `watch` or `phone`, at the existing pace (170 s in front; in the background only from the sync worker with the background permission); the Health Connect changes API or a logged `RateLimitExceeded` without a retry storm (rates are unpublished); "all" keeps the aggregate call unchanged.

**The recount window** (owner N5, replacing plan check B6's "today and yesterday"). Switching a goal between All, Watch and Phone recounts every day still covered by the steps cover window (up to 30 days, `StepsStore.KEPT_DAYS`), not only today and yesterday; the days older than that keep their values. The derived writer removes a device's own entry for a covered day the read no longer has, so the read's cover is exactly the days the class read returned this run; an entry outside it is left alone (a restart that re-reads only some days removes nothing). The reader therefore reads raw records for the whole window when a goal's choice changes, and caches closed days in memory afterwards.

## Consequences

- Both PC tiles now mean something, and a watch-only goal is honest about days the watch missed. The price is a heuristic: a merged phone-plus-watch writer that marks all its steps as PHONE puts watch steps into "Phone"; the A3 hand test compares with Health Connect's per-app view before this is relied on.
- Until the main phone updates and re-reads, a day of a Watch or Phone goal still shows its old "all" value instead of "no data"; once the phone writes the class value for that day it takes over for good. The hand test covers it.
- A change of source recounts up to 30 days: a heavier first read (one or two pages a day, short-lived objects, measured and logged as counts only). Closed days are cached.
- The switch syncs, the permission does not: a tablet or other device never asks.
- Reversing is cheap while no screen depends on it: `stepsFrom`, the entry word and `stepsTracking` are optional and omitted when unset; older apps ignore or keep them.
