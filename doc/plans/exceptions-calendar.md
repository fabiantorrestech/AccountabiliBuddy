# Exceptions tab and calendar: plan

Status: **draft, waiting for approval**.

Decisions so far (2026-10-18):
- In-app calendar, iCal links **and** Google sign-in.
- Time off can be all day or specific hours.
- Time off that starts during a scheduled session ends that session.
- A new "Exceptions" tab.
- Ical.Net for parsing.
- Links are checked every 5 minutes; Settings can only make that longer.
- Refresh fetches everything; hiding a calendar only hides it from view.

## What you get

A new **Exceptions** tab with a month calendar. Each day shows:

| Item | What it does | Strict or loose |
|---|---|---|
| **Events** | From the in-app calendar, iCal links and Google. For planning only | Neither |
| **Time off** | All day, or only certain hours on a date. No scheduled block runs during it: a 09:00–17:00 block with time off 12:00–14:00 runs 09–12 and 14–17. A scheduled session still running when time off starts ends then | Looser, so it gets the 10-min countdown |
| **One-off block** | Extra blocked time on one date with a profile, e.g. Sat 09:00–12:00 exam prep | Stricter, so it applies at once |

- **"Time off for this" on an event** pre-fills the event's date and hours (an all-day event gives an all-day time off). You can edit it before saving.
- **Off days move out of the Schedule tab.** Existing off days become all-day time off.
- **Calendars panel:** each source (in-app, each iCal link, each Google account) lists its calendars.
  - Each calendar has Show/Hide and a colour.
  - Each source shows when it was last updated, or its error.
  - **Refresh** fetches every calendar of every source, hidden ones included.

## Sources and freshness

| Source | How calendars are found | Updates |
|---|---|---|
| In-app | Events you add | Instant |
| iCal link (Outlook, iCloud, Nextcloud, Google "secret address") | One link is one calendar. Links can be grouped under a name, e.g. "Work account" | Every 5 min by default; Settings can make it longer (15, 30, 60 min). Also on Refresh and when the window opens. Skips the download when the server says nothing changed, if it supports that |
| Google sign-in | All calendars of the account are listed automatically | Asks Google for changes only, every 30 s (~1 KB when nothing changed), plus Refresh |

- **True push from Google is not possible:** it needs a public HTTPS server.
- **How fresh Google's iCal links are is unknown;** we'll measure it in E4.

## Google sign-in

- **Sign-in flow:** the standard flow for desktop apps. Google's sign-in page opens in your browser and sends you back to a temporary local address (127.0.0.1); PKCE protects that hand-off.
- **Access:** read-only (`calendar.readonly`).
- **The Google Cloud project is yours:** you create it and paste its client ID in Settings. The app ships with no Google project.
- **Known limit:**
  - While the project is in "Testing", Google ends the sign-in after **7 days**, so you sign in again.
  - Publishing it to "Production" removes that limit, but may show an "unverified app" warning or need Google's review for calendar access. **Not yet confirmed**; checked in E5.
- **Tokens:** the sign-in token is stored with DPAPI encryption. "Disconnect" in the Calendars panel deletes it and revokes it at Google. Unlike Setup's Revoke buttons, it works during focus, because disconnecting a calendar doesn't loosen anything.

## Data

**Profile JSON v4 (ADR 7, with a migration):** `WeeklySchedule` gets:
- `TimeOff`: id, date, optional start/end, note. It replaces `OffDays`; each old off day becomes all-day time off.
- `OneOffBlocks`: id, date, start, end, profile.

**`documents` table, local to this PC:**
- `calendar.events`: in-app events.
- `calendar.sources`: links and Google accounts, calendars, show/hide, colours. Link addresses and tokens are DPAPI-encrypted.
- `calendar.cache`: all fetched events, so the tab works offline. Also Google's change markers, so only changes are asked for.

## Logic

**Core (portable):**
- `WeeklySchedule.FindActive` counts one-off blocks as blocks. Time off cuts a block's occurrence: a scheduled session ends when time off starts, and a block can start when time off ends.
- `LooseningDetector`:
  - Looser: time off added or widened, a one-off block removed or shortened.
  - Stricter: the reverse.
- `CalendarParser` turns `.ics` text into events, with repeats and time zones (Ical.Net).

**App:**
- `CalendarService` runs the timers, Refresh and the cache.
- `GoogleCalendarClient` handles sign-in, the calendar list and the changes-only fetch.
- Network code stays in the App (or Platform) layer, behind a Core port.

## Network (needs ADR 9)

ADR 2 forbids any outbound connection. **ADR 9 (proposed) replaces it.**
- Activity data rules are unchanged.
- The only connections allowed are:
  - read-only downloads of the calendar links you paste in;
  - Google sign-in and read-only Calendar API calls for accounts you connect.
- Nothing about your activity is ever sent. Any other connection still needs a new ADR.

## Dependencies

- **Ical.Net** (MIT): approved 2026-10-18.
- **Google: open question.** Either Google's official .NET libraries (`Google.Apis.Calendar.v3` + `Google.Apis.Auth`, Apache 2.0, larger), or our own small client on .NET's built-in `HttpClient` (no dependency, ~300 lines).

## Milestones

| # | Milestone | Done when |
|---|---|---|
| E1 | Core: time off + one-off blocks, v4 migration, `FindActive`, detector | Headless checks: partial-day time off splits a block and ends a running scheduled session; old off days migrate; looser/stricter classified |
| E2 | Exceptions tab: month view, time off (all day / hours), one-off blocks; off days removed from Schedule | UI test: add 12–14 time off → countdown → block pauses 12–14 |
| E3 | In-app events + Calendars panel (show/hide, colours) | Add, edit, delete events; hiding hides them from the month view |
| E4 | iCal links: groups, fetch every N min (Settings), Refresh, cache, "time off for this" | A local test `.ics` server: events appear; Refresh fetches hidden calendars too; a failed fetch keeps the cache; the address is not readable in the database. Google link freshness measured |
| E5 | Google sign-in: calendar list, 30-s changes-only fetch, Disconnect | With your test project: all calendars listed, a change shows up within ~30 s, Disconnect revokes |
| E6 | Checks | Regression suites, light and dark screenshots |

## Open questions

1. Google: official libraries (new dependencies) or our own small client?
