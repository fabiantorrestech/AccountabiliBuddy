# 13. Build the Android companion with Kotlin and Jetpack Compose

Date: 2026-10-20

## Status

Accepted

## Context

The roadmap has always planned an Android companion, and ADR 7 chose a versioned JSON profile document so that a Kotlin app could read the same profiles without sharing the C# code (ADR 4 keeps Core portable, but .NET does not run as a normal Android app). The first prototype has to show today's timeline and the sleep line, read sleep from the phone's own health store, offer a home-screen widget and a silent pinned notification with the timeline, track phone use alongside the blocker Curbox and import its lists, and later act as a remote for the PC. The user owns a Pixel on Android 15 or newer, has Android Studio, the Android SDK and an emulator installed on the development PC, and approved the standard Android stack. Profiles reach the phone, for now, by importing a backup file exported on Windows (`.abbackup`, readable JSON without secrets, or password-protected with PBKDF2-SHA256 and AES-GCM). The project's local-only rule (ADR 2, ADR 9) applies to the phone as well.

## Decision

We will build the Android companion as a native Kotlin app with Jetpack Compose and Material 3, built with Gradle and the Android Gradle Plugin, in an `android/` folder of this repository. It targets Android 16 (API 36) and runs on Android 8 (API 26) and newer. It uses only Kotlin and AndroidX libraries: Compose for the screens, kotlinx-serialization to read the ADR 7 document and backup files, Glance for the home-screen widget, Health Connect for sleep, and WorkManager for background refreshes. The timeline, schedule and sleep rules that the Windows Core already defines (DayTimeline, ScheduleBlock, TimeOff, OneOffBlock, SleepSchedule) are ported to Kotlin with the same behaviour rather than shared as code. Until the PC remote is designed in its own ADR, the app declares no internet permission.

## Consequences

The phone app gets current Android features (widgets, Health Connect, notification behaviour, accessibility services) without a cross-platform layer, it can be built and tested from the same PC with the installed SDK and emulator, and keeping it in this repository puts the schema, ADRs and handoff next to both apps. In return the timeline and schedule rules exist twice, in C# and Kotlin, and must change together; a change to the profile JSON now needs a coordinated release of both apps, as ADR 7 foresaw. The project gains a second toolchain (JDK, Gradle, Android SDK) and third-party build plugins whose versions must be kept current. Health Connect data stays on the phone, so this does not need ADR 11's Google Health API connection, but syncing with the PC will need a new ADR for its protocol and pairing.
