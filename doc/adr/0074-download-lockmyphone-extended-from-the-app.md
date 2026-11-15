# 74. Download LockMyPhone-Extended from the app

Date: 2026-11-09

## Status

Accepted

## Context

ADR 73 makes LockMyPhone-Extended (LMP) the phone's lock when it is installed. When it isn't, the built-in screen is used and LMP is recommended. The owner wants the recommendation to be one tap:
- download the latest LMP,
- get Android's install prompt,
- then be asked whether to delete the downloaded file.

The same button should offer an update when the installed LMP is too old for the hold contract (LMP ADR 0011). The page that lists the owner's apps and LMP's source repository should be linked too.

`doc/hard-rules.md` allows only listed outbound connections. AB's own updates come from `fabiantorrestech/AccountabiliBuddy-releases` (ADR 12), and any other connection needs a new ADR. LMP is published as a release asset of `fabiantorrestech/android-apps`. It is signed with the same key as AB, which the two need for their signature permission (LMP ADR 0012).

## Decision

We will allow one more outbound connection, from the phone only: a download of `https://github.com/fabiantorrestech/android-apps/releases/download/lockmyphoneextended/LockMyPhoneExtended.apk`.
- **When:** only when the user taps "Download LockMyPhone-Extended" or "Update LockMyPhone-Extended". Never in the background, never on a timer.
- **What is sent:** an anonymous GET with a generic user agent. No account, no device data, nothing from AB's own data.
- **Where it goes:** into the app's cache.
- **Before install:** AB checks that the file's package name is LMP's and that its signing certificate matches AB's own, then hands it to Android's installer through a FileProvider. This needs `REQUEST_INSTALL_PACKAGES`, a manifest permission; no library is added.
- **After install:** once Android reports the package added or replaced, AB asks whether to delete the downloaded file.

The update button shows when the installed LMP's capabilities report a contract version older than AB needs.

The links to `https://fabiantorrestech.github.io/android-apps/` and `https://github.com/fabiantorrestech/LockMyPhone-Extended` open in the browser and are not connections made by AB.

The download lives in one class (`LockMyPhoneInstaller`), so the list of the phone's connections stays easy to read.

## Consequences

Getting LMP takes a tap instead of a search, and an outdated LMP is caught before a session depends on it.

The phone gains a permission that can install apps. It is limited by Android's own per-app "Install unknown apps" switch, which the user grants on the first install, and by the package and certificate checks. GitHub sees the phone's address and the time of each tap.

The check ties AB to LMP's release key and to that file name: a renamed asset or a new key breaks the button until AB is updated. Removing this means deleting one class, the permission and the hard-rules line.
