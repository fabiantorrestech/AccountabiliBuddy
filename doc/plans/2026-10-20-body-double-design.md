# AccountabiliBuddy as a body double: design

Date: 2026-10-20. Status: **draft for the user's review** (not approved; nothing in parts B–E is built).
Decisions marked *(user)* come from the user in chat on 2026-10-20; *(my call)* are defaults they can change; **Open**
items are listed at the end.

## Intent

AccountabiliBuddy (AB) is a body double: someone sitting next to you who notices when you start drifting, asks
about it kindly but firmly, and keeps score of the time you really focused. It does not enforce. Blockers do that:
Cold Turkey on the PC and Curbox on the phone are the police; AB hands them what it learns (new time-wasting sites)
so their block lists get better over time *(user)*. The goal is the habit of catching yourself, for someone with
ADHD: enough friction to stop impulses, never so much that a real need (a gift, a ticket sale, a limited drop) is
blocked *(user)*.

**Success** *(user)*: fewer drift minutes in Work blocks (the main sign of self-awareness) and more focus hours.
Time on a blocker's block page is not drift; repeatedly trying blocked sites shows up as attempts.

**Anti-goals**: AB blocking you from something you genuinely need; AB touching your own Cold Turkey blocks; a
buddy so naggy you ignore it; losing an impulse instead of parking it; sending anything off your devices without a
decision record.

## Parts and order *(user: day plan first)*

| # | Part | State |
|---|---|---|
| A | End on command | **Built (0.1.41)** |
| B | Day plan and timeline | B1 built (day plan, editor, one-off days, sleep from the plan, phone reads v5); flexible lunch, B2 timeline and B3 phone screens next |
| C | The buddy: catching drift, can-it-wait, parking, check-ins, blocker hand-off, Blocks tab | Designed |
| D | Session plan (checklist) and reflection | Designed |
| E | Progress and targets | Outline |
| F | AI (optional, later) | Outline |

## A. End on command (built)

