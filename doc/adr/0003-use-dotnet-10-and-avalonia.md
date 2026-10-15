# 3. Use .NET 10 and Avalonia

Date: 2026-10-15

## Status

Accepted

## Context

The desktop app relies heavily on Windows APIs: window event hooks, per-monitor window enumeration, screen capture, the built-in Windows OCR engine, startup registration and, later, a Windows service. A Linux desktop companion is planned, and a macOS one may follow. The developer's strongest stack is Java/JavaFX, where these Windows APIs are only reachable through verbose JNA bindings and screen capture and OCR are awkward. C# is close to Java and has first-class access to Win32 and WinRT.

## Decision

We will build the desktop app in C# on .NET 10 (the current LTS release) with Avalonia as the UI framework, rather than Java/JavaFX or WPF. Avalonia was chosen over WPF because it uses similar XAML but also runs on Linux and macOS, so the UI can be reused by the Linux companion.

## Consequences

Windows integration is straightforward, and the Linux desktop app can reuse the UI and logic and only needs new platform adapters. The developer has to learn C# and Avalonia. Avalonia has fewer tutorials than WPF, and some Windows-specific window behaviour, such as a topmost click-through overlay on a chosen monitor, will need Win32 interop. Switching runtime or UI framework later would mean rewriting the app layer.
