# Redesign phase 2: a calm start, the other computer (local), first run, habits

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** the first minutes of a session stop bombarding; unknowns are settled in one window or at the end; the personal PC holds dev tools during work hours with a gate that re-arms; first run is three questions; two habits (hyperfocus guard, comms windows) join.

**Architecture:** Core gains a hold on unknown escalation (`EnforcementEngine.HoldUnknownsUntil`), the review's unknown-time query and the repeat-allow finder. The App gets `PlanFirstService`, a stacked classify prompt, a review card on Today, `WorkHoursGuard` + gate window, a capture popup with local come-backs, a slimmer setup assistant, `HyperfocusGuard`, and comms windows in the day plan (profile schema v6, phone reader).

**Tech Stack:** .NET 10, Avalonia 12.1.4, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, CsWin32; Kotlin (phone reader for schema v6 only). Scratch checks in `%TEMP%\ab-sync-checks` (synccore, syncapp, ktcheck), never repo tests.

**Spec:** `doc/plans/2026-10-22-redesign-design.md`, sections D, E, J (hyperfocus guard, comms windows), K (local part: Work-hours profile, the gate, capture at the source). Reviewer's questions 5 and 6.

## Global Constraints

- Core has no UI or OS dependencies (ADR 4).
- "Plan-first holds only the classify prompt for unknowns. Rules still enforce; drift in those minutes counts in History." (settled question 5) Project `CLAUDE.md` hard rule says exactly this.
- No escape hatches: the classify prompt has no "not now"; "On track" lasts for the session and needs a reason; "add exemption" is permanent and needs a reason. The review card and the stacked prompt keep both rules.
- "The review card writes AccountabiliBuddy rules into the session's profile (plus the pre-ticked ones for allows)" (settled question 6).
- Stricter changes apply at once; an allow from a prompt or the review skips the looser-change wait only because its typed reason is the friction (as `AddRuleNowAsync` today).
- K: "Dev tools (Claude Code, terminals, IDEs; the user's list) are blocked (minimized at once), not nudged … The only ways out are the existing ones: end early (wait + reason) or the goal unlock." The gate "re-arms after 3 minutes with no input" and has "a 60-second wait before anything else".
- App-owned state lives in the `documents` table; texts typed by the user (come-backs, reasons) are DPAPI-encrypted.
- Commit per task with the session's trailers; never commit `handoffContext/`.

## Review Focus

1. A session started and ended inside the 7-minute warm-up: expected: the stacked prompt never opens; the unknowns go to the review card.
2. An unknown seen in warm-up that's no longer on screen when warm-up ends: expected: it's still a row in the stacked prompt (answered or left for the review), not dropped.
3. Work hours on the personal PC while a session the user started is running there: expected: the session's own profile rules apply, the Work-hours guard steps aside (no double minimizing, no gate).
4. The gate open when work hours end (17:00): expected: it closes by itself; no come-back is lost (typed text is saved on close).
5. A computer set up before phase 2 (setup already done): expected: no learn mode, no setup re-run; the three-question flow only on fresh installs.

---

## Slices (ship each on its own)

| Slice | Tasks | Ships as |
|---|---|---|
| 1. Calm start | 1–4 | 0.1.66 |
| 2. The other computer (local) | 5–7 | 0.1.67 |
| 3. First run | 8 | 0.1.68 |
| 4. Habits | 9–10 | 0.1.69 (+ phone APK) |

Each slice ends with: `run-pc.sh` ALL PASS, `dotnet build`, project `CLAUDE.md` updated, version bump, package, session check, install, publish, push.

---

### Task 1: Plan first: unknowns held for 7 minutes

