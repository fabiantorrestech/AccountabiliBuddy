# 9. Allow read-only calendar connections

Date: 2026-10-18

## Status

Accepted

Supersedes [2. Process all activity data locally](0002-process-all-activity-data-locally.md)

## Context

ADR 2 keeps all activity data on the device and forbids the desktop app from making any outbound internet connection. The new Exceptions tab lets the user plan time off and one-off blocks around the calendar they already keep. The user wants to see events from Google Calendar, Outlook, iCloud or Nextcloud there, choose which calendars are shown, and get changes quickly.

Every major calendar service publishes a read-only iCal (.ics) address per calendar. Fetching one is a plain HTTPS download that sends nothing about the user, but it has no push, so freshness depends on how often the app polls. Google's Calendar API lists every calendar of an account and can return only what changed since the last request, which makes polling every 30 seconds cheap. That API needs Google sign-in (OAuth) and stored tokens, and Google's push notifications need a public HTTPS server, which a desktop app cannot provide. CalDAV was left for later, with the self-hosted sync server. The user asked that both iCal links and Google sign-in ship together, and that all data still stays local.

## Decision

Activity data stays on the device exactly as ADR 2 decided. Window titles, URLs, screen text, labels, reasons and history are never sent anywhere, and the browser extension still talks only to the local native messaging host.

The desktop app may make two kinds of outbound connection, both read-only and both for calendars the user adds:

- HTTPS downloads of iCal addresses the user pastes in, every 5 minutes by default (the user can only make this less frequent), on Refresh, and when the window opens.
- Google sign-in and read-only Google Calendar API calls (`calendar.readonly` scope) for accounts the user connects, using the installed-app sign-in flow with a loopback redirect to 127.0.0.1 and PKCE. The Google Cloud project and its client ID belong to the user; the app ships with none.

Fetched events are cached locally. iCal addresses and sign-in tokens are stored encrypted with DPAPI, and disconnecting an account deletes its token and revokes it at Google. Requests carry nothing beyond what the protocol requires. The Google client is our own small HTTP client rather than Google's libraries. Any other outbound connection still requires a new ADR.

## Consequences

The user sees their real calendars while planning, and Google changes show up within about 30 seconds. The calendar providers learn the computer's IP address and when it fetches, which the user accepts by adding each source. "No network egress" can no longer be checked as zero connections; it becomes "only calendar hosts the user added", and the code that makes those connections must stay in one service that is easy to audit. Google sign-in brings Google's rules with it: while the user's project is in testing, Google ends the sign-in after 7 days, and publishing it may require Google's review. Device sync (doc/plans/device-sync.md) must build on this record rather than ADR 2 when it relaxes the rules further.
