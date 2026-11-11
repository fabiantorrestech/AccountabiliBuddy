# 78. Export task reminders to the app's own Google calendar

Date: 2026-11-11

## Status

Accepted

Amends [9. Allow read-only calendar connections](0009-allow-read-only-calendar-connections.md) (reading stays as it was; this adds one write path)

## Context

The Tasks redesign (ADR 76, `05 Shared behaviour` §7, `02 Data model and sync` §6) gives tasks a due date and up to five reminders, and asks for them to appear in the user's Google Calendar, one event per task with the reminders as the event's reminder overrides. Until now the app only read calendars: ADR 9 allows read-only iCal downloads and Google Calendar with the `calendar.readonly` scope, and the hard rules say any other connection needs a new ADR. Writing events needs a broader Google permission, and the user must consent to it again in their own Google Cloud OAuth app (the app uses the user's own client, set up through `GoogleSetupSteps`).

Google offers `https://www.googleapis.com/auth/calendar.app.created`, which lets an app create secondary calendars and see, create, change and delete events only on the calendars it created. It cannot read or change the user's other calendars. Writing into the user's main calendar instead needs `https://www.googleapis.com/auth/calendar.events`, which lets the app change events in every calendar the user has.

The owner decided (2026-11-11): export task reminders and due dates; by default into the app's own calendar, with experimental support for adding them to another (main) calendar as well.

## Decision

The app may write to Google Calendar for task reminders and due dates only, through the existing calendar code in `CalendarNetwork` / `CalendarSyncService`, with the user's own OAuth client.

By default it asks for `calendar.app.created` in addition to `calendar.readonly`, creates one secondary calendar named "AB Reminders", and writes every exported event there: a task with a due date becomes an event at its due time, and a task with reminders but no due date becomes an anchor event, with the task's reminders as the event's reminder overrides (Google allows five, which is why a task holds at most five). It is one event per task, updated when the task's title, reminders or due date change and deleted when the task is finished, archived or deleted.

As built (M3 task 3, `TaskCalendarMapping`, `TaskCalendarDiff`, `GoogleCalendarWriter`, `TaskCalendarExportService`):

- A due date with a time is a 15-minute event at that time. A due date alone is an all-day event; Google counts an all-day event's reminders back from its midnight start, so reminders on the day itself (e.g. "At start time", which rings at 9:00 AM for a whole-day due date) can't be carried and are left off the event (they still ring in the app; the log counts them). Reminders but no due date (only "at" reminders can ring then): the anchor is a 15-minute event at the task's last reminder, every reminder an override before it, so each still rings at its own time. Overrides are 0 to 40320 minutes (four weeks), at most five; events are marked free.
- Event ids are fixed per task (`ab` + the task id's hex digits, Google's base32hex alphabet), so every device and a reinstall reach the same event and never make a second one (an id Google already holds, also a deleted one, is replaced). The events this PC wrote are recorded device-local (document `tasks.calendarExport`: account, the "AB Reminders" id, per task the calendar, event id and a hash of the content; no text), and only recorded events are ever changed or deleted. The synced `calendarEventId` on reminders is left unset: writing it from the exporter would stamp the task's reminders field and could override a reminder edit from another device that hasn't arrived yet.
- Existing users: export stays off (`NeedsConsent`) until they press Allow (Settings, M7), which runs Google sign-in again for `calendar.readonly` + `calendar.app.created` with `include_granted_scopes`; the account must be the connected one, and its new refresh token replaces the old. New connections still ask read-only only. Granted scopes are read from Google's token answers, so a sign-in without the scope turns export off again by itself. Reading calendars never needs the new scopes.
- The reading side leaves "AB Reminders" out (its events are the user's tasks, already in the app).
- Writes follow every change of the tasks or the settings two seconds later, in the background; a failure waits 30 seconds, doubling to 30 minutes, and never stops the app. With nothing to write the app makes no call to Google at all.

An experimental option, off by default and labelled as experimental, lets the user pick one of their own calendars for due-dated tasks: those tasks' events then go there instead of to "AB Reminders" (still one event per task; tasks without a due date stay in "AB Reminders"). Only when the user turns it on does the app ask for `calendar.events` (incremental consent); turning it off stops writing there, deletes the events the app made there and puts them back in "AB Reminders". Without that consent nothing is written outside the app's own calendar.

Only a task's title and times leave the device. Notes, steps, tags, labels, reasons and window data never do. The setting "Add new reminders to" (Google Calendar, the default; Nowhere; device-local in `tasks.reminderSettings`, with the picked calendar) and the per-task "Add to Google Calendar" switch decide what is exported: a task takes the setting's value when it gets its first reminder or due date (`TaskCalendarOptIn`), and existing tasks are never exported without the user turning it on for them. Windows Calendar is not built (no API usable from the desktop app without new connections was confirmed).

## Consequences

The app gains its first write path to an outside service other than the user's own Drive folder and relay: a mistake can now create or delete events in the user's calendar, so writes are limited to events the app created and recorded (fixed per-task event ids, recorded device-local in `tasks.calendarExport`), and the main-calendar option stays experimental until it has been used for a while. Existing users must re-consent before export works; until then export is off and the app keeps reading calendars as before. A whole-day due date can't carry same-day reminders to Google (an all-day event's reminders count from midnight); if that matters, a later change can make such tasks a timed event at 9:00 AM instead. The setup guide's scope list and the "What syncs / what leaves this PC" texts must name the new scopes. Task titles now appear in the user's Google account, which the user opts into by choosing Google Calendar in "Add new reminders to".