**Files:**
- Modify: `src/AccountabiliBuddy.Core/Engine/EnforcementEngine.cs`, `src/AccountabiliBuddy.Core/Engine/DriftTracker.cs`
- Create: `src/AccountabiliBuddy.App/Services/PlanFirstService.cs`
- Modify: `src/AccountabiliBuddy.App/Services/EnforcementCoordinator.cs` (set the hold; collect warm-up unknowns), `ViewModels/FocusViewModel.cs` + `Views/FocusView.axaml` ("Planning · 5:12 left · I'm ready"), `ViewModels/OnScreenOverlayViewModel.cs` + `Views/SessionOverlayWindow.axaml` (same line and button with Ctrl+Alt), `App.axaml.cs` (wire)
- Check: `synccore/Checks/PlanFirstChecks.cs`, `syncapp/Checks/PlanFirstAppChecks.cs`

**Interfaces:**
- Produces: `EnforcementEngine.HoldUnknownsUntil` (`DateTimeOffset?`): while `now < HoldUnknownsUntil`, Unknown results get `EscalationLevel.None` and `NeedsClassification = false`; after it, an unknown's grace counts from `max(since, HoldUnknownsUntil)`. Distracting results are untouched.
- Produces: `PlanFirstService { static TimeSpan Length = 7 min; bool IsPlanning; TimeSpan Left; DateTimeOffset? EndsAt; void Ready(); IReadOnlyList<ActivitySnapshot> HeldUnknowns; event EventHandler? Ended; }`. Starts on a session's start (not on resume, not on a count-up session started from the phone? → yes for every session the user starts); ends at `PhaseStartedAt + 7 min` (away time excluded via the moved start), on `Ready()`, or when the checklist's first task is ticked (`SessionTasksService.Changed` with a done task).

- [ ] **Step 1: Checks.** Core: a profile with no rule for `game`; snapshot `game` from T; `HoldUnknownsUntil = T+7min`; at T+6 → `None`, `NeedsClassification == false`; at T+7:30 (grace 1 min) → still `None`; at T+8:01 → `NeedsClassification == true`. A Block rule hit at T+1 still escalates (Overlay at T+1:45 with default settings). App: start a session → `IsPlanning`, `Left == 7:00`; `Ready()` → `IsPlanning == false`, `Ended` raised once; ticking the first task ends it; a session ended at T+3 raises `Ended` with `HeldUnknowns` intact (Review Focus 1, consumed by Task 3).
- [ ] **Step 2: Run** → RED (missing members).
- [ ] **Step 3: Implement.** The coordinator sets `_engine.HoldUnknownsUntil = planFirst.EndsAt` before each `Evaluate`, and adds each Unknown snapshot it sees while planning to `HeldUnknowns` (one per subject: process + domain + section). Focus view and overlay show "Planning · m:ss left" with an "I'm ready" button.
- [ ] **Step 4: Run** → GREEN; build.
- [ ] **Step 5: Commit** `Plan first: unknowns aren't asked about in a session's first 7 minutes`.

### Task 2: The stacked prompt

**Files:**
- Create: `ViewModels/ClassifyStackViewModel.cs`, `ViewModels/ClassifyStackRowViewModel.cs`, `Views/ClassifyStackPrompt.axaml(.cs)`
- Modify: `Services/EnforcementPresenter.cs` (`ShowStack(...)`; while it's open the single prompt doesn't open), `App.axaml.cs` (on `PlanFirstService.Ended` with a session still running and `HeldUnknowns.Count > 0`)
- Check: `syncapp/Checks/ClassifyStackChecks.cs`

**Interfaces:**
- Consumes: `PlanFirstService.HeldUnknowns`, `ClassifyPromptViewModel`'s scope logic (reuse its `SiteParts.PartOf/PageOf` rules: extract the scope/pattern building into `ClassifyScopeOptions` used by both).
- Produces: `ClassifyStackViewModel(string profileName, IReadOnlyList<ActivitySnapshot> unknowns, IReadOnlyList<ProfileOption> others, IReadOnlyList<Guid>? preTicked)` with `Rows` (each: `Subject`, `Scope` whole/parts, `Verdict` {Unanswered, Distracting, OnTrack, Exempt}), one `Reason` for every OnTrack/Exempt row, `Submit()` → `IReadOnlyList<ClassifyAnswer>` (same type the single prompt raises). Unanswered rows stay unknown (they escalate as usual afterwards; Review Focus 2).

