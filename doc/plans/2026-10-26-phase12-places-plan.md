# Phase 12: Places, location-based focus spots (PC + phone)

Date: 2026-10-26 · Status: agreed with the user; a detailed task plan (files, steps, acceptance checks) comes before it is built

## Context
The user wants AccountabiliBuddy to recognise where a device is (the network it's on, location as a fallback) and
tighten itself at places they work. When a device sees a new place it asks what that place is. Each device learns its own
places. Nothing exists yet: there's no location or Wi-Fi code on either side. The only related code is the ADR 14 link's
`WindowsNetworkTrust`, which lists the connected Wi-Fi/Ethernet networks on physical adapters.

## Decisions (user, 2026-10-26)

| Topic | Decision |
|---|---|
| Kinds of place | **Focus** (rules on arrival) · **Sometimes** (asks "Working here today?") · **Free** (nothing added, never asked again) · **Not now** (asked on the next visit). Edit all of them in Settings → Places. |
| Focus spot rules | Work nudge from arrival (the PC 1 / `StartNudgeService` flow; Today with the task box, card after 5 min, then every 15 min) · work-hours guard on while there (ADR 28 apps), even outside its usual hours · a profile per spot, pre-selected in Start / Quick start. |
| Sometimes spot | Arrival card: **Work now** / **Work in 5 · 15 · 30 min** / **Chill**. The pre-selected choice comes from the day (a workday during work hours → Work now, otherwise Chill). "Work in N" starts the nudge after N minutes. Chill → Work applies at once at any time; Work → Chill waits 10 min and never happens during a session. The answer lasts until you leave or the day ends. |
| Spots only add | A Free spot or a Chill visit never weakens rules you already have (work nudge, work-hours guard, Pre-Work). Leaving a focus spot loosens only after the looser-change wait (10 min), never during a session, and never ends a session. |
| Losing the signal | Wi-Fi off, cable out, or location off keeps the last known place until a *different* place is seen, so switching off the network isn't an escape hatch. |
| Detection | Network first (Wi-Fi or Ethernet). Location (coarse, about 100 m) is the backup for a spot with no network of its own. A VPN never hides the place: the place comes from the physical adapter's network, and VPN/tunnel adapters are ignored. |
| Sharing | Per device. Nothing new goes into the sync files. |
| Auto-start session | Not now. Future list: an option, off by default (it would need an ADR 18 amendment). |

## Privacy and rules (ADR 44, new)
- Network ids, Wi-Fi names and coordinates are private text. On the PC they're DPAPI-encrypted (`IDataProtector`) in a new
  `documents` entry "places". On the phone they get the same protection as other private text there (check during the
  phase plan how the phone protects reasons today). They never leave the device and are never synced, backed up in the
  clear or logged.
- No new connections: Windows' and Android's own location services are the OS's. No new dependencies: WinRT
  `Windows.Devices.Geolocation` / NetworkListManager on the PC; framework `ConnectivityManager` / `LocationManager` on the
  phone, no Play Services.
- Activity history: a session can record the place's *kind* (Focus/Sometimes) but never its name or id.
- Record the rules in `doc/hard-rules.md`. The ADR covers: the new private data, the OS permissions, "spots only add",
  losing the signal, and per device.

## Design

### Core (`src/AccountabiliBuddy.Core`)
- `Domain/Place.cs`: id, the user's name, kind (Focus/Sometimes/Free), network ids, optional centre + radius, profile id.
- `Domain/PlaceVisit.cs`: the current place, the Sometimes answer (Work / WorkFrom time / Chill) and its time.
- `Ports/IPlaceSensor.cs`: an event when the set of connected physical networks changes (each with its id and display
  name, Wi-Fi or not), plus an optional coarse location. Event-driven, no polling.
- `Engines/PlaceResolver.cs` (pure, unit-testable): networks + location + known places → Known(place) / New / NoSignal.
  It applies "losing the signal keeps the last place" and the loosening rules, using `TrustedClock` and the
  pending-changes wait.

### PC (`Platform.Windows` + `App`)
- `Platform.Windows/Network/WindowsPlaceSensor.cs`:
  - Reuses `WindowsNetworkTrust`'s physical-adapter filter and NetworkListManager enumeration, using `INetwork.GetNetworkId()`
    for a stable id and `GetName()` for the name.
  - Extends the filter to ignore TAP, Wintun/WireGuard, "VPN", "Tunnel" and loopback adapters.
  - Listens to `NetworkInformation.NetworkStatusChanged`.
  - Location fallback via `Geolocator` (coarse), only when the user turns it on.
  - Pull the shared adapter filter into one helper used by both classes. Phase 4b deletes the link, so the place sensor
    must not depend on link code.
- `App/Services/PlaceService.cs`: owns the "places" document, raises the new-place card, and feeds the rest:
  - `StartNudgeService` gets "a focus visit started" (counted as arrival) and "Work from <time>".
  - `WorkHoursGuard` gets "on at this place".
  - `QuickSessionService` / Start get the spot's profile.
  - It can only make these stricter.
- UI:
  - The new-place card (Focus / Sometimes / Free / Not now, name box, profile picker).
  - The Sometimes card.
  - Settings → Places: list, edit kind/name/profile/radius, forget; locked during a session like other Settings.
  - A small "At <place>" chip on Today.
  - A SetupFeatures entry (version, a "set up?" check, an assistant page explaining the location permission).

### Phone (`android/app`)
- `places/PlaceSensor.kt`:
  - A `ConnectivityManager.registerNetworkCallback` for `TRANSPORT_WIFI` / `TRANSPORT_ETHERNET`. These callbacks still see
    the underlying Wi-Fi under a VPN.
  - The SSID/BSSID come from `NetworkCapabilities.transportInfo` with `FLAG_INCLUDE_LOCATION_INFO` and need
    `ACCESS_FINE_LOCATION`.
  - Location fallback via `LocationManager.addProximityAlert` (framework geofence).
- Permissions: `ACCESS_FINE_LOCATION`, plus `ACCESS_BACKGROUND_LOCATION` to notice places while the app is closed. If the
  background permission is denied, the phone checks only when the app opens.
- Port `PlaceResolver` to Kotlin, reusing the same rules as the PC.
- Wiring and UI:
  - Hook into `notify/WorkStartNudge.kt` and the `nudge/Takeover.kt` card.
  - The same cards and Settings → Places.
  - A phone SetupFeatures entry.

## Build order (Phase 12, after Phase 11, before Claude Design)
1. ADR 44 + the hard-rules entry (user reviews).
2. Core: `Place`, `PlaceVisit`, `IPlaceSensor`, `PlaceResolver`.
3. PC network sensor + `PlaceService` + cards + Settings → Places + the nudge and guard hooks.
4. PC location fallback (`Geolocator`).
5. Phone sensor + resolver port + cards + Settings + nudge hook + permissions.
6. Phone location fallback.

## Risks (said up front)
- Unpackaged desktop app + `Geolocator`: it needs Windows' "Let desktop apps access your location" switch. I believe that's
  enough, but I'll verify on this PC before building step 4.
- VPN clients that show up as plain Ethernet adapters (e.g. some TAP ones) could look like a new place. The filter covers
  the common names. Test with the user's VPN; fallback: a "This is my VPN, ignore it" button on the new-place card.
- The phone's background location permission is a big ask. Without it, places update only when the app opens.

## Verification
- `dotnet build AccountabiliBuddy.sln` (0 errors); `./gradlew assembleDebug` (BUILD SUCCESSFUL).
- Manual (user):
  1. Laptop on a new Wi-Fi → the card appears; choose Focus → nudge and guard on.
  2. Turn the VPN on → still the same place.
  3. Turn Wi-Fi off → the place is kept.
  4. Join a known Free network → it loosens only after 10 min, and never during a session.
  5. Sometimes spot → "Work in 15" → the nudge starts 15 min later.
  6. Phone: the same with the app closed (background permission on) and with it denied (updates when the app opens).
- Privacy check: grep the DB "places" row → only ciphertext; the sync file has no places fields.

