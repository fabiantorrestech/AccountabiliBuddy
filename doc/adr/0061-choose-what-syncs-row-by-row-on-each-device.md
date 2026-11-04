# 61. Choose what syncs, row by row, on each device

Date: 2026-11-03

## Status

Proposed

## Context

Each device writes one sealed file (ADR 22) and every method gets the same contents. The owner wants a Settings › Devices & sync › What syncs page: for each kind of data, on or off and which methods carry it, with everything on by default (plan: Shutdown Routine and Sync Settings Plan, Task 2). The file was not built for this. Readers cannot tell "not sent" from "none", some fields hold guards up, and files are forwarded by devices that cannot open and trim them.

What the code does today, which sets the limits:

- **A receiver replaces its copy of a sender's part** (`SyncMerge`). Only `DayPlan`, each `Documents` entry and `Categories` survive being left out. Every other optional field becomes null in the copy, and a file without `Sessions` and `CoversFrom` says the last 14 days are empty, so receivers delete them.
- **Shared lists never read absence as deletion** (`SharedListMerge`). Only a tombstone deletes, and items one side lacks are kept, so `null` and `[]` both leave a receiver's list alone. But the difference between empty and null has meaning elsewhere. `parked != null` is how a phone knows a PC has shared lists (`ReceivedDevice.hasSharedLists`; without it, it falls back to legacy come-backs), and `later != null` is how a receiver knows a writer has ADR 50's fields (`knowsDetails`; without it, its tasks inherit the receiver's notes and estimates). Null would trigger both old-app paths.
- **Two precedents exist.** `ForQr` already sends less by one method. `Steps` is inferred by method (`method == QR` keeps the stored counts) because null also means "switched off, forget them". Inferring does not extend to a device choosing per kind.
- **Fields drive guards.** `Running` drives the phone's guards and also its settings locks (`PlacementSession.current`, used for device removal, restore, the Guards page, Essentials, Curbox), and other PCs' `OtherPcSessions`. `Events` become guarded windows on the phone (`CalendarEventSources.carriedFrom`, ADR 58). `Sessions` and `Drift` feed the phone's Budget windows through `FocusDayCombine` and `DayWorkWindows`. `NextAlarm`, `SmartBedtime` and `WakeEstimate` set the PC's sleep schedule (`SleepSettingsService`, `ToSchedule`). `Placement` drives the PC's nags and the pocket's loss of phone-free credit. `Lunch` steps a session away and can lift the phone's lock.
- **The plan's worry about `SyncCarry` is only half right.** `SyncCarry` forwards the sender's sealed bytes unchanged, so it cannot re-send anything the sender left out of a later file. The real re-send is the phone's own file, which publishes its merged lists and `lunch()` whatever their origin.
- **`OwnFileWriteGate` is not a conflict.** Each phone source owns one gate, and the PC already builds and seals a file per method.

## Decision

We will add a What syncs page with one on/off per row, on each device, applying to all methods. We follow the plan's two steps: this ADR settles step one, and per-method choices are a later step with its own decision, built only if a real need appears.

**Scope.** The choice is device-local: a `sync.whatSyncs` document on the PC, a settings file on the phone; never in the sync file's shared settings and never in the profile schema. A missing row means on, so nothing changes until someone turns something off. A row off on a device means it neither sends nor takes that kind. The rule goes in one pure function on each platform, applied where everything already passes: `BuildOwnFileOnUiThreadAsync` and `ownFile()` going out, `AcceptOnUiThreadAsync` and `acceptSealed` coming in. Filtering before the gate's hash means an off row causes no writes.

**How "off" is written.** Shared lists are sent as `[]` (with `Comebacks` null), so the has-lists and knows-details signals stay true. Closed sessions and drift are left out with `CoversFrom` set to the write time, so any receiver, old apps included, keeps what it holds. `HistoryOff` keeps its meaning (receivers delete, ADR 15) and is never used for this. Everything else is left out. A new optional `Withheld` list names the kinds off on the sender. The format stays 1 and old apps ignore it. Receivers use it to keep their stored `Steps` and `Pickups`, which null would otherwise wipe, and to say on the Devices page that a device isn't sharing a kind. For `Steps` this does in general what the QR inference does today; `PhoneSteps.MaxAge` still ages the counts out.

**A receiver keeps stored data, except live state.** Lists, settings, the plan, history, categories, steps and past pickups stay. Live state (the rows marked Drops below) goes null as it does today, because a stale copy under a fresh file would keep applying: `Events` freshness is judged by the file's write time, so kept events would shape guards indefinitely. The phone keeps its own stored events for display.

**Rows.** Fixed rows are not on the page. Locked rows refuse any change during a focus session the user started; turning one off waits the looser-change delay with Cancel and no "apply now", by `TrustedClock`, and turning one on applies at once. Free rows apply at once between sessions. The whole page rests during a session, like the Devices page today.

| Field | Written by | What relies on it | If it stops | Class (page row) | Receiver when off |
| --- | --- | --- | --- | --- | --- |
| `FormatVersion` | all | Readers refuse newer | File unreadable | Fixed | n/a |
| `Device.DeviceId` | all | Identity, file naming, merge | File refused | Fixed | n/a |
| `Device.Name` | all | Devices page, notices | No label | Fixed | n/a |
| `Device.Kind` | all | Every PC/phone filter | Consumers misfire | Fixed | n/a |
| `Device.AppVersion` | all | `supportsControl`, `PocketCostsCredit` | Treated as too old | Fixed | n/a |
| `Device.WrittenAt` | all | Merge order, freshness, ADR 60 | Merge breaks | Fixed | n/a |
| `Device.Methods` | all | Devices page | Row wrong | Fixed | n/a |
| `Device.HistoryOff` | all | Deletion on receivers (ADR 15) | Delete not honoured | Fixed | n/a |
| `Device.Seen` | all | QR reminder | Reminder wrong | Fixed | n/a |
| `Device.Companions` | phone | Watch row on the PC | Watch row gone | Fixed | n/a |
| `Device.PublicKey` | all | Rekey wrapping (ADR 54) | Device cut off by next removal | Fixed | n/a |
| `CoversFrom` | PC | Merge range | Receivers delete history | Fixed | n/a |
| `Running` with `OffSince`, planning | PC | Phone guards and settings locks, other PCs, countdown, watch | All of those lift at once | Fixed (see below) | n/a |
| `Sessions`, the open piece | PC | Session start for placement, running checklist on phone and watch | Checklist gone, placement misdated | Fixed | n/a |
| `PastKeys` | all | Opening old backups (ADR 56) | Backups unopenable | Fixed | n/a |
| `Withheld` (new) | all | This decision | Receivers can't tell | Fixed | n/a |
| Rekey file `ABR1` (own file, not a field) | all | Removals (ADR 54) | Group splits | Fixed | n/a |
| `Sessions`, closed | PC | History, totals, phone Budget windows, pickups | Budget stays unmet; no new pickups; turning back on can lift | Locked (Focus history) | Keeps |
| `Drift` | PC | Phone focus totals, Stats | Totals change | Locked (Focus history) | Keeps |
| `DayPlan` | PC | Phone guards, other PCs (clash rule) | Other devices keep the old plan | Locked (Day plan) | Keeps |
| `Documents.profiles` | PC | Phone's and other PCs' rules | Others keep old rules | Locked (Focus profiles) | Keeps |
| `Documents.sleep` | PC, phone | Sleep guards everywhere | Others keep old settings | Locked (Sleep) | Keeps |
| `Device.NextAlarm` | phone | PC wake time | PC wake and bedtime shift | Locked (Sleep) | Drops |
| `SmartBedtime` | phone | PC bedtime | PC bedtime shifts | Locked (Sleep) | Drops |
| `WakeEstimate` | phone | PC wake | PC wake shifts | Locked (Sleep) | Drops |
| `Placement` | phone | PC nags, phone-free credit | No nags, pocket costs no credit | Locked (Phone placement) | Drops |
| `Pickups` | phone, PC | Phone-free credit, History line | No credit for new sessions; a held count of 0 would stand for a running one | Locked (Pickups) | Keeps |
| `InputRuns` | PC | "Straight from that PC" notice (PC only; Kotlin has no field) | No notice | Locked (Pickups) | Drops |
| `Lunch` | PC, phone | Steps a session away, lifts phone lock, day bar | Lunch stays on one device; lift doesn't happen | Locked (Lunch) | Drops (own store stays) |
| `Events` | PC | Phone's guarded windows (ADR 58), phone's calendar | PC-carried windows vanish | Locked (Calendar events) | Drops (phone's display copy stays) |
| `Actions` | phone | Phone's ticks, start, end early, plan edits on a PC | Phone can't act on the PC | Locked (Control from the phone) | Drops |
| `Acks` | PC | Clearing the phone's pending actions | Actions wait until they expire | Locked (Control) | Drops |
| `ActionResults` | PC | Phone's answers | Phone gets none | Locked (Control) | Drops |
| `EndEarly` | PC | End early from the phone | PC only | Locked (Control) | Drops |
| `ProfileOptions` | PC | Phone's start picker | Picker empty | Locked (Control) | Drops |
| `DefaultProfileId` | PC | Quick Start from the phone | Button gone | Locked (Control) | Drops |
| `NextTasks` | PC, phone | Next-session list | Lists diverge | Free (Next tasks) | Keeps |
| `Parked` | PC, phone | Come-backs | Lists diverge | Free (Come-backs) | Keeps |
| `Comebacks` (legacy) | PC | Phone 0.1.62 | Old phone shows none | Free (Come-backs) | Drops |
| `Later` | PC, phone | Later list | Lists diverge | Free (Later) | Keeps |
| `Categories` | PC | Pickers elsewhere | Other devices don't offer them | Free (Task categories) | Keeps |
| `Documents.daybar` | PC | Day bar look | Others keep old | Free (Day bar) | Keeps |
| `Documents.goal` | PC | Goal; lowering waits locally | Others keep old | Free (Daily goal) | Keeps |
| `GoalMinutes` | PC | Phone Stats, watch | Goal missing there | Free (Daily goal) | Drops |
| `ReminderActions` | PC, phone | Dismissing a reminder elsewhere | Others still ring | Free (Reminder dismissals) | Drops |
| `Blockers` | PC, phone | Listing blocks in the rules editor; each device enforces its own (ADR 37) | Others can't pick those blocks | Free (Blocker list) | Drops |
| `Beeminder` | PC | Phone's goals | Goals gone on phone | Free (Beeminder) | Drops |
| `Noise` | PC | Watch's device switcher | No switcher; commands travel as `Actions` | Free (Noise player) | Drops |
| `Steps` | phone | PC's steps card | Stale, then none | Free (Steps) | Keeps |
| `Reviews` (new, see below) | PC | Stats over time elsewhere | Review stays local | Free (Shutdown reviews) | Keeps |

That is 17 fixed fields, 19 locked fields on 9 page rows, and 14 free fields on 12 page rows. A device has a row for a kind it never writes, since it can still refuse to take it. A row that can't apply (a feature off, or Focus history while Activity history is off) shows grayed out with the reason.

**`Running` and the open session piece are fixed, not locked,** where the plan allowed either. Turning sync off already has a looser-change wait, but it costs the user everything. A session-state row would remove guards and settings locks for every later session while everything else keeps working, for a 10-minute wait. It would be a clean way out, and it protects no privacy that Focus history doesn't.

**Merging after a row returns.** Re-enabling sends the whole list and normal newest-edit-wins applies, so nothing is lost. Tombstones live 30 days, so a row off longer can bring deleted items back, the same as a device away that long. The page says so past 30 days.

**Phone and carrying.** The phone honours the page through `ownFile()` and the incoming filter. Carrying stays blind: `SyncCarry` forwards sealed bytes even for kinds off on that phone, since trimming would mean re-sealing another device's file, and `SyncMerge`'s rule is that a carrier passes on only the sender's own sealed bytes. An off row keeps that text out of the phone's stores while it stays in the carried sealed bytes.

**Older apps.** They ignore `Withheld` and read left-out live fields as none, as they read `ForQr` files. Lists and history are safe by the encodings above. They cannot honour a row, so an older device keeps publishing whatever list items it already holds. Turning a row off here stops this device taking part and recalls nothing.

**The shutdown review.** It is a free row, on by default per the owner, in a new optional `Reviews` list: date, feeling, note. Items take stable ids, edit times and tombstones so they use `SharedListMerge`, and are read-only away from the writer. Like the lists it is left out of the stored copy, kept in a sealed store on arrival (DPAPI `shutdown.reviews`, Keystore on the phone), and left out of QR files, backups and the watch. It is the owner's free text, so `doc/hard-rules.md` must list it as an explicit exception to "notes never leave the device", as ADR 50 did for task notes. Until this ADR is built, reviews stay local.

## Options considered

| Option | Why not |
| --- | --- |
| Group-wide choice, travelling as a shared setting | Would stop the merged-list re-send everywhere, but one device could then change another's rows, including locked ones, by newest-edit-wins, and the switch itself needs a fixed channel. Old apps still ignore it. |
| Send-only rows (off stops sending, still takes) | Doesn't keep a kind off a device such as a work PC; its stores would fill from other devices' files. |
| Per-method from the start | See below: needs carrying rules every device must honour, and its benefit is unproven. |
| A separate file per kind | Breaks ADR 22's one file per device and every older reader. |
| Null for every off kind | Trips `hasSharedLists`, `knowsDetails` and history deletion, as shown in Context. |

**Per-method choices, step two.** The blockers are not the gate. Carrying is the first: a phone forwards a file to any method other than the one it came by (`it.method != cameBy`), so a kind kept off Drive reaches Drive through the phone, and only a scope mark every carrier honours would stop that, which older phones won't. The copy is second: `SyncMerge` keeps one copy per sender and the newest file wins, so a narrower newer file flips live fields on and off, which rules out per-method on any Drops row. `Seen` is third: it records a time per device, not per kind, so "has the latest" overstates. Delivered marks (`SyncOffWait`) are fourth: a method that carries only a narrow file would count as a way of syncing, so locked rows must stay on every method. Relay push is unaffected, since it only schedules the same write. Per-method could therefore apply only to free Keeps rows, with a minimum app version. Its benefit is also thin: every method holds only AES-GCM sealed files that neither Google nor the relay can read, so it gains no confidentiality (reasoning only, from ADR 22 and 46).

## Consequences

Nothing changes until a row is turned off. A device can keep a kind off itself, and this page cannot lift a guard without the wait. A row off never recalls what others already hold, and other devices keep sending what they have, so the page must say "this device" plainly. With Focus history off, the open piece of a running session still travels and its closed version doesn't, so receivers keep a session cut short at their last file.

Step one commits us to: the `Withheld` field; the row list and its classes; the `[]` and `CoversFrom` encodings, which old apps depend on; blind carrying; and the rule that fixed and locked rows travel by every method. It leaves room: `sync.whatSyncs` stores `{ on, methods }` per row with `methods` null meaning all, so step two needs no migration. It commits to no per-method file. Reversing means ignoring `Withheld` and treating all rows as on; the file format doesn't change.

Every new sync field must now take a class in this table. `doc/hard-rules.md` records `sync.whatSyncs`, `Withheld` and `Reviews`. The profile schema stays at v13. No `SetupFeatures` entry, as the default needs no setup.

## Questions for the owner

1. **Is a row per device, or for the whole group?** Recommended: per device. Group-wide stops the re-send but lets one device change another's guards.
2. **Is session state fixed on?** The plan allowed fixed or locked. Recommended: fixed, for the reason above.
3. **Is per-method still wanted, given sealed files gain no privacy from it?** Recommended: build step one and drop per-method unless a concrete need appears; if built, only free Keeps rows.
4. **May the review note leave the device?** Today's hard rule says notes don't. Recommended: yes, to the user's own devices only, sealed, never to the watch, on by default as decided on 2026-11-02.
5. **Should turning a row off also erase what other devices hold?** Recommended: no. The plan's verification wants "loses nothing", and an erase command travelling between devices is a new way to lose data; ADR 55's erase stays the only one.
