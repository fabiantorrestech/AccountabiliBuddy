# The user's feature & bugfix list: decisions and order

Date: 2026-10-26 · Status: structure agreed; each phase gets its own detailed plan before it's built

Source: the user's note `fablesZettelkasten/2 - Source Material/Software Dev/Projects/AccountabiliBuddy_App/AccountabiliBuddy - Future Feature & Bugfix List.md`.
Item numbers below (e.g. **PC 3.1**, **C 8.4**, **W 1**, **A 1**) are that note's numbering (PC App, Watch App, Android
App, Combined). When an item is done, tick its checkbox in that note and update the AI_Agents_Hub project log.

Order: bugs → functional features → the Claude Design batch last. Implementation runs with subagents (orchestrator
Opus 5.5 high, implementers Opus 5.5 medium), one item or one group of items that touch different files per agent.

## Decisions (user, 2026-10-26)

| Item | Decision |
|---|---|
| PC 3.1 session slider | Plain drag = 1-minute steps, Shift = 5. Shift is wired only in the quick-session popup today, never on Today. |
| PC 4 Enter in the prompt | Single prompt: Enter = On track (this session); stacked prompt: Done; with "Add exemption" open: Add exemption. |
| PC 5.1 + C 5 spare key | C 5 is the same bug (the Migration Assistant's spare key page shows no key) **plus**: no way to get a new key except Emergency release. New: "Get a new spare key" with Pre-Work emergency friction: a reason of 40+ characters, retype a 12-character code (no pasting), a 10-minute wait with Cancel, at most 2 in 7 days, allowed in work hours and Pre-Work, not during a focus session (end it first), logged. No viewing the current key. Amends ADR 39. |
| PC 7 Alt+Tab | The browser was behind other windows (not minimized, not visible). Debug first: likely the watcher judges the next window in z-order while the Alt+Tab switcher is in front. |
| PC 9 session end | Don't bring the window forward; flash the taskbar icon. |
| PC 6.1.1 / 6.1.2 overlay | Fix the cut-off task now; draw our own cursor-following tooltips above the overlay. The full overlay redesign goes to the Claude Design batch. The cut-off line "The bottom when we're…" is dropped. |
| PC 11.1 wake estimate | With "wake by alarm" and no alarm that morning: median of past alarm times on that kind of day (Workdays / Off-Days), else Health Connect's measured wake times, bedtime + goal only as the last fallback. The phone computes it and sends the numbers (new sync field: amend ADR 23/35 and the hard rules). |
| PC 1 work nudge | Timing starts on arrival (the first input after 10+ min away, e.g. 9:30), not at the work part's start. Arrival: Today with the task box. After 5 min with no session: a card in front of everything like the work-hours gate (Start, or "Not now" with a reason), again every 15 min. |
| PC 2.1 Notebook | Edit and delete come-backs and notes (tasks already can). |
| PC 8.1 Settings | A new side group "Overlay" holding On-screen timer and On-screen timeline; "On screen & sound" keeps Theme, Animations, Sounds, Noise player. |
| PC 8.2 timeline opacity | The timeline gets its own "Opacity" and "Opacity while Ctrl+Alt is held" for when no session runs. |
| PC 10 browser setup | Less text; keep status lights and "installed"; copy-path buttons instead of opening Explorer (Chromium: the folder; Firefox: the full path to `manifest.json`); browsers grouped under "Browser extensions". |
| A 1 test session | Today → More options, 1 minute by default, adjustable. Runs like a real session on the phone, but is never counted, never in History and never synced. |
| C 9 now line | The phone's halo now line (a377dcd) on the PC's day bar and on-screen timeline. |
| C 2 noise lock | Per device (PC, phone): a lock icon; locked with no headphones, Play does nothing and says "Connect headphones"; media keys, shortcuts and the watch respect it. Plus a setting on each device: stop the player when headphones disconnect (apart from headphone auto-play). |
| C 8 task list | Categories = named groups with colours (each task in one; the list grouped under them). Parked = come-backs: move a task to come-backs and a come-back into the checklist; edit come-backs in place. Drag to reorder, keeping ↑ ↓. PC and phone. |
| C 10 flexible lunch | Already built: Start lunch, 1 h default, hint, "Lunch over!", sync. Add: Start lunch on any workday (a plan lunch part is only a hint), +/- while lunch runs (synced), a watch page (start, I'm back, countdown), a workday lunch length setting. |
| C 1 Wi-Fi link | Move the four things that still need it to sync files first (phone start / end early, phone day plan edits, the watch's PC route, the phone's live view of the PC), then delete the link, its pairing and its port. Breaking change; new ADR superseding parts of ADR 14, 19 and 21. |
| W 1 watch noise devices | Switch Work PC → Phone → Personal PC on the Noise page, PCs reached through sync files (accept ~10 s delay). After C 1. |
| C 6 timers & alarms | V1 local per device: PC glow + chime + a bar per timer in the overlay; phone one notification per timer with a live bar and an alarm sound; watch buzz + its own page. Named, editable. Synced timers (C 6.1) after the sync server. |
| W 2 complication | Two complications: the existing session one, and a new "Timers" one (soonest ending). |
| C 3 sync server | Both: a guide for self-hosting with existing tools (Syncthing, Nextcloud) on Windows, macOS and Linux, shown in both apps; and a sealed relay program (stores the sealed sync files, pushes changes in about a second, can't read them). New ADR (a new connection). |
| C 7 other blockers | Detect installed (Freedom, PluckEye; AppBlock, StayFocused, Freedom on Android): status only in Integrations and the setup assistant. |
| C 11 Beeminder | Read goals only (Today on PC, phone, watch); token encrypted at rest; new ADR (a new connection). "Send focus minutes" goes to the Future list. |
| C 4 UI | Mobile, desktop and tablet (One UI-style two panels) redesigns: Claude Design batch, at the end. |
| Day plan rework | Per-weekday plans and alternating weeks: added; waits on the user's description of their week. |
| Future list | Untouched until the rest is done. |

### Added items (user, 2026-10-26 evening)

| Item | Decision |
|---|---|
| PC 12 + PC 13 overlay | Fix now (the redesign keeps the behaviour). Left-align Goal, Profile, session time, task list and noise controls. Anchoring: a 3×3 grid of screen zones picked from the overlay's centre on drop; corners anchor at the corner, edge-middles at the edge's middle, the centre zone grows evenly; always clamped on-screen. Debug the snap-back first (likely collapsed and expanded sizes saving different positions). |
| PC 14, PC 17, PC 19, PC 20 | Bugs, fix as written (timer colour delay on Ctrl+Alt; 5th category missing in the new-task picker; sidebar "More" toggle; sidebar scrolls when the window is short). |
| PC 15 step away + side task | Step away never resumes by itself: focus time stays paused until "Back on track". On the first input after a step away, a "Back to <task>?" card shows; unanswered, it repeats every 2 min. New "Side task" at the desk: asks for a length (5/10/15, default 5); at the end a chime + card "Back to <task>?" with Back on track / +5 min / End session. Each +5 needs a short reason; from the 2nd extension on, a short wait (like end early). The overlay shows side time growing in amber next to focus time; History logs it as "Side task", never focus. Enforcement keeps running throughout (no escape hatch). Record in doc/hard-rules.md. |
| PC 16 | A distinct pinned-task marker (Material icon) instead of ">" on the PC app and overlay. |
| PC 18.1 Wrap up the day | Come-backs and Left open stand out more; tasks show under their category headers in both sections; one rule in both: checked = done, unchecked = carried over. |
| PC 21 | In a session the timeline slides up out of the way; hovering at the top shows it; Today's session UI takes the freed space. |
| A 2 Essentials | Hard to dismiss (Home / Back); use LockMyPhone-Extended when it's set up instead of the in-app screen; date under the time, time with AM/PM. Investigate first. |
| A 3 | The start-session prompt must not be dismissible by Home / Back (any warning, not only the last). Investigate first. |
| C 12 + C 13 planning | Planning has its own countdown bar. When it ends: a sound + "Ready?" with Start / +2 / +5 / +15. Then a fresh countdown bar for the full session length. The session timer counts down by default. Planning isn't focus time. PC, phone, watch. Record in doc/hard-rules.md. |
| C 14 | Number the tasks (1. 2. 3.) in Today on PC, phone, watch and in the Ctrl+Alt overlay. |
| C 15 work window | Folded into the day plan rework (phase 8). |
| PC test session (new, like A 1) | Today → More options, 1 minute by default, adjustable. Runs like a real session on the PC (enforcement, prompts, overlay), but is never counted, never in History and never synced. First use: confirm the Alt+Tab fix (PC 7), then remove its DIAG logging. |
| Places (new, not in the note yet) | Location-based focus spots per device: Focus / Sometimes / Free / Not now; network first (VPN-proof), location as backup; spots only add rules. Full decisions: `doc/plans/2026-10-26-phase12-places-plan.md` (ADR 44 first). |
| Future: auto-start at a spot | An option, off by default; needs an ADR 18 amendment. After Places. |

### User answers (2026-10-27): phases 10, 11, 4b and 6 approved
| Item | Decision |
|---|---|
| Phase 10 | Details in `doc/plans/2026-10-27-phase10-added-bugs-plan.md` ("User decisions"). |
| 4b | Delete the link, its pairing, its port and the watch's PC route; the watch works through the phone only. |
| C 3.1 relay | A small standalone .NET program (built-in ASP.NET Core, no NuGet): Windows service, Linux systemd or Docker. Stores the sealed files, pushes changes in about a second, can't read them. The phone uses plain HTTP streaming (no new Android libraries). |
| Relay reach | All three, each in the guide: LAN + the user's VPN/Tailscale (pinned self-signed cert), the public internet with a real certificate (Let's Encrypt / reverse proxy), and a VPS. |
| C 6.1 synced timers | A synced timer or alarm shows and rings on every device; dismissing or snoozing on one stops it on all. Each can be marked "this device only". |
| Phone placement (new, 2026-10-27) | During a work session the phone should be in a bag (best) or face down and still on the desk (best); a pocket is a fallback. Phone sensors only (proximity, light, accelerometer), sampled a few seconds a minute in a session; "a few feet away" can't be measured, so face-down-or-bag-and-still counts as out of sight. **No phone buzzing** (more distracting): nags show on the PC. Pocket: a gentle PC notice. Face up, in hand or unlocked without a work reason: escalate (PC overlay notice at once, after 1 min a stronger one, after 3 min the phone's Essentials screen opens, silently); each is counted in the session review. "Need it for work" (Call / 2FA / Work app / Other + short note, 5/10/15 min), from the 4th use in a session a longer reason and a short wait (like end early); afterwards "Done? Put it back". Only a placement label goes to the PC, never sensor data. Own phase right after phase 11. |

## Phases

1. **Bugs.** PC 3.1, PC 4, PC 5.1 (+ C 5 bug part), PC 7, PC 9, PC 6.1.1, PC 6.1.2, PC 11.1 (needs the ADR amendment).
2. **PC features.** C 5 new spare key (ADR 39 amendment), PC 1, PC 2.1, PC 8.1, PC 8.2, PC 10, C 9.
3. **Phone, watch and shared features.** A 1, C 2, C 8, C 10.
4. **Retire the Wi-Fi link.** C 1 (ADR first), then W 1.
5. **Timers & alarms V1.** C 6.2, C 6.3, W 2.
6. **Sync server.** C 3.2 guide, then C 3.1 relay (ADR, likely new dependencies: ask first), then C 6.1 synced timers.
7. **Integrations.** C 7, C 11 (ADR).
8. **Day plan rework** once the week is described (now includes C 15).
9. **Claude Design.** PC 6.1 overlay redesign, C 4.1-4.3.
10. **Added bugs.** PC test session first (needed to test the rest), then PC 12, PC 13, PC 14, PC 17, PC 19, PC 20, A 2, A 3. After phase 4a and the goal-sync bug.
11. **Added features.** PC 16, PC 18.1, PC 21, C 14, then PC 15 (step away + side task), then C 12 + C 13 (planning bar). Before phase 9.
12. **Places.** ADR 44, Core, PC network sensor + cards + Settings → Places, PC location fallback, then the phone. After phase 11, before phase 9.

Each phase: a detailed plan (files, steps, acceptance checks) agreed with the user, then implementation.
