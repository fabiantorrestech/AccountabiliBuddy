# Sleep tracking: plan

Status: **planning**. Cloud first (Google Health API, [ADR 11](../adr/0011-allow-read-only-google-health-sleep-data.md), Proposed), Health Connect later in the Android companion.

## Goal

Next to the planned bedtime and wake time from sleep mode, show how you actually slept: when you fell asleep and woke, time asleep, stages, and how that compares with your goal over the last weeks. Optional, read-only, kept on your devices.

## Sources

| Source | Covers | Reached by | When |
|---|---|---|---|
| Google Health API (`health.googleapis.com/v4`) | Fitbit and Pixel Watch sleep in your Google account | The Windows app, Google sign-in (like Calendar) | Now (S1–S4) |
| Health Connect (on the phone) | Every app that writes sleep: watch apps, phone sleep apps, Samsung, Oura… | The Android companion, then device sync to the PC | V3 (S5) |
| Google Fit APIs | — | — | Not used: being retired |

The cloud API does not list phone or sleep-app data as a source, so those nights wait for the Android app.

## Phases

### S1. Spike (throwaway, scratch only)

Answers: can a personal Google Cloud project in testing mode read sleep with the restricted scope, and what do real nights look like?

You, in the Google Cloud project already used for Calendar:
1. APIs & Services → Library → **Google Health API** → Enable.
2. Google Auth platform → Data access → Add scope: the **sleep read** scope (`…/auth/googlehealth.sleep.readonly`, or the current name if Google renamed it again).
3. Audience → Test users: your Google account is listed.
4. The existing Desktop OAuth client is reused (the one in Settings → Calendars).

Me: a scratch console that reuses the app's sign-in flow (loopback + PKCE), reads the client from the app's saved calendar settings, lists the last 7 nights (`GET /v4/users/me/dataTypes/sleep/dataPoints`, filtered by end time) and prints start, end, minutes asleep and stage minutes. Nothing is stored.

Done when: 7 nights print, or we know exactly why Google refuses (then skip to S5 and keep ADR 11 Proposed/Rejected).

### G. Google setup guide (in the app)

Setting up the Google Cloud project is the hardest part for a new user, and Calendar and Sleep share it. A guide in the app walks through it step by step:

- Opens from Settings → Calendars ("Set up Google step by step…") and Settings → Sleep, and from the setup assistant.
- One step per screen, with a progress list on the left: create the project, enable the APIs (Calendar, and Google Health when sleep is wanted), branding (app name + your email), audience (External, Testing, add yourself as test user), data access (add the read-only scopes), create a Desktop client, paste the client ID and secret, test.
- Each step: what to click in plain words, an **Open this page** button that goes straight to the right Google Cloud Console page, **Copy** buttons for exact values (scope strings, app name), and a "Done" check. The steps remember progress.
- Last step: paste the client ID and secret (saved encrypted as today), then **Test**, which signs in and reports what works (Calendar list, sleep nights) or the exact Google error with the step to fix.
- Explains the 7-day sign-in expiry in Testing mode and what "unverified app" warnings mean.
- Console links and scope names live in one place in the code, so they can be fixed when Google moves pages.
- Done when: a fresh Google account can go from nothing to a working Calendar (and Sleep, after S1) using only the guide.

### S2. Accept ADR 11

Update ADR 11 with what the spike found (scope name, testing-mode behaviour), set it to Accepted, and update CLAUDE.md's hard rule on outbound connections.

### S3. PC integration

- `HealthNetwork` (one auditable class, like `CalendarNetwork`): token refresh, sleep list call, nothing else.
- Settings → Sleep: **Connect Google Health** (status, last sync, Disconnect = delete + revoke token).
- Sync: on launch, when the window opens, every 30 min, and hourly for the 3 hours after wake time (Fitbit uploads late). Only the last 14 days are requested after the first sync (first sync: 90 days).
- Storage: documents `sleep.nights`, DPAPI-encrypted JSON (start, end, minutes asleep/awake, stage minutes, source device), kept 90 days. Setup tab: Revoke + Delete like activity history.
- Done when: connect, sync, disconnect and delete work against your account; a night appears after the watch syncs; restarting keeps it.

### S4. Sleep tab and visuals

- **Sleep tab** (new, in the sidebar after History):
  - Last night card: asleep 6h 52m (coloured like the sleep line), fell asleep 11:48 PM vs bedtime 10:00 PM, woke 6:40 AM vs 6:00 AM, stage bar (awake/light/deep/REM).
  - 14-night chart: time asleep per night against the goal line (7 h), bedtime drift dots.
  - Averages for 7 and 30 nights: time asleep, bedtime, wake time, nights at 7 h+.
  - Planned times link to Settings → Sleep.
- **Day bar**: the morning's night band shows real sleep (solid) instead of planned (shaded) once synced; tooltip with stages.
- **Morning line** (overlay + header, for 2 h after wake time): "Last night: 6h 52m asleep · bed 11:48 PM", green/yellow/red.
- Done when: screenshots of the tab with real data in light and dark themes, day bar with a real night, morning line colours.

### S5. Android (V3, with device sync)

The companion reads Health Connect `SleepSessionRecord` (with stages) after the user grants the sleep read permission, and sends nights to the PC over device sync. Nights from both sources are merged by time: same device/source kept once, otherwise the watch's night wins and the phone's fills gaps.

## ADRs

- ADR 11 (this plan): read-only Google Health sleep data. Proposed until S1.
- Device sync and the Android companion get their own ADRs (see device-sync.md).

## Risks

- Pre-GA API: scopes were renamed once (May 2026) and may change again.
- Restricted scopes may not be allowed for an unverified personal project; testing-mode sign-ins expire after 7 days (same as Calendar).
- Sleep is sensitive health data: encrypted at rest, deletable, never written back to Google.

## Open questions

- Should the planned bedtime learn from real nights (suggest a later bedtime when you never make it)? Default: no, only show the difference.
- Naps: show them on the day bar and in the tab, or main sleep only? Default: main sleep only.
