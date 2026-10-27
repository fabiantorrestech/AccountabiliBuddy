# Phase 11c: watch app performance — Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.

**Goal:** the Pixel Watch app feels fast. No lag opening it, scrolling pages, turning the crown or tapping; Tiles and complications update without jank; low battery use.

**Spec:** user, 2026-10-27: "the watch app is quite unoptimized and laggy". An Opus agent explores places to optimize and improve, Sonnet subagents implement the fixes, and the Opus orchestrator looks over their work.

## Steps
1. **Audit (Opus 5.5, read-only).**
   - Areas: `android/wear` plus what it uses from `android/shared`. Look at Compose recomposition (unstable params, lambdas, state reads high in the tree, `remember`/`derivedStateOf`), work on the main thread (snapshot JSON parsing, disk I/O, Data Layer listeners), list/pager use (`ScalingLazyColumn` keys, pager pre-loading), animations, the 1 s tickers, Tile and complication update frequency, wake locks, alarm scheduling, image or vector cost, and startup work (Application/Activity `onCreate`). It also looks at the release build (R8, baseline profiles).
   - Output: `.superpowers/sdd/2026-10-27-phase11c-watch-performance-plan/audit.md`, ranked by user-visible impact, each item with file:line, cause, fix, risk and size.
2. **Batches (Sonnet 5.5 implementers).** The orchestrator groups the audit's items into 2–4 tasks with no file overlap and sends them out (parallel where the files are disjoint). No new dependencies unless the user approves; a baseline profile needs `androidx.profileinstaller`, so ask first.
3. **Review (Opus orchestrator).** Each task's diff is checked against its audit items and for behaviour changes; the orchestrator merges.
4. **On the watch:** the user checks it on the Pixel Watch 3 (wireless adb), comparing before and after.

## Execution
After phase 11b; the audit runs early (read-only). It touches only `android/wear` and the watch-facing parts of `android/shared`. The shared snapshot code is also used by the phone, so the phone must build and behave the same.
