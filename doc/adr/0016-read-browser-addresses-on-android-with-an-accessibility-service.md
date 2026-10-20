# 16. Read browser addresses on Android with an accessibility service

Date: 2026-10-20

## Status

Accepted

## Context

On the phone, Usage access tells which app is in front but not which site a browser shows, so a 2FA page and a shopping site in Chrome look the same. The user wants phone sites matched against the profile's URL rules in the first linked-devices version, for awareness only (Curbox already blocks on the phone). Android offers no other way for an app to read another app's address bar: a local VPN can see domains, but it would take the phone's single VPN slot and conflict with a real VPN. Curbox already runs its own accessibility service, and Android allows several. Google Play restricts accessibility services that aren't for disabilities, and the companion is installed directly, not from the Play Store. The user also required that nothing interfere with calls, the power-menu Emergency button or Emergency SOS.

## Decision

We will add an opt-in accessibility service to the companion that only reads the address bar of known browsers (Chrome, Brave, Firefox, Edge, Samsung Internet, DuckDuckGo, Opera). Its configuration lists those packages, so Android sends it nothing from any other app (not the dialer, emergency screens or system UI). It asks for window content only, filters no keys, performs no gestures or actions, and keeps only the domain of the address, never the full URL. Domains are matched against the session profile's URL rules for awareness and recorded as the website of a usage segment (shared per ADR 15). The user turns it on in the app's Settings after an explanation, and it can be turned off there or in Android's settings at any time.

## Consequences

The phone can tell Amazon from a 2FA page during focus, using rules the user already wrote on the PC. Browser address bars change between browser versions, so the reader looks them up by view ids that may need updates, and an unrecognised browser counts as the browser app only. The app can't be published on Google Play with this service in it; it stays a direct install, and a store build would have to leave the service out. An accessibility service is a sensitive permission: the explanation must say exactly what it reads and keeps, and limiting it to browser packages keeps it away from calls and emergency features by construction.
