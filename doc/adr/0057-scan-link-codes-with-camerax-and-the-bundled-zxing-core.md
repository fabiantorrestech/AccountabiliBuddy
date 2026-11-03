# 57. Scan link codes with CameraX and the bundled ZXing core

Date: 2026-11-03

## Status

Accepted

## Context

A PC shows its link code as a QR code (`accountabilibuddy://join?c=...`, ADR 22). The phone joins by opening that link. In practice this is fragile: the owner's phone camera app (Nothing) only copies `accountabilibuddy://` links and cannot open them, so the only way left is to type 40 characters.

Constraints from the hard rules: nothing leaves the device except the connections ADR 22, 26, 43 and 46 list; the link code is a secret (it admits a device to the group and the relay); the app is about 2.5 MB after R8 and runs on phones and a watch from the same codebase. The ZXing core library is already in the app (it draws QR codes, ADR 22). A new library needs the owner's approval.

## Decision

We will scan inside the app with CameraX for the camera and the ZXing core already in the app for decoding, and add a Paste option next to it.

- **Scan** opens a camera screen (CameraX `Preview` plus `ImageAnalysis`). Each frame's luminance goes to ZXing's `QRCodeReader`, restricted to QR codes. The analyser keeps only the latest frame, runs off the main thread, and stops at the first valid result. Frames are never saved, shown anywhere but the preview, or sent anywhere. No connection is opened.
- **Paste** reads the clipboard only when the user taps it.
- Both produce a plain string and hand it to the join path that exists already: `JoinLink.parse` for a link (then the same "Join <name>'s devices?" confirmation as a link opened from outside), and the typed-code check (`SyncKeyCode.parse`) for a bare code, which only fills the code field; the user still taps Join. No new parsing rules, and no join happens without the existing confirmations.
- The `CAMERA` permission is asked when the user taps Scan, never at install or start. If it is refused, the screen says so plainly, offers the app's settings page, and Paste still works. `uses-feature android.hardware.camera` is declared `required="false"`, so a device without a camera installs and can still paste or type.
- The one new dependency is `androidx.camera` (core, camera2, lifecycle, view), approved by the owner. We will not use ML Kit or Play Services barcode scanning: they add a Google-managed model and a network-capable component that this app's rule of "outbound connections are listed" would have to cover.

Options considered:

| Option | Why not |
| --- | --- |
| System camera only (the deep link) | Does not work on the owner's phone: its camera only copies the link text. |
| Paste only | Works, but the owner chose scanning; kept as the second option. |
| ML Kit / Play Services code scanner | Larger, needs Google services, brings a component outside this app's control; ZXing core is already here. |
| `ACTION_IMAGE_CAPTURE` then decode | Saves a photo of the code on the phone, against "nothing stored". |

## Consequences

The release APK grows by CameraX's code (measured when built). CameraX ships consumer R8 rules, so the app adds no keep rules.

The camera runs only while the scan screen is open: the use case is bound to the screen's lifecycle and unbound when it closes or the app goes to the background. A QR that is not an AccountabiliBuddy code is reported ("That isn't an AccountabiliBuddy code") and scanning continues.

The link code is a secret, and anyone can show a crafted QR. The confirmation dialog stays the guard, exactly as for a link opened from a message or a web page. The code is never logged.

The camera permission is the app's first runtime permission that exists for one tap and needs no standing use; it is recorded in `doc/hard-rules.md`. The PC and the watch are unchanged.
