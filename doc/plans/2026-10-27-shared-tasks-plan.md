# Shared tasks across devices — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.

**Goal:** [ADR 48](../adr/0048-share-tasks-and-come-backs-across-devices.md), from the user on 2026-10-27:
- one next-session task list on every device;
- one Come back to list and one parked-thoughts list;
- the phone can add and edit tasks;
- edits from other devices reach a running session's checklist, through the PC's rules.

## Global Constraints
- Implementer rules: `.superpowers/sdd/implementer-rules.md`. No new dependencies; sealed format unchanged; new sync-file fields optional (older builds ignore them).
- Three tasks per session (phase 11 T6), no escape hatches, TrustedClock on the PC. Task text never leaves the user's devices.
- Item merge: a stable id, fields, `editedAt`, optional `deletedAt` (a tombstone kept 30 days) and `position`. Newest edit per item wins; a tombstone wins over an older edit. The same merge function lives in Core (C#) and shared (Kotlin), with identical results.

## Task 1: Shared lists model + merge (PC Core + shared Kotlin) and PC wiring
- **Core:** `SharedList<T>` items and `SharedListMerge`, plus sync-file fields `nextTasks`, `comebacks`, `parked`, carrying full items with tombstones.
- **PC services adopt merged lists:**
  - `SessionTasksService`: the draft / next-session list only. The running checklist stays its own.
  - `ComebacksService` and the parked store.
  - Edits stamp `editedAt`; deletes write tombstones; received lists merge on every sync accept. No UI change beyond what follows from the data.
- **Kotlin:** the shared merge plus the phone stores (replacing the read-only come-backs view).
- [ ] Commit `Shared lists: next-session tasks, come-backs and parked merge across devices`.

## Task 2: Running-session edits by action (PC)
- New sync actions `taskAdd`, `taskEdit`, `taskReorder` (plus the existing tick and park) for the running session.
- The PC applies them through `SessionTasksService` with the three-tasks rule:
  - a 4th from another device is parked, and the answer says "Parked: 3 tasks this session";
  - it is refused during the last minute of a phase, and late actions are refused.
- They are acked in `actionResults`.
- [ ] Commit `Running session: other devices add, edit and reorder tasks through the PC`.

## Task 3: Phone editor
- Today:
  - the next-session list becomes editable: add, edit, delete, reorder, a category, up to 3 with the same "park it" prompt;
  - a running PC session's tasks are editable, sent as actions;
  - Come back to and parked are editable lists.
- The watch is unchanged (read-only).
- [ ] Commit `Phone: edit tasks, come-backs and parked thoughts`.

## Review Focus
1. Merge: the same inputs give the same result on PC and phone; deletes don't resurrect; two PCs editing different items both survive.
2. The three-tasks rule can't be bypassed from another device; a running session can't be loosened by an action.
3. Task text only in sealed files, never logged.
4. An older phone (0.1.62) still reads the files.

## Execution
Task 1, then Tasks 2 and 3 in parallel. Implementers Sonnet high; reviewers Opus medium (sync).