Only a session that counts down to a set end keeps the end-early wait and reason. A scheduled block (the blocker
is the guardrail) and a session with no set end (count-up Quick Start, the Session tab's "No set end") end at once,
with no wait or reason, also from the phone and when quitting *(user)*. `BypassPolicy.EndsOnCommand`.

## B. Day plan and timeline

- **Parts of the day** *(user)*: Work, Lunch, After-office work, Sleep, each with set hours. After-office work counts
  as Work (asks about new sites, counts toward targets) *(user)*.
- **Workdays vs other days** *(user)*: one plan for workdays and one for the other days; which days are workdays is
  configurable (Mon–Fri by default, *my call*).
- **Categories** *(user: add your own)*: each part of the day belongs to a category with a name and a colour, e.g.
  **Work** (the day job) and **Extra Work** (your own time: side projects, coding, resume, home cleanup, chores). How
  a category behaves *(user)*: each one acts like one of three behaviours:
  - **Watch**: the buddy asks about new sites and counts drift (Work, Extra Work).
  - **Rest**: no asking; parked "Today" items come up (Lunch, Evenings, Free time).
  - **Sleep**: bedtime reminder and the shaded night; folds in today's sleep mode.
  Starting set: Work and Extra Work (Watch), Lunch, Evenings and Free time (Rest), Sleep (Sleep); you add, rename,
  recolour and delete your own.

**Status: approved by the user 2026-10-20.** Next: the four ADRs, then the build plan, starting with part B.
- **Editing the plan** *(user, from mockups)*: tabs for Workdays and Other days, day chips (Mon–Sun) for which days
  are workdays, a 24-hour bar whose parts you drag (snaps to 15 min; click empty space to add, click a part to
  select it) and, under it, the selected part's name, kind and exact times (all parts listed when none is selected).
  Bar and list stay in sync.
- **One-off days** *(user)*: a single day's plan (a day off, a half day) is edited in the Exceptions tab with the same
  editor, starting from that day's usual plan. *(my call, as built in B1)*: the old time off and one-off blocks
  are dropped with the grid at the v5 migration, not converted (blocking is the blockers' job, ADR 18).
- **Flexible lunch** *(user, 2026-10-20; next after B1)*: lunch often moves with medication hyperfocus, so Lunch is
  a flexible break rather than fixed hours: a "Start lunch" button (any time, on the PC or the phone) starts a timer,
  1 hour by default, kept in sync between PC and phone; when it's over the PC rings once and pops up "Lunch over!",
  and the phone notifies the same. If lunch hasn't started by 1 PM, AccountabiliBuddy hints to take it. Its usual
  window is 11 AM–3 PM: the buddy and anything tied to the blocks plan around lunch falling in that window, but it
  can be started any time. *(my calls, to confirm)*: in the plan a flexible break has a window, a length and a hint
  time instead of fixed hours; until it starts, that time counts as the surrounding Watch part.
- **Replaces the weekly grid of profiles** *(user)*. What blockers enforce is shown from the blockers themselves:
  toggles lay Cold Turkey's block schedule and/or Curbox's (phone) blocks over the day plan *(user)*.
- **Timeline** *(user)*: today it shows the profiles' blocks (from Cold Turkey); it should show the day plan for the
  day. Layout *(user, from mockups)*: **separate labelled rows**, since one shared bar gets cluttered: **Calendar**
  (events from the linked calendars you choose for it *(user)*; titles on hover, *my call*), **Plan** (the day's
  parts), **Focus** (sessions you started, with drift marks and the focused total), and **Blockers** (Cold Turkey's
  blocks and/or Curbox's, each toggled on or off). That order is the default; Settings lets you reorder the rows top
  to bottom. All rows share one hour axis and the now line.
- **On-screen window** *(user)*: its all-day timeline uses the same four rows.
- **Phone** *(my call)*: Today shows the same rows; the pinned notification and the widget keep one slim bar (Plan
  with Focus on it), since they have no room for four.
- **Shared with the phone**: the plan travels with the PC's settings over the link (L2), so the phone's timeline
  and notifications follow it. If it lives in the shared profile document, that is a schema bump (v5) with a
  migration (ADR 7) *(my call: put it there, since the phone needs it)*.

## C. The buddy

**New site in a Work block** (in a focus session or not) *(user)*:
1. After **90 s** on a site AB hasn't seen before (30 s inside a focus session you started, *my call* for
   "stricter"), it asks: **"Is this productive?"**
2. **Yes**: you write the exact reason. The reason stays on the overlay, and AB checks back every **2.5 min**. Two
   ignored check-ins count as drift and the nudge gets stronger.
3. **"Can this wait until after Work?"**:
   - **Park it**: one tap saves the page to the parked list (below).
   - **It can't wait**: pick **5 / 10 / 15 min** or **until it's done**. When the time is up, one "Done?"
     check-in; "+5 min" once without questions; a second extension or an ignored check-in counts as drift and asks
     for a new reason; three overruns on the same site suggest a shorter time next time *(user: yes)*.
   - **Until it's done** (queues, ticket sales): no end time; a check-in every 2.5 min ("Still in the queue?",
     one tap) on whichever device you're using, the PC overlay or a phone notification, so you know you're still
     tracked *(user)*. Switching tabs or apps doesn't end or reset it: the timebox belongs to the reason, not the
     tab. Nothing is ever closed or blocked by AB; ignoring check-ins is logged as drift *(user)*.
4. **No**: the site goes on a list you review at the end of the Work block *(user)*; what you approve is added to
   AB's own Cold Turkey block (below).

**Drifting with no focus session running** *(user)*: AB suggests taking 4–5 minutes to plan a focus session (part
D's checklist) and checks in (every 2.5 min, *my call*) until the plan exists.

**AB itself never blocks or minimizes** *(user: blockers enforce)*. It asks, nudges (toast, then overlay) and logs.
Today's minimize/redirect enforcement is removed, which changes CLAUDE.md's hard rules: **needs an ADR**.

**Parked impulses** *(user)*:
- Labels **Today** (reminder at the end of Work), **Soon** (you pick a day, reminder then), **Someday** (fun stuff,
  hobbies, side projects; no reminder). A quick park is Someday; one tap changes it.
- Shown in their own tab on the PC and on the phone, with a **silent** notification on the phone (just there to look
  at); only Today and Soon remind.
- The full link goes to the phone so it can be opened there *(user)*: **extends ADR 15** (today full addresses never
  leave the PC).

**Hand-off to Cold Turkey** *(user)*:
- AB keeps its own block, **"A.B. - Work"**, so it never touches your blocks. Cold Turkey can't create a block or
  set its schedule from outside, so the **setup assistant asks you to create it once** in Cold Turkey and give it
  your Work hours.
- AB adds approved sites with Cold Turkey's command line (`-add "A.B. - Work" -web <site>`). It has no remove, so
  removals are listed for you to do in Cold Turkey. It can't add apps (only sites and exceptions).
- **Changes must reach Cold Turkey as soon as possible** *(user)*. To verify: whether a `-add` shows up in a running
  Cold Turkey at once or needs a restart. That needs one test on a throwaway block with your OK (never your real
  blocks). Writing to another app is a new integration: **needs an ADR**.
- Curbox: the same hand-off once the API change in your copy of Curbox is done (add/remove items; PR drafted
  later).

**Profiles become "Blocks"** *(user: rename, a glance at what's enforced elsewhere)*: the tab shows each Cold Turkey
and Curbox block (name, on/off, schedule, sites), read only, plus AB's own block and the review list. What AB uses
to tell on-track from off-track: see Open.

## D. Session plan and reflection *(user)*

- **Plan at the start** (optional): a short checklist. Ctrl+Alt on the overlay shows it; click an item to cross it
  off. When every item is crossed off, the overlay glows and offers "End session".
- **Pin one item** to the overlay so you always see what you're on; pin and unpin from the overlay's menu.
- **When the session ends**, all optional:
  - Reason for ending, filled with "All tasks completed" when every item was crossed off.
  - **Notes** with a light format *(my call)*: "Done:", "Not done / next:", "Got in the way:".
  - **How it went**: 1–5 with faces from sad to happy *(my call: 1–5 is quicker than 1–10)*, with one line of
    guidance per face (1 = mostly drifting … 5 = deep focus the whole time).
- Text (items, notes, reasons) is stored encrypted like other reasons (ADR 8); the rating and counts are readable.

## E. Progress and targets *(user)*

- A daily deep-work target, **4 h by default** (Cal Newport's ceiling for a trained mind), adjustable; time beyond it
  is extra credit.
- A graph over days and weeks: focus time, drift minutes in Work blocks, blocked attempts and device/context
  switches, with the target line. Session ratings alongside.
- Absorbs the backlog's progress heatmap.

## F. AI (optional, later) *(user: yes, rules first)*

- Works fully without AI first: questions, parking, timeboxes and habit counts (overruns, parked items never
  reopened, reasons that keep coming back) are what catch impulses.
- A **`buddy.md`** the user edits ("flexible, knows I have ADHD, firm on shopping during Work, never blocks real
  emergencies"): the wording AB uses; with AI on, the AI's instructions.
- Providers: none (default); a model on this PC (data stays local); an online model of your choice with your key
  (opt-in, shows exactly what is sent: **needs an ADR**). Best use: "the reason says concert tickets, but the page
  is a mouse review thread".

## Builder notes *(my calls, from reading this as the builder)*

- **Drift** = time in a Work part on an off-track site, on a new site after its 90 s without an answer, or after an
  ignored check-in. Time on a blocker's block page is an attempt, not drift.
- **The 90 s** count time on that site in a row; leaving and coming back within 2 min continues the count.
- **Check-ins on the PC** come as a small clickable prompt next to the overlay (the overlay itself stays
  click-through), like today's classify prompt; on the phone, as a notification with action buttons.
- **Storage**: parked items, checklists and notes live in the local database; the link, note and item text are
  encrypted (ADR 8), the label, dates, counts and rating are readable.
- **Learning from habits** without AI: per site, count overruns, extensions, parked items never reopened and how
  often each reason repeats; these change defaults (shorter timebox, ask sooner) and feed part E.

## ADRs needed before building

1. AB stops enforcing (nudges and logs only); blockers enforce. Changes the hard rules.
2. Writing to Cold Turkey through its command line (and later Curbox through its API).
3. Parked full links to paired devices (extends ADR 15).
4. Online AI provider (part F, later).

## What counts as off-track *(user)*

A site is off-track if it's in any of your blocker lists (read from Cold Turkey and Curbox), if you answered "No",
or if you answered that it isn't relevant to the task or work you're on; on-track if you gave a reason or allowed
it; new otherwise. Today's AB profile rules become that allow/watch list.

## Also decided *(user)*

- Cold Turkey's command line was tested on a throwaway "AB Test" block (2026-10-20): `-add "AB Test" -web
  "example.com"` reached the running Cold Turkey at once and it saved its settings within ~2 s; no restart needed.
  The command line still can't remove sites, create blocks or set schedules.
- The session checklist, pinned item and check-ins also show on the phone during a PC session.

## Open

- Day plan and timeline details, with mockups (next round).
