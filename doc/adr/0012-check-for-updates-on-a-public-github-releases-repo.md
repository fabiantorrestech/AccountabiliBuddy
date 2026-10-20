# 12. Check for updates on a public GitHub releases repo

Date: 2026-10-20

## Status

Accepted

## Context

ADR 10 packages the app with Velopack and says updates come only from locally built packages, never the internet. The user now installs releases on more than one machine and wants the app to tell them about new versions, with a Check now button in Settings and an option to install updates automatically. The source code repository is private, so the app cannot read its GitHub releases without a token stored on every PC. Velopack already ships an update source for GitHub releases and an updater (Update.exe) in every install.

## Decision

We will publish installers and Velopack update packages to a separate public repository that holds only release files (fabiantorrestech/AccountabiliBuddy-releases), and the installed app will check it for newer versions with Velopack's GitHub source, without a token. It checks shortly after launch, every few hours, and on Check now in Settings. By default it only tells the user that a version is ready, through a notice and Settings, and installs when they press Install and restart; the notice mentions that automatic updates can be turned on in Settings. With that setting on, it installs by itself only when no session is running. It never installs during a session, and dev builds and test copies never check. Locally built packages keep working as before, and every other part of ADR 10 (install location, data safety, backups before migration and on a new version's first run) is unchanged.

## Consequences

New versions reach every installed PC without copying files by hand, and the source stays private. GitHub learns each PC's IP address and when it checks, and the outbound-connection rule widens to include GitHub release downloads. Whoever controls the releases repo can push code to every install, because packages are not code-signed; the repo must stay under the user's own account with two-factor sign-in. Releasing now means uploading the update packages as well as the installer, which the packaging script does. Turning automatic install on means the app may restart between sessions without asking.
