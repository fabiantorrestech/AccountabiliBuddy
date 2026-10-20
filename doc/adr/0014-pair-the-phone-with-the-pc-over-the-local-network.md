# 14. Pair the phone with the PC over the local network

Date: 2026-10-20

## Status

Proposed

## Context

The Android companion (ADR 13) should act as a remote for the PC: show the PC's running session and today's timeline, and start or end sessions with the PC's own friction. The user chose to pair by scanning a QR code the PC shows, on the same Wi-Fi, end-to-end encrypted, with nothing going through the internet. The project's rule is local only, with every outbound connection named in an ADR (ADR 9, ADR 12), and the phone app currently declares no internet permission. Android has no separate permission for local-network sockets, so any connection to the PC needs the INTERNET permission. Both platforms ship TLS (.NET's SslStream, Android's HttpsURLConnection) and the cryptography needed for pinned certificates and random tokens, so no protocol library is required. The larger device-sync design (doc/plans/device-sync.md: event log, home server, Bluetooth fallback) is not started; this remote is its first, smaller step.

## Decision

We will let the Windows app accept connections from paired phones on the local network, off by default and turned on in its Setup tab. When pairing is started, the PC creates a self-signed certificate (kept with DPAPI), listens on one TCP port on its private-network addresses only, and shows a QR code holding its address, port, the certificate's SHA-256 fingerprint and a one-time pairing code that expires after a few minutes. The QR content is an `accountabilibuddy://pair?...` link, so the phone's own camera app can open it in the companion without the companion needing a camera or a barcode library; the same values can be typed in by hand. The phone connects over TLS, accepts only the pinned certificate, and trades the pairing code for a random device token that the PC stores per device and the phone keeps encrypted with the Android Keystore. Afterwards the phone sends the token with each request to a small JSON API: the session state and today's planned and actual focus; start a session with a profile; ask to end early, where the PC applies its wait and reason rules exactly as for its own window. The phone app gains the INTERNET permission but connects only to paired PCs, which a network security configuration and the pinned certificate enforce. Either side can unpair, and unpairing revokes the token.

## Consequences

The phone becomes a working remote without any cloud service, and the same pairing (address, pinned certificate, device token) can carry the later sync events. The PC now runs a listening port while the feature is on, which is new attack surface: it must refuse any request without a valid token, rate-limit pairing attempts, never listen on public networks, and keep ending a session as hard as it is on the PC. Windows Firewall will ask the first time. Addresses change on some networks, so the phone keeps the last address and asks for a new scan when it can't connect, until the UDP discovery from the sync plan exists. The PC needs a QR code encoder; writing one by hand is error-prone, so this likely means adding a small library, which needs the user's approval first. The phone's manifest now holds the INTERNET permission, which app stores and users read as "can reach the internet" even though the code only talks to the paired PC.
