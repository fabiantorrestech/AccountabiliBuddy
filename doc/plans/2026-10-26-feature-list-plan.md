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

## Phases

1. **Bugs.** PC 3.1, PC 4, PC 5.1 (+ C 5 bug part), PC 7, PC 9, PC 6.1.1, PC 6.1.2, PC 11.1 (needs the ADR amendment).
2. **PC features.** C 5 new spare key (ADR 39 amendment), PC 1, PC 2.1, PC 8.1, PC 8.2, PC 10, C 9.
3. **Phone, watch and shared features.** A 1, C 2, C 8, C 10.
4. **Retire the Wi-Fi link.** C 1 (ADR first), then W 1.
5. **Timers & alarms V1.** C 6.2, C 6.3, W 2.
6. **Sync server.** C 3.2 guide, then C 3.1 relay (ADR, likely new dependencies: ask first), then C 6.1 synced timers.
7. **Integrations.** C 7, C 11 (ADR).
8. **Day plan rework** once the week is described.
9. **Claude Design.** PC 6.1 overlay redesign, C 4.1-4.3.

Each phase: a detailed plan (files, steps, acceptance checks) agreed with the user, then implementation.