- [ ] **Step 1: Check:** three unknowns (reddit.com/r/x, youtube.com, `game`) → three rows; reddit row offers "only r/x"; set Distracting / OnTrack / Exempt with an empty reason → `Submit()` refuses ("A reason for the On track and Exempt rows"); with a reason → three answers: Block rule for r/x (Lifetime Permanent), Allow for youtube.com (Lifetime Session), Allow for `game` (Permanent, `OtherProfileIds` = pre-ticked). A row left Unanswered → no answer for it.
- [ ] **Step 2: Run** → RED.
- [ ] **Step 3: Implement.** The window: title "Settle what you opened while planning", one row per unknown, radio-style chips, one reason box, Submit. Answers go through `EnforcementPresenter.Answered` one by one (the coordinator already writes them).
- [ ] **Step 4: Run** → GREEN; dev build with seeded unknowns: screenshot.
- [ ] **Step 5: Commit** `Stacked prompt: what was opened while planning, settled in one window`.

### Task 3: The end-of-session review card

**Files:**
- Modify: `src/AccountabiliBuddy.Core/Storage/SqliteActivityLog.cs` (`GetUnknownUsageAsync`)
- Create: `src/AccountabiliBuddy.App/Services/SessionReviewService.cs`, `ViewModels/SessionReviewViewModel.cs`, `ViewModels/ReviewRowViewModel.cs`
- Modify: `Views/TodayView.axaml` (card above the start card after a session ends), `Views/HistoryView.axaml` + `ViewModels/HistoryViewModel.cs` ("New this week": the same rows over the last 7 days, same three choices), `App.axaml.cs`
- Check: `syncapp/Checks/SessionReviewChecks.cs`

**Interfaces:**
- Produces: `SqliteActivityLog.GetUnknownUsageAsync(DateTimeOffset from, DateTimeOffset to) → IReadOnlyList<UsageEntry>` (classification Unknown, `in_focus = 1`, grouped like Top distractions: site with section, else app; descending time).
- Produces: `SessionReviewService { SessionReviewViewModel? Pending; event Changed; }`: on `SessionEnded`/`SessionCompleted`, rows = entries ≥ 1 minute, at most 8, minus keys in document `"review.ignored"`; none → no card. Choices per row: **Distracting** (Block rule, at once), **Fine** (Allow rule into the session's profile + pre-ticked profiles, needs the card's one reason), **Ignore** (key added to `"review.ignored"`). "Done" applies and closes; "Later" keeps the card until the next session starts.

- [ ] **Step 1: Check:** segments in a session: example.com Unknown 12 min, docs.rs Unknown 40 s, game OnTrack 5 min → one row "example.com · 12m"; Fine without reason refused; Fine with reason → Allow rule `Domain example.com` added to the profile; Ignore → next session's review leaves example.com out; Distracting → Block rule present.
- [ ] **Step 2: Run** → RED.
- [ ] **Step 3: Implement** the query (reuse `TopUsageAsync` with the Unknown filter), the service and the card ("12m on example.com with no rule" · Distracting / Fine / Ignore · one reason box · Done / Later).
- [ ] **Step 4: Run** → GREEN; dev build: end a seeded session, screenshot the card.
- [ ] **Step 5: Commit** `Review card: unknown time from the session becomes rules`.

### Task 4: Promote repeats

**Files:**
- Modify: `src/AccountabiliBuddy.Core/Storage/SqliteActivityLog.cs` (`GetLabelsAsync(from, to)` → `IReadOnlyList<ActivityLabel>`, readable columns only)
- Create: `src/AccountabiliBuddy.Core/Engine/RepeatAllows.cs`
- Modify: `Services/SessionReviewService.cs` (offer rows), `ViewModels/SessionReviewViewModel.cs`
- Check: `synccore/Checks/RepeatAllowChecks.cs`

