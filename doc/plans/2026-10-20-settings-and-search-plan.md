# Settings Layout and App-Wide Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two changes:
- **Settings:** no longer one long list. A side list of 4 groups shows one group's sections at a time, as cards with bigger headers and room around them.
- **Search:** a box in the main window's top right highlights matches on the current page as you type. "Search All Results…" lists every match in the app, both your own things and the app's words, and clicking one opens that tab and section with the word highlighted.

**Architecture:**
- **Matching** is pure: `TextMatcher` finds case-insensitive occurrences.
- **Two sources feed one result list:**
  - `DataSearchSource` (pure) turns the app's data into entries: profiles and rules, the day plan, one-off days, calendar events, apps and sites in History.
  - `UiSearchSource` walks each page's logical tree for the words the app shows (tab, section and setting texts). It is tagged by the `SearchArea` attached properties on Settings cards and groups.
- **On the page,** `SearchHighlightLayer`, an overlay above the pages, draws highlights over matching text (Avalonia `TextLayout.HitTestTextRange`) and moves between matches.
- **Navigation:** a `SearchTarget` says which tab, Settings group, profile, date or control to show.

**Tech Stack:** .NET 10, Avalonia 12, CommunityToolkit.Mvvm.

**Spec:** the user's request and the approved description on 2026-10-20:
- **Settings:** layout B (mockup `settings-layout.html`). Groups (my guess, not objected to): Focus sessions, Your day, On screen & sound, App & data.
- **Search:** behaves as in mockup `search.html`; approved with "nothing, go ahead and implement".

## Global Constraints

- No new dependencies; no tests in the repo (scratch checks: `scratchpad/searchcheck` (pure + data), `scratchpad/b2render`-style offscreen render for the UI).
- One public type per file; nullable on; explicit readable code.
- Search is local and in memory. Nothing new is stored, and nothing leaves the PC. History matching uses only readable columns (app, domain, section) through `GetSummaryAsync`, over the 90 days kept.
- Matches are case-insensitive "contains" on the typed text, with at least 1 character after trimming. Results are capped at 200.
- Settings keeps its single Save/Revert header and its focus lock. Moving sections changes no setting.
- Light and dark themes both work. Cards use translucent backgrounds, not fixed colours.

## Decisions (approved in chat, plus my calls marked)

1. **Groups:**
   - **Focus sessions:** Quick session shortcut, Pomodoro defaults, Escalation, Ending a focus session early, Unlocks, Looser changes.
   - **Your day:** Day bar, Sleep, Calendars.
   - **On screen & sound:** On-screen timer, On-screen timeline, Sounds when time is up, Noise player.
   - **App & data:** Updates, Backup, Data.

   Under each group name, the side list shows that group's section names; clicking one scrolls to it *(my call)*.
2. **Cards:** each section is a card with a 20 px header, 16 px padding and 16 px between cards *(my call)*.
3. **Dropdown:** under the box it shows "N on this page · Enter = next, Esc = clear" and "Search All Results… (M in the app)". When nothing is on the page, "Search All Results…" is the first line. Enter with nothing on the page opens all results.
4. **All results:** shown in the dropdown panel (scrolls), grouped by tab. Each row has the text with the match highlighted, a path ("Settings › Your day › Sleep") and a tag: setting, your plan, your profiles, your calendar, your history, app.
5. **Opening a result:**
   - Profiles: selects the profile.
   - Exceptions: shows the date.
   - Settings: picks the group.
   - Any app word: brings that control into view.

   The section card flashes once, and the word stays highlighted until the box is cleared.
6. **Highlight colours:** semi-transparent yellow over every match, orange over the current one *(my call)*.

## Review Focus

1. Typing fast: each keystroke re-searches without freezing. The page highlight is debounced to ~120 ms and the History query runs once per "Search All Results…".
2. Highlights follow scrolling and window resizing, and disappear on a tab switch until recomputed for the new page.
3. A Settings result in a hidden group: the group switches first, then the control is brought into view.
4. Text in templates of hidden pages (lists not realized yet) is still found, through the data source.
5. Clearing the box (Esc or empty) removes every highlight and closes the dropdown.

---

### Task 1: Matching (pure)

- Create `src/AccountabiliBuddy.App/Services/Search/TextMatcher.cs`: `public static IReadOnlyList<(int Start, int Length)> Find(string? text, string query)`.
- [ ] Failing checks:
  - "Sleep mode on" with "SLEEP" gives (0,5);
  - "sleepfoundation.org sleep" with "sleep" gives 2 matches;
  - a blank query gives none;
  - overlapping "aaa" with "aa" gives 1 (no overlaps).
- [ ] Implement; checks PASS.

### Task 2: Your data as search entries (pure)

