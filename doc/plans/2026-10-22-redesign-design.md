# Redesign: get started, keep tasks in front, see the day grow

Date: 2026-10-22. Status: **approved (see the end); nothing built yet.** Mockups: the "AccountabiliBuddy Redesign Mockups" design canvas, https://claude.ai/artifact/UxxSFk6xM965mtKZg8NATj (private to the user; source in the gitignored `handoffContext/mockups/`).

## Intent

The app's job is to **get the user started** on whatever device they walk up to, **keep the current task in front of them**, and **show the day growing** toward a deep-work goal. It must work simply and reliably on the work PC every day, with as few choices as possible at the moment of starting (ADHD: too many options is what stops the start).

## What the user said (2026-10-22, condensed)

1. Walk up to any device, have it steer me toward starting: plan a few tasks, start a session. Derailment happens at the beginning. Blockers are too blunt; keeping their lists current is a chore. Phone = temptation but also visible progress; watch = bathroom-break glance and simple session controls; a paper notebook holds working notes. Mid-session thoughts: most may drift (internal distractions), some must not (doctor's notes, time-sensitive replies).
2. Mid-session fiddling was the overlay and the task list. Tasks want their own quiet place. The timeline should sit on the first page only. The first minutes of a session are a bombardment of "is this on track?". Setup is the biggest pain.
3. Rank: a) get me to start, b) keep tasks in front of me, c) show how the day went. Plus: detect new distractions and help add them to blocklists; track sessions with metrics. Just the user for now.
4. Weekly tabs: Session, Profiles, Day plan, Exceptions (rename: Calendar). Sometimes: Devices, History. Almost never: Setup, Integrations, Settings, About. One app everywhere; the work PC is the bar.
5. "Working" = today's progress growing toward a daily goal (≥1 h deep work, 4+ on great days, trained up over time), progress over days, a focus-over-time graph with a research-grounded y-axis.
6. Google Drive is the sync method, with in-app Google sign-in on the phone (B). The Wi-Fi link is dropped. QR codes and shared files stay as a fallback. The window shows only the essentials while a session runs.

## Goals, non-goals, anti-goals

- **Goals:** one obvious way to start on every device; the current task always visible; a daily goal that fills; prompts that don't interrupt the first minutes; setup that reaches sensible defaults in three questions; sync that just works at work.
- **Non-goals (for now):** a self-hosted or local sync relay; multi-user; flexible lunch; Curbox data; open-sourcing.
- **Anti-goals:** another settings page; a feature that needs tuning mid-session; anything that makes the phone more interesting to pick up; losing the privacy rules (project `CLAUDE.md` hard rules stay, amended only where this doc says).

## Constraints

- Core stays free of UI and OS code (ADR 4). Profiles stay schema-versioned for the phone (ADR 7). Privacy per ADR 8, 15, 22, 24.
- Stricter changes apply at once, looser ones wait; focus-session locks stay (project `CLAUDE.md`).
- Everything here is one app; the computer type (ADR 22) only sets starting switches.

## A. Navigation (user: tiers)

Sidebar, top to bottom:

| Tier | Tabs |
|---|---|
| Weekly | **Today** (was Session), Profiles, Day plan, **Calendar** (was Exceptions) |
| Sometimes | History, Devices |
| Tucked away | a **More** item at the bottom opening Setup, Integrations, Settings, About |

The timeline (day bar + legend) is drawn **only on Today and History** *(user)*. Calendar shows its own agenda. The app-wide search stays.

## B. Today (idle): the start page

Top to bottom (mockup "Today – idle"):

1. **Goal line:** a ring or bar "0 of 2h 00m deep work" and the plan's status ("Work until 5:00 PM").
2. **Start card** (the one big thing): "What are you doing first?" with up to three task lines (the guided checklist, ADR 24), then **Quick Start with Default** (big, teal) and a quieter **Start 25 min · Coding**. Profile, length and break live behind **More options**, collapsed by default.
3. **Come back to:** flagged captures not yet dealt with (section F).
4. **Timeline:** the day bar and legend, as today.

Steering *(my call)*: at the first Watch part of a workday with no session yet, Today opens the Start card with the task box focused, and the on-screen window shows one line: "Plan 3 tasks and start?". Nothing nags after that; the Start card is the nudge.

