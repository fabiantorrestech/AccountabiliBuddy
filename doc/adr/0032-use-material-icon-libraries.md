# 32. Use Material icon libraries for UI icons

Date: 2026-10-23

## Status

Accepted

## Context

The user asked for context-appropriate icons on the Profiles device groups, on every Settings section and on every Today section, on both the PC and the phone (2026-10-23). Until now neither app used an icon library. The few icons there were (the PC, phone and watch outlines in Devices) are hand-written vector paths, the same path on both apps. Drawing dozens of icons by hand would be slow, inconsistent and simpler in shape than users expect. The user chose an icon library over hand-drawn paths and approved the two dependencies (2026-10-23).

## Decision

We will take icons from the Material Design icon set on both apps.

- The PC app uses `Material.Icons.Avalonia` 3.0.2, which supports Avalonia 12, through its `MaterialIcon` control.
- The phone app uses Google's `androidx.compose.material:material-icons-extended`, versioned by the Compose BOM. R8 strips the icons the release build doesn't use.

The two apps use the same icon for the same meaning (for example a moon for Sleep, headphones for the noise player, a calendar for events), so the user recognises a section on either device. The hand-written device outlines stay as they are.

## Consequences

Both libraries only draw vectors at runtime. They open no connection and change nothing in the hard rule on outbound connections. The PC install grows by the library's assembly (about 1 MB). The phone's release APK grows only by the icons actually used. Dropping either library later means replacing its icons with paths or another set; nothing else depends on it.
