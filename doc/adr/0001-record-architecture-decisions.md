# 1. Record architecture decisions

Date: 2026-10-15

## Status

Accepted

## Context

AccountabiliBuddy starts as a Windows-only MVP but is planned to grow into companion apps on Android and Linux, with device sync and a locally trained classifier. Several early choices (runtime, UI framework, storage, browser integration) will be expensive to change once those companions exist, and work may be shared between more than one coding agent and the developer. Without a durable record, the reasoning behind those choices will be lost.

## Decision

We will use Architecture Decision Records, as described by Michael Nygard in "Documenting Architecture Decisions", kept in `doc/adr/` and committed alongside the code that enacts each decision.

## Consequences

Future contributors and agents can see why the architecture looks the way it does, and reversing a decision requires an explicit superseding record. Writing records adds a small cost to every significant decision, and the log is only useful if that discipline is kept.