## C. Focus mode (user: only the essentials while a session runs)

While a session runs, the window swaps to **Focus mode** (mockup "Focus mode"): timer and profile; the goal bar; the **current task** big, with its checklist in its own quiet panel (tick, pick ▸, add); the **capture box**; **End session**. The sidebar collapses to icons. A small **Show everything** at the bottom brings the normal tabs back for that session (most are locked during focus anyway). Focus mode is the default whenever the window is opened during a session.

The on-screen window keeps its bar and Ctrl+Alt checklist, plus one new button: **Step away**.

### The on-screen window: quiet by default *(user, 2026-10-22: too much colourful noise)*

- **Quiet (resting):** the countdown, the current task and a **slim, muted timeline** (one tone, no category colours), about 60% opacity. The only motion is the now line **gliding** continuously (sub-pixel, every second) instead of jumping each tick, so it never catches the eye *(user, 2026-10-22)*.
- **Loud, briefly:** the last 2 minutes of a phase and time's up (the glow and chime that exist), the goal reached (one green pulse), a nudge after plan-first (an amber edge until answered). Step away keeps its dim "Away · 4:12" badge.
- **Full, on demand:** hover brightens it; Ctrl+Alt shows the details, the checklist, the timeline in full colour and the buttons.
- Settings → On screen: **Quiet** (the default) or **Full colour** (today's look) *(user)*.

### Step away *(user, 2026-10-22: bathroom breaks are a necessary context shift)*

- **Start:** the Ctrl+Alt details' "Step away" button, a global hotkey (Settings), or the watch's session page. Optional: starts by itself after 5 minutes idle.
- **While away:** the on-screen window shows a small "Away · 4:12" badge so it isn't forgotten; prompts and escalation sleep; the watch shows "Away".
- **Back:** any mouse or keyboard input ends it ("Back · away 4 min", a small note), as does the hotkey or the watch. Input ending it means it can't be used to sit at the PC "away".
- **The clock** *(open; my recommendation: pause)*: the countdown pauses and the session's end moves later, so 25 minutes is 25 minutes of work; away time is left out of focused time and drawn hatched on the Focus row. The alternative keeps the end and only leaves away time out of the total.
- Away stretches are session events (DB schema bump); they travel in the sync files as times only.

## D. Calming the prompts (user: bombarded at the start)

- **Plan first** *(user: "7 minutes to devise a plan, end sooner if it's ready")*: the first **7 minutes** of a session are a planning phase: the task box has focus, unknown activity is **logged, not prompted**, and escalation doesn't start. "I'm ready" (or ticking the first task) ends it early. After it, one prompt window lists **every unknown seen so far as rows** (whole site / only parts, Distracting / On track / Exempt per row), so one window settles several. The "no not-now" rule stays: after warm-up an unanswered unknown still escalates (nudge → overlay). Project `CLAUDE.md` gets the warm-up added to its hard rule.
- **Learn from use** *(user: "detect new distractions")*: at a session's end, a short **review card**: "12 m on example.com with no rule" → Distracting / Fine / Ignore, writing rules. The same for the week in History ("New this week").
- **Promote repeats** *(my call)*: a site answered "On track (this session)" in three sessions is offered as a permanent exemption, once.
- Sensible defaults (section E) cut the unknowns in the first place.

## E. Setup with less friction (user: the biggest pain)

First run asks **three things**: what kind of computer (exists), **sign in to Google for sync** (section H; skippable), and **today's deep-work goal** (default 1 h). "QS - Default" exists with its starting rules. The first session runs in **learn mode**: nothing is prompted; the end-of-session review (D) proposes the rules. Imports from other blockers, browsers and the rest of the assistant move under More → Setup.

## F. Capture (user: thoughts that may drift, and ones that must not)

One line box in Focus mode, the on-screen window's Ctrl+Alt details, the quick-session popup and the phone's Today. Typing a line and Enter saves a **note**; a **⚑** toggle (or a leading `!`) makes it a **come-back**.

- **Notes** (drift-OK): shown once in the end-of-session review, kept with the session in History, never resurfaced.
- **Come-backs** (must not drift): listed on Today ("Come back to") on the PC and the phone until ticked; ticking records when.
- Stored like session notes: DPAPI-encrypted on the PC, Keystore-sealed on the phone. Come-backs (not notes) travel in the sealed sync files, like task names (ADR 24). **Needs an ADR** extending ADR 24 to come-backs.

## G. The daily goal and growth (user: see it grow, over days too)

`doc/plans/2026-10-21-deep-work-goal-design.md` becomes central, not a legend detail: the goal ring on Today, the on-screen window's bar, the phone's header and widget, the watch's ring. The default goal is asked at first run (1 h). History adds **Deep work** per day and week against the goal, and a **focus-over-time graph**.

**Phone pickups** *(user, 2026-10-22)*: the phone already knows when it's unlocked (Usage access, which the app has). During a PC session, each unlock counts as a pickup; the count goes into the phone's sync file (times and counts only, no app names) and shows on the end-of-session review ("Phone: 3 pickups") and on History's day and week. No prompt, no camera: a quiet number, so the trend is visible without another interruption. Needs the ADR 22 field list extended (an "Amends" note in ADR 26). The y-axis is a decision for the build, after a short look at the research on knowledge-work focus; candidates: focused minutes per day, longest unbroken stretch, drifts per focused hour. (Not invented here; the plan gets a research step.)

## H. Sync: Google Drive only (user; ADR 26 to write)

- **Both apps sign in to Google** and read/write the sealed sync files (ADR 22, unchanged format) in an app-owned Drive folder, scope `drive.file` (only files the app made). The PC reuses the Calendar sign-in (`GoogleCalendarClient`, PKCE on 127.0.0.1) with the scopes united; the phone gets its own sign-in (new outbound connection: **ADR 26**, superseding ADR 14 and amending ADR 22's list of allowed connections and the project `CLAUDE.md` hard rule). One auditable class per app, like `CalendarNetwork`.
- **Polling:** the PC every 20 s while a session runs, else 5 min; the phone every 30 s while a session runs or its timeline notification is pinned, else 15 min (and on the watch's "/hello"). Drive has no push to phones, so the poll interval is the lag: PC → phone about 30 s, phone → watch about 1 s more, a watch tick or thought → PC about 20 s. Countdowns are never late (absolute times). Seconds-level sync is what a later relay would buy.
- **Dropped:** the Wi-Fi link (server, pairing by QR, discovery, trust-by-network notices), the phone's "start / end a session on the PC" and "edit the day plan from the phone" (ADR 19 deprecated), the watch's direct PC route (ADR 21 amended; the watch is fed only by the phone). Paired devices see one notice: "Linking was replaced by Drive sync: sign in on each device."
- **Kept as fallback:** QR codes of today's files, share and import of files, the mirrored-folder option for a PC without sign-in.
- **Later (new ADR when wanted):** a self-hosted relay or a local method; phone-started sessions through the sync files.

## I. Phone and watch

Phone Today: the goal ring with today's progress, the running session's task, **Come back to**, then the timeline. At the first Watch part with nothing started, one notification: "Plan 3 tasks on the PC?" (no action from the phone: it stays boring on purpose).

**Watch** *(user, 2026-10-22; mockup "Watch – session page and Tile")*:
- **Fed only by the phone** (Data Layer; the Wi-Fi route to the PC goes with the link); the phone pushes a snapshot the moment it learns of a change. The Link page shrinks to a freshness dot (green = under 5 min old).
- **Session page** (first while a session runs): phase label, the big countdown, the current task with its subtask and "1 of 3", two buttons: **tick the current task** and **park a thought** (voice or keyboard), the deep-work goal as the outer ring. Nothing else. Ticks and thoughts go watch → phone → sync file → PC (about 20 s). No ending a session from the wrist: ending has friction on purpose.
- **Buzz** at a phase end (exists) and **when the daily goal is reached** (new).
- **Complication** = the face glance and the app launcher (the running countdown, else today's focus of the goal). **Tile** = stats with a live countdown: one timeline entry, the countdown and the now tick bound to the watch's clock (ProtoLayout dynamic time, as the complication already does), and an explicit Tile refresh whenever a snapshot arrives. This replaces the 12 ten-minute entries, the likely cause of the black Tile.
- ADR 21 is amended for the phone-only route and the two controls (they change nothing on the PC except a task tick and a note; task names already travel, ADR 24).

## J. Deep-work habits the app should carry *(user, 2026-10-22: all four)*

- **Shutdown routine.** At the end of the last Watch part (or on quitting after it), a short card: open come-backs, unfinished tasks (carry or drop), and **tomorrow's first task**. Tomorrow's Today opens with that task already in the box. Skippable in one click; never nags twice.
- **Hyperfocus guard.** After 90 minutes of unbroken focus, one quiet nudge on the on-screen window: water, stand, eat. Per-profile opt-out; the interval in Settings → Focus sessions.
- **Comms windows.** A **Comms** part in the day plan (a Watch-category part flagged "comms"): inside it, mail and chat apps and sites (Outlook, Teams, Slack, Gmail…) are allowed; outside it they count as distracting, in every profile. Two 15-minute windows by default on workdays (11:30, 16:00); the day plan editor sets them like any part.
- **Interrupted.** A second flavour of Step away for a colleague at the desk or a call: same badge, same pause, same auto-end on input, labelled so History tells breaks from interruptions.
- Not now: "where I left off" at session end, Windows Do Not Disturb.

## Order of work (my proposal; reorder freely)

1. **Navigation, Today, Focus mode** (A, B, C): the daily win, no backend change.
2. **Prompt calming and learn mode** (D, E): the first-minutes pain.
3. **Drive-only sync** (H, ADR 26): reliability at work; removes the link.
4. **Goal and growth** (G): schema v6, History graph.
5. **Capture** (F, ADR).

## Reviewer's questions, settled (2026-10-22; my calls unless marked user)

1. **Both apps call the Drive API** with the `drive.file` scope (only files the app made; the desktop and Android sign-ins belong to one Google Cloud project, so they see each other's files). The mirrored-folder (Drive for desktop) way is dropped, so the Work preset gains one outbound connection: the Drive API (*user confirmed*). The project leaves "Testing" so sign-ins stop lapsing every 7 days.
2. **Sync files gain** (optional fields, format version stays 1): calendar events as the link sent them (titles, times, colours; ADR 22 amended), and the running session's planned end and break schedule (for the phone's countdown and the watch's buzzes).
3. **Cross-device writes** (watch or phone: tick a task, park a thought, tick a come-back, Step away start/stop) travel as an **action list** in the phone's file with ids; the PC applies them and acknowledges by id in its `Seen`. A Step away arriving late is applied from its start time and ends at the first PC input after it.
4. **Auto Step-away after idle: off by default** (the paper notebook means idle minutes while working; *user confirmed*). Step away pauses the countdown, the end-early wait and ADR 25's 10-minute guard alike.
5. **Plan-first holds only the classify prompt for unknowns.** Rules still enforce; drift in those minutes counts in History. Project `CLAUDE.md` hard rule to say exactly this.
6. **The review card writes AccountabiliBuddy rules** into the session's profile (plus the pre-ticked ones for allows); the Cold Turkey hand-off stays its own later ADR.
7. **Phone day-plan editing stays**, carried by the sync files (ADR 22's clash dialog); ADR 19's link transport is what's dropped.
8. **Live sync on the phone requires its pinned timeline notification** (Android's condition for a 30-second poll); otherwise every 15 minutes. Stated in Devices.

## Left to the builder

Exact spacing and sizes (the mockups set the direction, not the pixels); the ring vs. bar choice per surface; how the sidebar's icon rail looks collapsed; the prompt window's row layout; the migration notice wording; which Drive folder name.

## Settled with the user (2026-10-22)

- Step away **pauses** the countdown. The end-of-session review is an **in-app card**. The default daily goal at first run is **1 h**. The resting overlay keeps a slim timeline with a gliding now line.

- Auto Step-away after idle is **off by default**; the work PC **calls the Drive API itself** (one more outbound connection there, like the calendar). *(user, 2026-10-22)*

## Status

**Approved by the user on 2026-10-22** after a builder check (the "Reviewer's questions" section). Plans follow per phase; nothing is built from this document alone.
