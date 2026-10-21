# Hand-Added Focus Sessions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In History, add a past focus session by hand (date, start, end, optional note), shown in a lighter striped shade, counted everywhere a recorded session counts, with no overlaps, and editable or deletable later.

**Architecture:**
- **Storage (ADR 20):** session events of a new mode `added` in the activity history, written through new `IActivityLog` methods that touch only `added` events. The DB schema version goes up by one, with no table change.
- **Rules:** a pure Core `AddedSessionRules.Validate` checks length and overlaps.
- **History:** lists added sessions in their own shade with Edit and Delete, and adds their length to its focus time.
- **Day bar:** draws them striped on the Focus row, counts them in the focused total, and clicking empty time on the Focus row offers to add one there.

**Tech Stack:** .NET 10, C#, Avalonia 12, Microsoft.Data.Sqlite.

**Spec:** the user's answers on 2026-10-20:
- counts everything;
- date, times and an optional note (no profile);
- no overlaps; editable later;
- both an "Add a past session…" button and clicking empty time.

ADR 20 (Accepted 2026-10-20 with this plan).

## Global Constraints

- ADR 8: the times and the mode are readable; the note is encrypted with `IDataProtector`; kept 90 days.
- Core has no UI dependencies. No new dependencies. No tests in the repo; checks go in scratch programs (`scratchpad/addedcheck`).
- Recorded sessions are never changed by these methods.
- Not locked during focus (the user didn't pick that rule).
- Not limited to the past (the user didn't pick that rule). A session can't end more than 24 hours after it starts, and it must last at least 1 minute *(my call)*.

## Decisions (my calls, for the user to check)

1. **"Click empty time"** means the empty part of the day bar's **Focus** row above the tabs (History has no timeline of its own). It opens the same form starting at the clicked quarter hour, lasting 30 minutes.
2. **Shade:** the same teal at half strength with diagonal stripes. Hover says "Added by hand · 9:00–10:30 AM" plus the note. In History's list it gets a lighter row and an "added" tag.
3. **History's focus time** adds the added sessions' length inside the period. Its on-track and distracted split stays from recorded activity only, since added sessions have no activity.
4. **The phone** counts them once it shows focus history (B3). Today the phone has no focus history to put them in.

## Review Focus

1. Overlap rules:
   - an added session touching a recorded one end-to-start is allowed;
   - one inside or across a recorded session is refused;
   - editing a session doesn't count it as overlapping itself.
2. Deleting or editing never removes a recorded session's events, including one with the same start time.
3. An omitted recorded session still blocks overlaps. It exists, only its time doesn't count.
4. A backup restore brings added sessions back with their notes. Without the encryption key, the note is unreadable but the session stays.
5. A day bar click on empty Focus time, inside the bar's day but not on a session, prefills the form. A click on a session still opens its profile.

---

### Task 1: Storage and rules (Core)

- **Add:**
  - `SessionMode.Added`;
  - `AddedSessionRules.Validate(DateTimeOffset start, DateTimeOffset end, IReadOnlyList<SessionRecord> sessions, DateTimeOffset? editingStart) -> string?`;
  - `IActivityLog`: `AddSessionAsync(start, end, note)`, `ReplaceAddedSessionAsync(oldStart, start, end, note)`, `DeleteAddedSessionAsync(start)` (SQLite: only rows with mode `added`);
  - the DB schema version +1 with a backup-before-migrate as usual.
- **Checks (addedcheck, temp DB):**
  - write, read back through `SessionHistoryBuilder` (mode Added, note decrypted);
  - edit and delete leave a recorded session with the same start alone;
  - the overlap cases from Review Focus 1 and 3;
  - an older-schema DB migrates.

### Task 2: History

- **History view model and view:**
  - an "Add a past session…" button above Sessions;
  - a form: a date picker, start and end time pickers (15-minute steps), a note, the rules' message, Save and Cancel;
  - added rows shaded, with Edit and Delete;
  - focus time includes added sessions.
- **Checks:** the view model on a temp DB (add → listed and counted; overlap → refused with the message; edit; delete). Render the form and a shaded row.

### Task 3: Day bar

- **Rows and control:**
  - `TimelineSession.Added` gives a striped, lighter span and an "Added by hand" tip;
  - the focused total includes added sessions;
  - `DayBarControl` raises `AddSessionRequested(DateTime)` on a click in empty Focus-row time;
  - the main window opens History's form prefilled.
- **Checks:** the builder (an added session is striped and counted); a render.

### Task 4: Ship

- Update the project's `CLAUDE.md` (history: added sessions) and ADR 20 → Accepted.
- Version.
- Fresh opus review, then one fix pass.
- Commit, push and install.
