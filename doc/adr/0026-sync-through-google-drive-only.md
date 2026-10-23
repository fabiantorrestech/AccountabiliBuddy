# 26. Sync through Google Drive only

Date: 2026-10-23

## Status

Proposed

## Context

The user's devices share progress, the day plan and settings in sealed per-device files (ADR 22), carried by QR codes, shared files, a mirrored folder or the Wi-Fi link between a paired phone and PC (ADR 14). In daily use the link is the least reliable part: it only works on a Private network at home, the phone has to find the PC again whenever its address changes, the work computer can't take part, and the watch's direct route to the PC (ADR 21) adds a second pairing. The approved redesign (section H, 2026-10-22, with the reviewer's questions 1, 2, 3, 7 and 8 settled) replaces the link with Google Drive: both apps sign in to Google and read and write the same sealed files in an app-owned folder. The user confirmed that the work computer calls the Drive API itself (one more outbound connection there, like the calendar). Seconds-level sync is not required: the poll interval is the lag, and countdowns never depend on it because they carry absolute times.

## Decision

We will make Google Drive the way the user's devices sync. Both apps sign in to Google with the `drive.file` scope (only files the app made; the desktop and Android sign-ins belong to one Google Cloud project so they see each other's files) and read and write the ADR 22 sealed files, format unchanged, in an app-owned folder. The PC reuses its Calendar sign-in (PKCE on 127.0.0.1) with the scopes united, inside the one auditable network class; the phone gets its own sign-in in one auditable class. The PC polls every 20 seconds while a session runs, else every 5 minutes; the phone every 30 seconds while a session runs or its timeline notification is pinned, else every 15 minutes, and when the watch asks. The sync files gain optional fields (format version stays 1): calendar events as the link sent them, the running session's planned end and break schedule, the phone's pickup counts during PC sessions (times and counts only), and an action list from the phone (tick a task, park a thought, tick a come-back, step away start and stop) that the PC applies and acknowledges by id. The Wi-Fi link goes: its server, QR pairing, discovery and network-trust notices, the phone's start and end of PC sessions, the link transport of phone day-plan edits (the edits themselves stay, carried by the sync files with ADR 22's clash dialog) and the watch's direct route to the PC (the watch is fed only by the phone). Paired devices see one notice: "Linking was replaced by Drive sync: sign in on each device." QR codes, shared files and the mirrored folder stay as fallbacks. This supersedes ADR 14 and amends ADR 19 (transport), ADR 21 (the watch's route) and ADR 22 (the allowed connections and fields).

## Consequences

Sync works the same at home, at work and on mobile data, and the work computer joins without a local network or a mirrored folder. In exchange, two outbound connections are added (the PC's Drive calls and the phone's Google sign-in and Drive calls), the Google Cloud project must leave "Testing" so sign-ins stop lapsing every 7 days, the phone needs a Google sign-in library (a new dependency), and the lag becomes the poll interval (about 30 seconds from PC to phone) instead of instant. Removing the link deletes a large, working part of both apps, so it is done only after Drive sync has run for a while. Reversing this means restoring the link (it stays in git history) and dropping the Drive classes; the file format doesn't change either way.
