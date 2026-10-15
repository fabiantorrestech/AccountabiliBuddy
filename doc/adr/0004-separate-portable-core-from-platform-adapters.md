# 4. Separate portable Core from platform adapters

Date: 2026-10-15

## Status

Accepted

## Context

The MVP ships on Windows only, but Linux and Android companions are planned. The rules about what counts as a distraction, how sessions run and how escalation works are the same on every platform. Detecting windows, capturing screens, enforcing blocks and registering at startup differ completely between platforms. Restructuring after the MVP would be far more expensive than separating the two from the start.

## Decision

We will keep a `AccountabiliBuddy.Core` library that targets plain `net10.0` and has no UI or operating-system dependencies. It contains the domain model, rule evaluation, session and escalation engines, storage and a set of port interfaces (`IActivityWatcher`, `IBrowserActivitySource`, `IScreenTextReader`, `IEnforcer`, `INotifier`, `IStartupRegistrar`, `IActivityLog`, `ISyncTransport`). Each platform provides adapters in its own project, starting with `AccountabiliBuddy.Platform.Windows`.

## Consequences

Core logic can be reused by the Linux desktop app and unit-tested without Windows. New platforms only need adapters. The cost is more projects and interfaces up front, and the port interfaces must be designed without knowing every platform's limits; Wayland and Android may force changes to them. The Android companion will be written in Kotlin, so it shares data formats with Core rather than code.
