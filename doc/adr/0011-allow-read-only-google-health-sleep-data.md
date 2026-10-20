# 11. Allow read-only Google Health sleep data

Date: 2026-10-19

## Status

Proposed

## Context

ADR 9 lets the desktop app make outbound connections only to calendar sources the user adds, and says any other outbound connection needs a new ADR. Sleep mode now plans a bedtime and wake time and shows how much sleep is left, but the app cannot know how much the user actually slept. The user tracks sleep with a Fitbit or Pixel Watch and with a phone sleep app, and wants those real nights in AccountabiliBuddy.

Android's Health Connect holds sleep from every source on the phone, but it is on-device only and has no cloud API; reading it needs an app installed on that phone, and the Android companion does not exist yet. Google's cloud route is the Google Health API (health.googleapis.com/v4), the successor to the Fitbit Web API, which lists Fitbit and Pixel Watch devices as sleep sources and offers a read-only sleep scope. Its scopes are classed as restricted, the API was still pre-GA in October 2026, and it is not yet confirmed that a personal Google Cloud project in testing mode may use them. The older Google Fit APIs are being retired and were not considered. The user chose to get watch sleep on the PC now through the cloud and to add Health Connect later through the Android companion.

## Decision

We will allow one more read-only outbound connection: Google sign-in and Google Health API calls for sleep data, using only the sleep read scope, for accounts the user connects. It follows ADR 9's rules for Google Calendar: the user's own Google Cloud project and client ID (the app ships with none), the installed-app sign-in flow with a loopback redirect and PKCE, our own small HTTP client rather than Google's libraries, tokens encrypted with DPAPI, and disconnecting deletes the token and revokes it at Google. All Google Health requests live in one small network class next to the calendar one, so the connections remain easy to audit.

Fetched nights are kept only on this computer, encrypted with DPAPI, for 90 days, and can be deleted from the Setup tab. They are display data: they never change what is blocked and are not locked during focus. Nothing is ever written to Google Health. Health Connect data from the Android companion will arrive later over device sync and will be merged with these nights by source.

## Consequences

The user sees real sleep (time asleep, stages, actual bedtime and wake time) next to the planned times, without waiting for the Android app. Google learns the computer's IP address and when it fetches, which the user accepts by connecting the account. The network-egress rule widens again to "only calendar hosts and the Google Health API for accounts the user added". Sleep is sensitive health data, so storage must stay encrypted and deletable. The integration depends on a pre-GA Google API whose scopes were renamed once already and may change again, and on Google allowing restricted scopes for a personal testing-mode project; while the project is in testing, sign-in expires after 7 days as it does for Calendar. If Google blocks or withdraws access, the feature degrades to planned sleep only and the Android Health Connect path becomes the only source.