**Interfaces:**
- Produces: `RepeatAllows.Find(IReadOnlyList<ActivityLabel> labels, IReadOnlyList<SessionRecord> sessions, ISet<string> alreadyOffered, int threshold = 3) → IReadOnlyList<RepeatAllow>` where `RepeatAllow(string Key, string Display, Rule SuggestedRule)`; counts distinct sessions (label `CreatedAt` inside a record's window) with a Session-lifetime Allow for the same key (process, or domain + section).
- The review card gets a second list "You've called these on track in 3 sessions: always allow?" (Yes → permanent Allow with the card's reason; No → never offered again). Offered keys in document `"review.promoted"` (offered once, either answer).

- [ ] **Step 1: Check:** Session-lifetime Allow labels for github.com in sessions 1, 2, 3 → one RepeatAllow; in 1 and 2 → none; two labels in the same session count once; a key in `alreadyOffered` → none.
- [ ] **Step 2–4:** RED → implement → GREEN.
- [ ] **Step 5: Commit** `Promote repeats: on track in three sessions is offered as an exemption, once`.
- [ ] **Slice 1 ship:** project `CLAUDE.md` (hard rule: plan-first, the stacked prompt, the review card, promote repeats; documents list `review.ignored`, `review.promoted`), 0.1.66.

### Task 5: ADR 27 and the Work-hours guard

**Files:**
- Create: `doc/adr/0027-work-hours-guard-on-a-personal-pc.md` (adr skill; regenerate the index), `src/AccountabiliBuddy.App/Services/WorkHoursSettings.cs` (+ service), `src/AccountabiliBuddy.App/Services/WorkHoursGuard.cs`
- Modify: `App.axaml.cs`, `Views/SettingsView.axaml` + `SettingsViewModel` (Your day: "Work hours on this computer"), `Services/EnforcementCoordinator.cs` (steps aside), project `CLAUDE.md`
- Check: `syncapp/Checks/WorkHoursChecks.cs`

**Interfaces:**
- Produces: `WorkHoursSettings(bool On, IReadOnlyList<string> Apps, bool LockOnIdle = false)`; document `"workHours"`; default Apps: `claude, windowsterminal, wt, pwsh, powershell, cmd, code, devenv, rider64, idea64, pycharm64, clion64, webstorm64` (process names, lowercase); off by default; offered on the Personal computer type.
- Produces: `WorkHoursGuard { bool IsGuarding; string? UntilText; event Changed; Task<bool> StopForTodayAsync(string reason, DateTimeOffset requestedAt); }`: guarding while On, inside a Watch part of today's plan, no session the user started is running (Review Focus 3), and not stopped for today. On each activity change, a window of a listed process is minimized at once (`IWindowEnforcer.Minimize`) with one notice per minute: "Work until 5:00 PM: <app> waits. Park the idea with Ctrl+Alt+N." Stopping for today uses `BypassPolicy` with the profile-wide `BypassSettings` (wait + reason); turning the setting off while guarding goes through the same wait + reason.
- ADR 27 records: enforcement outside a user-started session, only on a computer where the user turned it on, only for the listed apps, only in the plan's Watch parts; supersedes nothing; amends ADR 18's "no scheduled sessions" for this opt-in guard.

- [ ] **Step 1: Check** (fake enforcer, fixed clock, plan with Work 9–17): On, 10:00, `code` in front → minimized once, notice once; 18:00 → not minimized; session running → not minimized by the guard; `StopForTodayAsync("", t)` refused; after the wait with a reason → not guarding until midnight.
- [ ] **Step 2–4:** RED → implement → GREEN.
- [ ] **Step 5: Commit** `Work hours on a personal PC: dev tools wait until work ends (ADR 27)`.

### Task 6: The gate

**Files:**
- Create: `ViewModels/WorkGateViewModel.cs`, `Views/WorkGateWindow.axaml(.cs)`, `Services/WorkGateService.cs`
- Modify: `App.axaml.cs`
- Check: `syncapp/Checks/WorkGateChecks.cs`

**Interfaces:**
- Consumes: `WorkHoursGuard.IsGuarding`, `IUserIdleDetector`, `ComebackStore` (Task 7).
- Produces: `WorkGateService` armed while guarding; it opens the gate on the first input after ≥ 3 minutes with no input (and at the start of a Watch part); the gate shows "Work until 5:00 PM · what were you about to do?", a one-line box (Enter saves a come-back), and "Continue" enabled after 60 s; closing it some other way reopens it at the next check; it closes by itself when guarding stops, saving typed text (Review Focus 4). `static TimeSpan RearmAfter = 3 min; static TimeSpan Wait = 60 s`.

- [ ] **Step 1: Check:** idle 4 min then input → gate open, Continue disabled until +60 s; idle 2 min then input → no gate; text typed then guarding stops → one come-back saved, gate closed.
- [ ] **Step 2–4:** RED → implement → GREEN; dev build screenshot.
- [ ] **Step 5: Commit** `The gate: work hours ask what you were about to do, again after 3 idle minutes`.

### Task 7: Capture at the source (local come-backs)

**Files:**
- Create: `Services/ComebackStore.cs` (+ `Comeback` record), `Views/CapturePopup.axaml(.cs)`, `ViewModels/CaptureViewModel.cs`
- Modify: `App.axaml.cs` (global shortcut Ctrl+Alt+N, Settings → Focus sessions next to Step away), `Views/TodayView.axaml` + `ViewModels/SessionViewModel.cs` ("Come back to" list with tick, idle and in Focus mode)
- Check: `syncapp/Checks/ComebackChecks.cs`

**Interfaces:**
- Produces: `Comeback(Guid Id, string Text, DateTimeOffset CreatedAt, DateTimeOffset? DoneAt)`; `ComebackStore { IReadOnlyList<Comeback> Open; Task AddAsync(string text); Task TickAsync(Guid id); event Changed; }`; document `"capture.comebacks"`, text DPAPI-encrypted (like `session.tasks`); this computer only until phase 5's ADR adds them to the sync files.
- Ctrl+Alt+N anywhere opens a small popup at the cursor's monitor: one line, Enter saves and closes, Esc closes; works during focus (it changes nothing).

- [ ] **Step 1: Check:** add "call the dentist" → stored encrypted (raw JSON lacks the text), listed open; tick → not open, `DoneAt` set; reload keeps both.
- [ ] **Step 2–4:** RED → implement → GREEN.
- [ ] **Step 5: Commit** `Capture: Ctrl+Alt+N parks an idea as a come-back; listed on Today`.
- [ ] **Slice 2 ship:** project `CLAUDE.md` (hard rules: ADR 27 guard, gate; documents `workHours`, `capture.comebacks`), 0.1.67.

### Task 8: First run in three questions and learn mode

**Files:**
- Modify: `ViewModels/SetupAssistantViewModel.cs` + `Views/SetupAssistantWindow.axaml` (fresh installs: ComputerType → Google sign-in placeholder "With phase 3; skip for now" → Goal (30 min / 1 h / 2 h / 4 h, default 1 h) → Done; the old steps run from More → Setup "Full setup (imports, browsers)"), `Services/SetupAssistantService.cs` (`learn.firstSession` document), `App.axaml.cs`, `Services/PlanFirstService.cs` (learn mode = the hold lasts the whole first session; the stacked prompt doesn't open; the review card does)
- Create: `Services/GoalSettings.cs` (document `"goal"`: `DailyMinutes`, default 60; phase 4 reads it)
- Check: `syncapp/Checks/FirstRunChecks.cs`

- [ ] **Step 1: Check:** a fresh data folder → assistant steps are exactly ComputerType, Sync, Goal, Done; "1 h" saves 60; the first session holds unknowns to its end and raises the review; a data folder with `setup` already shown → no learn mode (Review Focus 5).
- [ ] **Step 2–4:** RED → implement → GREEN; dev build on an empty data folder: screenshots of the three steps.
- [ ] **Step 5: Commit** `First run: three questions; the first session learns instead of asking`.
- [ ] **Slice 3 ship:** `CLAUDE.md`, 0.1.68.

### Task 9: Hyperfocus guard

**Files:**
- Create: `Services/HyperfocusGuard.cs`, `Services/HyperfocusSettings.cs` (document `"hyperfocus"`: `IntervalMinutes` 90, `OffForProfiles` ids)
- Modify: `App.axaml.cs`, Settings (Focus sessions: interval; Profiles: "Hyperfocus reminder" switch per profile, stored in the document, not the profile JSON)
- Check: `syncapp/Checks/HyperfocusChecks.cs`

**Interfaces:**
- Produces: one reminder (`EnforcementPresenter.ShowReminder("90 minutes in", "Water, stand, eat. Step away pauses the timer.", teal)`) after `IntervalMinutes` of unbroken focus (focus phase time since the later of the phase start and the last return from away); once per stretch; not during a break.

- [ ] **Step 1: Check:** 89 min → none; 90 → one; 120 → still one; step away + back at 100, then 190 → second; profile in `OffForProfiles` → none.
- [ ] **Step 2–4:** RED → implement → GREEN.
- [ ] **Step 5: Commit** `Hyperfocus guard: one quiet reminder after 90 minutes`.

### Task 10: Comms windows (profile schema v6)

**Files:**
- Modify: `src/AccountabiliBuddy.Core/Domain/CategoryBehavior.cs` (`Comms`), `Storage/ProfileDocumentSerializer.cs` (v6: no data change; older apps refuse it), `ProfileDocument.CurrentSchemaVersion = 6`, `DayPlan.Default` (Comms category "Comms" #0EA5E9; workday parts 11:30–11:45, 16:00–16:15), `Engine/RuleEvaluator.cs` or the coordinator (outside a Comms part, the `CommsApps` list counts as Distracting in every profile), `Views/DayPlanView` (category behaviour choice), `android/shared` + `android/app` (read `comms` as a watch-like part; v6 accepted)
- Create: `Core/Domain/CommsApps.cs` (`outlook, olk, teams, ms-teams, slack, thunderbird` + `mail.google.com, outlook.office.com, outlook.live.com, teams.microsoft.com, app.slack.com`)
- Check: synccore `CommsChecks.cs`, ktcheck `CommsChecks.kt`

**Interfaces:**
- Produces: `CategoryBehavior.Comms` (a Watch part where the comms list is allowed); `CommsApps.IsComms(ActivitySnapshot)`; during a session outside a Comms part a comms window is Distracting even if the profile allows it; inside one it's OnTrack.
- Migration v5 → v6 adds the Comms category and the two parts only to an untouched default plan (`DayPlanVersion` equals the default's); edited plans get the category only.

- [ ] **Step 1: Checks:** v5 JSON → v6 with Comms category; default plan gains the two parts, an edited plan doesn't; Outlook at 10:00 in a session → Distracting, at 11:35 → OnTrack; Kotlin reads a v6 plan with a Comms part and treats it as a watch part.
- [ ] **Step 2–4:** RED → implement → GREEN; `./gradlew assembleRelease`.
- [ ] **Step 5: Commit** `Comms windows: mail and chat only in their parts (profile schema v6)`.
- [ ] **Slice 4 ship:** `CLAUDE.md` (schema v6 line, hard rule for comms), 0.1.69, phone APK.

## Not in this phase

Lights out, cross-PC pickups, goal unlock across PCs (phase 3/4, need sync); come-backs in the sync files and on the phone (phase 5, ADR); the shutdown routine (phase 5); notes vs come-backs (phase 5).