- Create `Services/Search/SearchEntry.cs` `(string Text, string Path, string Kind, SearchTarget Target)`.
- Create `Services/Search/SearchTarget.cs` `(int Tab, Guid? ProfileId = null, DateOnly? Date = null, int? SettingsGroup = null, object? Control = null)`.
- Create `Services/Search/SearchKinds.cs` (constants).
- Create `Services/Search/DataSearchSource.cs`:
  - signature: `public static IReadOnlyList<SearchEntry> Entries(ProfileDocument document, IReadOnlyList<CalendarEvent> events, IReadOnlyList<AppSiteUsage> usage, IReadOnlyDictionary<string, string>? shownNames)`;
  - profiles: "<name>", path "Profiles", target the profile;
  - rules: "<pattern> (block|allow <target>)", path "Profiles › <name>";
  - categories: "Categories › <name>";
  - plan parts: "<label or category> 9:00 AM – 12:00 PM", path "Day plan › Workdays|Other days";
  - one-off days: "Exceptions › <date>";
  - events: "<title> · <date>", path "Exceptions › Calendar";
  - History apps and sites, with sections and shown names: "History › Most used".
- `public static IReadOnlyList<SearchEntry> Filter(IEnumerable<SearchEntry> entries, string query, int cap = 200)`.
- [ ] Failing checks: a rule "reddit.com" is found by "redd" with the Profiles tab and its profile id; "sleep" finds the Sleep category and Sleep parts; an event title; a History site section "r/manybaggers"; the cap holds.
- [ ] Implement; PASS.

### Task 3: Settings layout B

- Modify `ViewModels/SettingsViewModel.cs`:
  - `public IReadOnlyList<SettingsGroup> Groups` (`SettingsGroup(string Name, IReadOnlyList<string> Sections)`, new file);
  - `[ObservableProperty] int SelectedGroupIndex`.
- Modify `Views/SettingsView.axaml`: a left list of groups with their sections, and a right ScrollViewer with 4 group panels (shown by `SelectedGroupIndex`), sections as `Border.card`. Header text class `sectionTitle` (20 px). Each card gets `search:SearchArea.Name`; each group panel gets `search:SearchArea.SettingsGroup`.
- Create `Services/Search/SearchArea.cs`: attached properties `Name` (string) and `SettingsGroup` (int, -1 = none).
- [ ] Verify:
  - **Render:** Settings at 1100×800, dark and light, group 2 selected. Expected: the side list, cards and bigger headers.
  - **Save check:** the existing b2render Settings save check still passes.

### Task 4: The app's words and the page highlight

- Create `Services/Search/UiSearchSource.cs`: `public static IReadOnlyList<SearchEntry> Entries(IReadOnlyList<(int Tab, string TabName, Control Page)> pages)`.
  - Walks logical descendants: `TextBlock.Text`, and string `Content` of `ContentControl`s (CheckBox, RadioButton, Button).
  - Skips empty and pure-number text.
  - Path: the tab name plus the `SearchArea.Name`s of its ancestors (and the group name for Settings). Kind = "setting" on Settings, else "app".
  - Target: the tab, the Settings group, and the control.
- Create `Controls/SearchHighlightLayer.cs`, a Control above `Pages` that doesn't take clicks:
  - `Query` and `Page` properties;
  - recomputes, debounced to 120 ms, on query, layout or scroll;
  - draws rects; `MatchCount`, `CurrentIndex`, `Next()` (brings the current one into view), `RevealControl(Control)`.
- [ ] Checks (render harness): Settings + layer with "sleep" gives MatchCount > 0 on the "Your day" group. `UiSearchSource` finds "Sleep mode" with the path "Settings › Your day › Sleep" and SettingsGroup 1.

### Task 5: The search box and opening results

- Create `ViewModels/SearchViewModel.cs`: `Query`, `PageCount`, `AllCount`, `ShowsAll`, `Results` (grouped rows with highlighted runs), `NextCommand`, `ClearCommand`, `SearchAllCommand`, `OpenCommand(SearchResultRow)`.
  - Sources come in as delegates: data entries (built on demand from the services; the History summary covers the last 90 days) and UI entries.
- Create `ViewModels/SearchResultRow.cs`, `ViewModels/SearchResultGroup.cs`, `Views/SearchBox.axaml(.cs)` (TextBox + Popup).
- Modify:
  - `MainWindow.axaml`: a top row in the right pane with the box, and the layer over `Pages`;
  - `MainWindow.axaml.cs`: navigation — select the tab, then the group, profile or date, then reveal the control, then flash its card;
  - `MainWindowViewModel.cs`, `App.axaml.cs` (wiring);
  - `ExceptionsViewModel.cs`: `ShowDate(DateOnly)`.
- [ ] Checks (render harness on a temp DB, a MainWindow-like host):
  - typing "sleep" on Settings gives PageCount > 0;
  - SearchAll lists a Day plan "Sleep" entry;
  - opening a Settings result in another group switches the group;
  - Esc clears the highlights.
- [ ] Hand check after install.

### Task 6: Ship

- [ ] Version 0.1.48; the project's `CLAUDE.md` (Settings groups, search); rerun all checks; fresh opus review; one fix pass; commit, push, install (user OK).
