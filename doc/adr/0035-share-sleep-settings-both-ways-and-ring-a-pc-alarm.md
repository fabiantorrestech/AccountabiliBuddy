# 35. Share sleep settings both ways and ring a PC alarm

Date: 2026-10-24

## Status

Accepted

Amends [34. Learn a smart bedtime on the phone](0034-learn-a-smart-bedtime-on-the-phone.md)

## Context

ADR 34 kept the smart bedtime's switches and limits on the phone, because the phone received the PC's sleep settings but never sent its own. The user wants to change them from a PC too (2026-10-24), while only the phone produces the data the feature needs, so on a PC they need a linked phone. The phone also took the PC's settings only over the Wi-Fi link, which ADR 26 is retiring, so a PC's change would stop reaching it. For the rare night without the phone, the user wants to choose a Windows PC alarm consciously instead: an alarm the PC app itself rings, whose time is the night's wake time.

## Decision

The smart bedtime's switches per kind of night (Workdays, Off-Days) and its minimum and maximum sleep move into the shared sleep settings ("sleep": `smartWorkdays`, `smartOffDays`, `minHours`, `maxHours`). The phone now sends its sleep settings as a document in its sealed sync file, with the time the user last changed them there, and applies the "sleep" document from a PC's file (not only from the link) when it is newer than its own; a PC applies the phone's the same way, taking the sleep fields it already shared plus these. The newest edit wins everywhere. The learned state (levels, nights, asked bedtimes) stays on the phone (prefs "smartBedtime").

On a PC, Settings → Sleep shows the smart bedtime's controls, enabled only while a phone is linked (a paired phone or a phone's sync file received).

Each PC chooses its wake source in Settings → Sleep (its own, never shared): the phone's alarm (ADR 23, needs a linked phone), this PC's alarm, or none (the Sleep part's wake time). With this PC's alarm, the app rings at the night's wake time as the plan works it out (the Sleep part's end, a one-night change, or bedtime plus the goal for a part woken by the alarm): the time-up sound on repeat, the on-screen window's glow and a window with Stop and Snooze 9 minutes. It sets a Windows wake timer for that time so a sleeping PC wakes to ring, and warns when the power plan doesn't allow wake timers. No connection is opened for any of this.

## Consequences

The user can change the smart bedtime from whichever device is at hand, and PC changes keep reaching the phone after the Wi-Fi link is gone. The phone's sleep settings now leave it, inside the sealed files only, like the PC's already did. The PC alarm only rings if the PC is on or can be woken: a laptop with wake timers off, a closed lid on battery or a muted PC won't wake the user, which the setting says. Profiles and day bar settings still reach the phone only over the link; that gap is left for the link's removal (ADR 26).
