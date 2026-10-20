# Four-Row Timeline (B2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The day bar above the tabs and the on-screen window's all-day timeline become labelled rows (Calendar, Plan, Focus, Blockers) on one hour axis with the now line, in an order you set in Settings. On the phone, the day plan's parts, in their category colours, show on the Today header, the pinned notification and the widget.

**Architecture:** Two small pure pieces in Core: Cold Turkey's blocks placed on real dates (`BlockerTimeline`) and drift read from History (`IActivityLog.GetDriftAsync` + `DriftMarks`). In the App, a pure `TimelineRowsBuilder` turns one day's data into `DayBarRow`s; `DayBarViewModel` gathers the data and `DayBarControl` draws the rows (full size in the main window, compact on the on-screen window). Settings → Day bar gets row order/visibility, which calendars show and the Cold Turkey toggle, stored in the app-owned "daybar" document (not the profile schema).

On the phone, `DayBarBuilder` puts every plan part (not only Watch parts) on the planned lane with its category's colour and name. `DayBarPainter` draws each span in its own colour, so the Today header, notification and widget all show the day plan (they share the painter).

**Tech Stack:** .NET 10, C#, Avalonia 12, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, System.Text.Json; Android Kotlin (Compose, RemoteViews, Glance) sharing one `DayBarPainter`.

**Spec:** `doc/plans/2026-10-20-body-double-design.md` (part B, "Timeline", "On-screen window"); mockup B ("Labelled lanes") plus the user's Calendar row.

## Global Constraints

