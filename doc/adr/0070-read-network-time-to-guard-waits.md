# 70. Read network time to guard waits

Date: 2026-11-06

## Status

Accepted

## Context

Every wait the app imposes (ending a session early, looser changes, the switch to ProductaBuddy in ADR 68) has to hold even if the owner changes the device's clock. On the PC, `TrustedClock` already ignores jumps in the Windows clock by following an elapsed-time counter that can't be set. It can still be nudged slowly within its drift allowance, and it can't tell which of the two clocks was right after a restart. On the phone, the ProductaBuddy wait counted on the phone's own clock, so moving the clock forward shortened it. The owner asked for a free, trusted, reliable real-time source, checked online, with the system clock as the fallback.

The app is local only: every outbound connection is listed in `doc/hard-rules.md`, goes through `CalendarNetwork`, and any new one needs an ADR. Android already keeps its own network time (`SystemClock.currentNetworkTimeClock`, Android 13 and later), fed by the system, so the phone can read trusted time without opening a connection of its own. Windows has no equivalent the app can read reliably, because the Windows Time service only sets the system clock, which the owner can change. The watch imposes no waits.

## Decision

We will read network time to anchor the clock the waits use.

**PC:**
- **Query:** `CalendarNetwork` asks for the time over SNTP (UDP port 123). It tries time.google.com first, then time.cloudflare.com, with a 3-second timeout.
- **Schedule:** at start, every 6 hours, and on resume from sleep.
- **Payload:** the request carries nothing but the standard 48-byte SNTP packet.
- **Effect:** when the answer differs from `TrustedClock` by more than its jump tolerance, the trusted time moves to the network time and its anchor is saved.
- **No answer:** `TrustedClock` keeps working as before.

**Phone:** the ProductaBuddy wait reads `SystemClock.currentNetworkTimeClock()`, and falls back to the system clock when Android has no network time yet. This opens no connection from the app.

**Watch:** unchanged.

## Consequences

The waits now hold against a moved clock wherever the device has been online since it started. That covers the PC whenever an NTP server answers, and the phone whenever Android has network time. The PC gains its first outbound connection that isn't to a service the owner set up: two public time servers, which learn only that this address asked for the time. It's listed in the hard rules and kept in `CalendarNetwork`. Offline, a moved clock is still only partly guarded: on the PC by the jump tolerance as before, and on the phone not at all. The phone's other waits (pause, day off, finished work) still read the system clock until they adopt the same source. Firewalls that block UDP 123 make the PC fall back silently.