- Core has no UI or OS dependencies (ADR 4); one public type per file; file-scoped namespaces; nullable on.
- No tests in the repo (user rule): checks are throwaway console programs in the session scratchpad (`scratchpad/b2check` for Core + App data, `scratchpad/rendercheck` for drawing), printing PASS/FAIL lines.
- No new dependencies. Commit and push only when the user says so; stage new files as they're created.
- Rows *(user)*: Calendar, Plan, Focus, Blockers, in that order by default, reorderable in Settings; all rows share one hour axis and the now line; the on-screen window uses the same rows.
- Calendar row *(user)*: events from the calendars you choose for it; titles on hover.
- Blockers row *(user)*: Cold Turkey's blocks and/or Curbox's, each toggled on or off. Only Cold Turkey here: the PC has no Curbox data yet (the phone's Curbox groups are a separate approved item; schedules need the Curbox API addition).
- History privacy (ADR 8): drift marks use only readable columns (times, app, domain, section, classification).
- The "daybar" document is display only: it can be saved during focus, like today.
- No ADR: UI plus one read query on the existing activity log; nothing costly to reverse.

## Decisions made here (my calls, for the user to check)

1. **Labels:** the main window shows a label column on the left ("Calendar", "Plan", "Focus 2h 55m", "Cold Turkey"). The on-screen window (200 px wide) shows the same rows thinner, with no labels.
2. **Settings → Day bar:** each row has a "Show" checkbox and ↑/↓ buttons. A list of your calendars has "On the timeline" checkboxes; new calendars are on. A "Cold Turkey's blocks" checkbox is on by default. The Blockers row disappears when Cold Turkey isn't installed or the box is off.
3. **Calendar row:** each event in its calendar's colour; all-day events span the bar.
4. **Plan row:** every part in its category colour (Sleep included), with its name when there's room. Clicking it opens the Day plan tab. Clicking a session in the Focus row still opens its profile.
5. **Focus row drift marks** (until part C redefines drift): Distracting time inside focus sessions you started, from History. The same site again within 2 minutes is one mark. Marks are red, at least 2 px wide, and hover shows e.g. "r/manybaggers · 11:35 AM – 11:41 AM (6m)". The headline's summary adds "· 2 drifts" when there are any.
6. **Blockers row:** amber with diagonal stripes, one stretch per switched-on block over its schedule. A block with no schedule spans the whole bar. Nothing shows while Cold Turkey is paused.
7. **Sleep:** the night shading and red "lost sleep" stay behind all rows, as today.
8. **Phone** *(user: the plan on the notification, widget and Today header)*:
   - **What changes:** the planned lane shows every part of the day plan in its category colour. Names show on the Today header when there's room; the notification and widget draw the same lane without names.
   - **What stays:** the event band, the measured-sleep lane and the night shading.
   - **Waits for B3:** the full four rows on the phone. The phone doesn't get today's focus history or Curbox schedules yet, and the phone app already ignores the new "daybar" fields (it reads only range, day start and day end).

## Review Focus

1. A "daybar" document saved before this change (no row, calendar or Cold Turkey fields) loads with all four rows shown in the default order and Cold Turkey on, not reset or failing.
2. Cold Turkey not installed, unreadable or paused: no Blockers stretches (row left out when unreadable/off), and the other rows keep their places.
3. Parts and events crossing the bar's edges (a Sleep part from the evening before, an event past bedtime) are clipped to the bar, never drawn past it.
4. Drift from an omitted session or from app/site time deleted in History doesn't show (it counts nowhere).
5. A calendar that was switched off and later removed is ignored; a newly added calendar shows.

---

### Task 1: Cold Turkey's blocks on real dates (Core)

**Files:**
- Create: `src/AccountabiliBuddy.Core/Domain/BlockerWindow.cs`, `src/AccountabiliBuddy.Core/Engine/BlockerTimeline.cs`
- Test: `scratchpad/b2check/Program.cs` (new console project referencing Core and App, like `dayplancheck`)

**Interfaces:**
- Consumes: `ColdTurkeyBlocks(bool IsPaused, IReadOnlyList<ColdTurkeyBlockState> Blocks)`, `ColdTurkeyBlockState(Name, IsSwitchedOn, IsScheduled, IReadOnlyList<WeekRange> Schedule)`, `WeekRange` (minutes since Sunday 00:00, a Saturday-night span split in two), `LocalWindow`, `DayTimeline.Clip`.
- Produces: `public sealed record BlockerWindow(string Name, LocalWindow Window);` and `public static IReadOnlyList<BlockerWindow> BlockerTimeline.ColdTurkey(ColdTurkeyBlocks blocks, LocalWindow range)` (ordered by start, then name).

- [ ] **Step 1: Write the failing checks** (10/20/2026 is a Tuesday; range Tue 06:00 – Wed 00:00 unless stated)

```csharp
Check("Tue 09:00-17:00 block → one window 09:00-17:00", Windows(Block("Work", Tue(9, 0), Tue(17, 0))) == "Work 10/20 09:00-10/20 17:00");
Check("22:00-02:00 Tue night, range Tue 06:00 - Wed 08:00 → one window", Windows(Block("Late", Tue(22, 0), Wed(2, 0)), Range(Tue(6, 0), Wed(8, 0))) == "Late 10/20 22:00-10/21 02:00");
Check("Saturday 23:00 - Sunday 01:00 (wraps the week) → one window", Windows(BlockFromDay("Wrap", DayOfWeek.Saturday, 23 * 60, 60), Range(Sat(20, 0), Sun(8, 0))) == "Wrap 10/24 23:00-10/25 01:00");
Check("always on (no schedule) → the whole range", Windows(AlwaysOn("All")) == "All 10/20 06:00-10/21 00:00");
Check("switched off → none", Windows(Off("Off")) == "");
Check("Cold Turkey paused → none", BlockerTimeline.ColdTurkey(Paused(Block("Work", Tue(9, 0), Tue(17, 0))), DefaultRange).Count == 0);
Check("two blocks ordered by start, then name", Windows(Block("B", Tue(9, 0), Tue(10, 0)), Block("A", Tue(9, 0), Tue(10, 0))).StartsWith("A "));
```

- [ ] **Step 2: Run** `dotnet run` in `scratchpad/b2check`. Expected: build error, `BlockerTimeline` doesn't exist.
- [ ] **Step 3: Implement.** For each week touching the range (from the Sunday on or before `range.Start` minus one week, until past `range.End`), turn each `WeekRange` into a `LocalWindow` from that Sunday 00:00. Clip it to the range, then merge each block's windows that touch or overlap (this joins the Saturday/Sunday split). A switched-on block with no schedule gets the whole range. A paused Cold Turkey or a switched-off block gets nothing.
- [ ] **Step 4: Run.** Expected: all Task 1 lines PASS.
- [ ] **Step 5:** `git add` both new files; ledger line.

### Task 2: Drift from History (Core)

**Files:**
- Create: `src/AccountabiliBuddy.Core/Domain/DriftStretch.cs`, `src/AccountabiliBuddy.Core/Engine/DriftMarks.cs`
- Modify: `src/AccountabiliBuddy.Core/Ports/IActivityLog.cs`, `src/AccountabiliBuddy.Core/Storage/SqliteActivityLog.cs`
- Test: `scratchpad/b2check/Program.cs` (temporary database through `LocalDatabase` in a temp folder)

**Interfaces:**
- Produces:
  - `public sealed record DriftStretch(DateTimeOffset Start, DateTimeOffset End, string ProcessName, string? Domain, string? Section) { public string Site => Section ?? Domain ?? ProcessName; }`
  - `Task<IReadOnlyList<DriftStretch>> IActivityLog.GetDriftAsync(DateTimeOffset from, DateTimeOffset to, IReadOnlyList<TimeRange>? leaveOut = null, IReadOnlyList<UsageDeletion>? deleted = null, CancellationToken cancellationToken = default)`: segments with `in_focus = 1` and classification Distracting that started in [from, to), oldest first, with the same `LeaveOutFilter`/`DeletedFilter` as `GetSummaryAsync`.
  - `public static IReadOnlyList<DriftStretch> DriftMarks.Merge(IReadOnlyList<DriftStretch> stretches, TimeSpan gap)`: per `Site`, a stretch starting within `gap` of the previous one's end joins it; the result is ordered by start. The App uses `gap` = 2 minutes.

- [ ] **Step 1: Write the failing checks.** Record via `RecordSegmentAsync`:
  - reddit.com/r/manybaggers, Distracting, in focus, 11:35–11:38 and again 11:39–11:41;
  - amazon.com, Distracting, in focus, 14:30–14:32;
  - github.com, OnTrack, in focus;
  - youtube.com, Distracting, not in focus;
  - ebay.com, Distracting, in focus, inside a leaveOut range;
  - x.com, Distracting, in focus, matching a `UsageDeletion` for x.com.

```csharp
Check("drift = distracting time in focus only", Sites(drift) == "r/manybaggers,r/manybaggers,amazon.com");
Check("same site within 2 min merges", Marks(DriftMarks.Merge(drift, TimeSpan.FromMinutes(2))) == "r/manybaggers 11:35-11:41,amazon.com 14:30-14:32");
Check("a different site in between doesn't break the merge", /* reddit 10:00-10:01, amazon 10:01-10:02, reddit 10:02-10:03 */ MergedCount == 2);
```

- [ ] **Step 2: Run.** Expected: build error, `GetDriftAsync` doesn't exist.
- [ ] **Step 3: Implement** the record, the port method, the SQL in `SqliteActivityLog` (select `started_at, ended_at, process_name, domain, section`) and `DriftMarks.Merge`.
- [ ] **Step 4: Run.** Expected: Task 1 and 2 lines all PASS; `dotnet build AccountabiliBuddy.sln` succeeds (no other `IActivityLog` implementations exist).
- [ ] **Step 5:** `git add` the new files; ledger line.

### Task 3: Day bar settings for rows, calendars and Cold Turkey (App)

**Files:**
- Create: `src/AccountabiliBuddy.App/Services/TimelineRow.cs`, `src/AccountabiliBuddy.App/Services/TimelineRowChoice.cs`
- Modify: `src/AccountabiliBuddy.App/Services/DayBarSettings.cs`
- Test: `scratchpad/b2check/Program.cs`

**Interfaces:**
- Produces:
  - `public enum TimelineRow { Calendar, Plan, Focus, Blockers }`
  - `public sealed record TimelineRowChoice(TimelineRow Row, bool Shown);`
  - `DayBarSettings(bool Show, DayBarRange Range, TimeOnly DayStart, TimeOnly DayEnd, IReadOnlyList<TimelineRowChoice>? Rows = null, IReadOnlyList<string>? CalendarsOff = null, bool ShowColdTurkey = true)`
  - `public IReadOnlyList<TimelineRowChoice> RowsInOrder()`: the saved order, with unknown and duplicate rows dropped and missing rows added (shown) in the default order; null gives Calendar, Plan, Focus, Blockers, all shown.
  - `public bool ShowsCalendar(string calendarId)`: false only when the id is in `CalendarsOff`.
  - Value equality: `Equals(DayBarSettings?)` and `GetHashCode` compare `RowsInOrder()` and `CalendarsOff` (null = empty) as sequences, because `SettingsViewModel.DayBarChanged` compares built and saved settings.

- [ ] **Step 1: Write the failing checks**

```csharp
// Review Focus 1: an old document (saved through SqliteJsonDocumentStore on a temp DB as an anonymous object
// { show = true, range = DayBarRange.FitToDay, dayStart = "06:00:00", dayEnd = "00:00:00" }) loads with the defaults.
Check("old daybar document → four rows, default order, all shown", Order(old.RowsInOrder()) == "Calendar+,Plan+,Focus+,Blockers+");
Check("old daybar document → Cold Turkey on", old.ShowColdTurkey);
Check("saved order kept, missing row added at the end", Order(With(Rows(Focus+, Plan-)).RowsInOrder()) == "Focus+,Plan-,Calendar+,Blockers+");
Check("duplicates dropped", Order(With(Rows(Plan+, Plan-)).RowsInOrder()).Split(',').Length == 4);
Check("equal lists → equal settings", With(Rows(Focus+)) == With(Rows(Focus+)));
// Review Focus 5
Check("a calendar not switched off shows", With(off: new[] { "gone:cal" }).ShowsCalendar("new:cal"));
Check("a switched-off calendar doesn't", !With(off: new[] { "work:cal" }).ShowsCalendar("work:cal"));
```

- [ ] **Step 2: Run.** Expected: build error (no `TimelineRow`).
- [ ] **Step 3: Implement** the enum, record and the `DayBarSettings` members.
- [ ] **Step 4: Run.** Expected: all lines so far PASS. If the old-document check fails because System.Text.Json doesn't use the optional parameters' defaults, the fix is in `RowsInOrder()` (null-safe) and a `ShowColdTurkey` default handled in `DayBarSettingsService.LoadBlocking`. Rule on it and ledger it.
- [ ] **Step 5:** `git add`; ledger line.

### Task 4: One day's rows (App, pure)

**Files:**
- Create:
  - `src/AccountabiliBuddy.App/Controls/DayBarRow.cs`
  - `src/AccountabiliBuddy.App/ViewModels/TimelineSession.cs`
  - `src/AccountabiliBuddy.App/ViewModels/TimelineItem.cs`
  - `src/AccountabiliBuddy.App/ViewModels/TimelineDay.cs`
  - `src/AccountabiliBuddy.App/ViewModels/TimelineRowsBuilder.cs`
- Modify: `src/AccountabiliBuddy.App/Controls/DayBarSpan.cs` (new optional parameters only, so the App keeps building)
- Test: `scratchpad/b2check/Program.cs`

**Interfaces:**
- Consumes: `PlannedPart(Part, Category, Window)` (`Name`, `Category.Color`), `BlockerWindow`, `TimelineRowChoice`, `DayTimeline.Clip`.
- Produces:
  - `DayBarSpan(double Start, double End, string Tip, Guid? ProfileId = null, string? Color = null, string? Label = null)`. `Color` is "#RRGGBB" (null = the row's own colour); `Label` is text drawn inside the span.
  - `public sealed record DayBarRow(TimelineRow Kind, string Label, IReadOnlyList<DayBarSpan> Spans, IReadOnlyList<DayBarSpan> Marks);`
  - `public sealed record TimelineSession(LocalWindow Window, string Tip, Guid? ProfileId);`
  - `public sealed record TimelineItem(LocalWindow Window, string Tip, string? Color);`
  - `public sealed record TimelineDay(LocalWindow Range, IReadOnlyList<PlannedPart> Parts, IReadOnlyList<TimelineSession> Sessions, IReadOnlyList<TimelineItem> Drift, IReadOnlyList<TimelineItem> Events, IReadOnlyList<BlockerWindow>? Blockers, TimeSpan Focused);` Here `Blockers` is null when Cold Turkey is off or unreadable.
  - `public static IReadOnlyList<DayBarRow> TimelineRowsBuilder.Build(TimelineDay day, IReadOnlyList<TimelineRowChoice> rows)`:
    - shown rows only, in the given order; Blockers is left out when `day.Blockers` is null;
    - spans are clipped to the range and converted to fractions;
    - labels: "Calendar", "Plan", `$"Focus {Duration(Focused)}"`, "Cold Turkey";
    - Plan spans: `Color` = the category's colour, `Label` = the part's name, `Tip` = "Work · 8:00 AM – 12:00 PM";
    - Focus spans keep `ProfileId`; drift goes in the Focus row's `Marks`;
    - Blockers spans: `Color` "#F59E0B", `Tip` "<block name> · <times>".

- [ ] **Step 1: Write the failing checks**

```csharp
Check("rows follow the saved order", Kinds(Build(day, Rows(Focus+, Plan+, Calendar+, Blockers+))) == "Focus,Plan,Calendar,Blockers");
Check("a hidden row is left out", !Kinds(Build(day, Rows(Calendar-, Plan+, Focus+, Blockers+))).Contains("Calendar"));
Check("no Cold Turkey data → no Blockers row", !Kinds(Build(day with { Blockers = null }, DefaultRows)).Contains("Blockers"));
Check("plan part in its category colour with its name", PlanSpan("Work").Color == "#2563EB" && PlanSpan("Work").Label == "Work");
// Review Focus 3: Sleep from the evening before, range 06:00-24:00 → clipped
Check("parts crossing the edges are clipped", PlanRow.Spans.All(s => s.Start >= 0 && s.End <= 1));
Check("focus label carries the total", FocusRow.Label == "Focus 2h 55m");
Check("drift marks sit on the Focus row", FocusRow.Marks.Count == 2);
Check("sessions keep their profile", FocusRow.Spans.All(s => s.ProfileId is not null));
```

- [ ] **Step 2: Run.** Expected: build error.
- [ ] **Step 3: Implement** the records and the builder. `DayBarLayout` is not touched yet (Task 5), so the App and b2check keep building.
- [ ] **Step 4: Run.** Expected: all lines so far PASS.
- [ ] **Step 5:** `git add`; ledger line.

### Task 5: The day bar gathers the rows' data (App)

**Files:**
- Modify:
  - `src/AccountabiliBuddy.App/Controls/DayBarLayout.cs` → `DayBarLayout(IReadOnlyList<DayBarRow> Rows, IReadOnlyList<DayBarTick> Ticks, double Now, IReadOnlyList<DayBarSpan>? Night = null, IReadOnlyList<DayBarSpan>? Missed = null)` with `Empty`; replaces Planned/Actual/Events, so Tasks 5 and 6 are done back to back before the App builds again
  - `src/AccountabiliBuddy.App/ViewModels/DayBarViewModel.cs`
  - `src/AccountabiliBuddy.App/App.axaml.cs` (both `DayBarViewModel`s: add `coldTurkey`, `deletedUsage`)
  - `src/AccountabiliBuddy.App/ViewModels/MainWindowViewModel.cs` (`DayBar.DayPlanRequested` → `SelectedTab = DayPlanTab`)

**Interfaces:**
- Consumes: `IActivityLog.GetDriftAsync`, `DriftMarks.Merge`, `BlockerTimeline.ColdTurkey`, `ColdTurkeyService.Blocks/Changed`, `DeletedUsageService.Parts/Changed`, `CalendarService.EventsOn` + `CalendarEntry.Calendar.Color`, `DayBarSettings.RowsInOrder/ShowsCalendar/ShowColdTurkey`, `TimelineRowsBuilder.Build`, `DayPlan.PartsIn`.
- Produces:
  - `DayBarViewModel` constructor gains `ColdTurkeyService coldTurkey, DeletedUsageService deleted` (before `TimeProvider time`).
  - `public event EventHandler? DayPlanRequested` + `[RelayCommand] OpenDayPlan()`.
  - `SummaryText` gains "· N drift(s)" when N > 0.

- [ ] **Step 1: Implement.**
  - Drift is reloaded with the session history (same span, every 5 min, 2 s after a session change). The leave-out ranges are the omitted and scheduled records' windows, and the deletions are `deleted.Parts`. Marks are merged with a 2-minute gap and kept only when they fall in a focus session drawn on the Focus row.
  - Events come from visible calendars filtered by `ShowsCalendar(entry.Calendar.Id)`, coloured by the calendar.
  - Blockers come from `BlockerTimeline.ColdTurkey` when `ShowColdTurkey` is on and `coldTurkey.Blocks` isn't null, otherwise null.
  - Plan parts are all of `plan.PartsIn(span)`.
  - Redraw on `coldTurkey.Changed`; reload on `deleted.Changed`.
  - `HasPlannedFocus`, the status text and the range fitting stay on Watch parts, as today.
- [ ] **Step 2: Verify.** `dotnet build AccountabiliBuddy.sln` succeeds once Task 6 lands (do Tasks 5 and 6 back to back); b2check still PASS.
- [ ] **Step 3:** ledger line.

### Task 6: Drawing the rows (App)

**Files:**
- Modify: `src/AccountabiliBuddy.App/Controls/DayBarControl.cs`, `src/AccountabiliBuddy.App/MainWindow.axaml:76-91`, `src/AccountabiliBuddy.App/Views/SessionOverlayWindow.axaml:33-35`
- Test: `scratchpad/rendercheck/Program.cs` (offscreen render to PNG, built to the short path `%TEMP%/abrc`)

**Interfaces:**
- Consumes: `DayBarLayout.Rows`, `DayBarRow`, `DayBarSpan.Color/Label`.
- Produces: `DayBarControl.OpenDayPlanCommand` (StyledProperty, `ICommand?`), bound to `OpenDayPlanCommand` in the main window.

- [ ] **Step 1: Write the render check.** Build a `DayBarLayout` with `TimelineRowsBuilder` from the Task 4 sample day:
  - work, lunch, work, after-office, sleep;
  - two sessions and two drift marks, one of them 1 minute long;
  - one event;
  - two Cold Turkey windows.

  Render it at 900×120 (normal) and 200×60 (compact, dark theme) to `b2-normal.png` and `b2-compact.png`.
- [ ] **Step 2: Run.** Expected: build error (`DayBarLayout.Rows` not drawn / old members gone).
- [ ] **Step 3: Implement.**
  - **Sizes:** normal has an 84 px label column, 14 px rows, 3 px gaps and the axis under the last row. Compact has no labels, 6 px rows and 2 px gaps.
  - **Behind the rows:** the night and lost-sleep shading, across the full height of the rows.
  - **Each row:** a track, then its spans in `Color` (or the row default: teal `#0F766E` for Focus, slate for Calendar). The Plan label is drawn in white when the span is at least 28 px wide (normal only). Blockers spans are amber with 45° darker stripes, clipped to the span.
  - **Focus row:** red `#EF4444` drift marks on top, at least 2 px wide.
  - **Now line:** one line through every row.
  - **Hover:** the tip comes from the row under the pointer (plus night/lost sleep).
  - **Clicks:** a Focus span with a profile runs `OpenProfileCommand`. A Plan span runs `OpenDayPlanCommand`.
  - **Height:** `MeasureOverride` height follows the number of rows.
- [ ] **Step 4: Run the render check and look at both PNGs.** Expected:
  - four labelled rows in order;
  - Plan parts in their colours with names;
  - the 1-minute drift is visible;
  - the stripes are visible;
  - the compact image is readable at 200 px.

  `dotnet build AccountabiliBuddy.sln` succeeds.
- [ ] **Step 5:** ledger line.

### Task 7: Settings → Day bar (App)

**Files:**
- Create: `src/AccountabiliBuddy.App/ViewModels/TimelineRowSettingViewModel.cs`, `src/AccountabiliBuddy.App/ViewModels/TimelineCalendarViewModel.cs`
- Modify: `src/AccountabiliBuddy.App/ViewModels/SettingsViewModel.cs` (load, `BuildDayBar`, change tracking), `src/AccountabiliBuddy.App/Views/SettingsView.axaml:50-66`

**Interfaces:**
- Consumes: `DayBarSettings` (Task 3), `CalendarService.Sources` (`CalendarSource.Name`, `CalendarInfo.Id/Name/Color`), `CalendarService.Changed`.
- Produces:
  - `TimelineRowSettingViewModel(TimelineRow Row, string Name, bool Shown)`, with `Shown` observable and `MoveUp`/`MoveDown` commands handled by the parent list;
  - `TimelineCalendarViewModel(string Id, string Name, string SourceName, string Color, bool OnTimeline)`;
  - `SettingsViewModel.TimelineRows`, `TimelineCalendars`, `ShowColdTurkeyOnTimeline`.

- [ ] **Step 1: Implement.** `BuildDayBar` saves rows in list order, `CalendarsOff` = the unchecked calendar ids, and `ShowColdTurkey`. Any edit marks Settings unsaved, as other fields do. The view goes under "Day bar":
  - "Rows, top to bottom": the checkbox, name and ↑ ↓ for each row;
  - "Calendars on the timeline": one checkbox per calendar with its colour dot and its source name;
  - "Show Cold Turkey's blocks";
  - the section's description is updated to the rows.
- [ ] **Step 2: Verify.**
  - **Build:** the solution builds.
  - **Render check:** extend `rendercheck` to open `SettingsView` on a temp DB, move Focus up, untick Calendar and Save. Expected: the saved "daybar" document has Focus first and Calendar hidden, and Settings shows no unsaved changes after the save.
- [ ] **Step 3:** `git add`; ledger line.

### Task 8: The day plan on the phone (Today header, notification, widget)

**Files:**
- Modify:
  - `android/app/src/main/java/com/accountabilibuddy/companion/engine/DayBarSpan.kt`
  - `android/.../engine/DayBarBuilder.kt` (planned lane)
  - `android/.../ui/DayBarPainter.kt` (per-span colour and names)

**Interfaces:**
- Consumes: `DayPlan.partsIn(range)` (Kotlin port; `PlannedPart.category.argb`, `PlannedPart.name`), `DayTimeline.clip`.
- Produces:
  - `DayBarSpan(start, end, label, profileId = null, color: Int? = null, name: String? = null)`;
  - `DayBarModel.planned` now holds every plan part (the status and summary texts still use Watch parts via `DayTimeline.planned`).

- [ ] **Step 1: Implement.**
  - **Builder:** planned spans come from `document.dayPlan.partsIn(span)`, clipped to the range, with `color` = the category's argb, `name` = the part's name and `label` = "Work · 8:00 AM – 12:00 PM".
  - **Spans:** the painter draws each planned span in its `color` (the teal stays the fallback). A night part drawn on the colorized notification keeps enough contrast: the painter uses 85% opacity, as the PC does.
  - **Names:** when not `compact` and the span is at least 28 dp wide, the name is drawn in white, 10 sp, trimmed to fit.
- [ ] **Step 2: Verify.**
  - **Build:** `./gradlew assembleDebug assembleRelease` gives BUILD SUCCESSFUL.
  - **Phone:** install the release APK on the Pixel 7 (`29071FDH200AFV`). With the user, check the Today header (Work/Lunch/Work/Sleep in their colours with names), the pinned notification (collapsed and expanded) and the widget (colours, no names).
  - **Notification text:** `dumpsys notification` still reads "… Work until 5:00 PM" / "… of work planned".
- [ ] **Step 3:** ledger line.

### Task 9: Docs, version, review, install

**Files:**
- Modify:
  - `CLAUDE.md` (the project's: the "daybar" document now also holds rows, calendars on the timeline and the Cold Turkey toggle; roadmap "B2 built");
  - `doc/plans/2026-10-20-body-double-design.md` (progress table);
  - `src/AccountabiliBuddy.App/AccountabiliBuddy.App.csproj` (0.1.47).

- [ ] **Step 1:** Update the docs and the version; rerun b2check, dayplancheck, editorcheck, sleepcheck and rendercheck. Expected: all PASS.
- [ ] **Step 2:** Final whole-branch review (fresh reviewer on the most capable model, with the user's OK), fix pass, ledger.
- [ ] **Step 3:** Commit and push on the user's OK; install 0.1.47 and the phone APK, then look at the real day bar, on-screen timeline and phone with the user.
