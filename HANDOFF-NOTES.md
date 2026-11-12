# HANDOFF-NOTES: Tasks redesign (PC + shared model run)

Scope: PC + shared model only (bundle Phases 1-3 + the PC part of 7). Android UI and the Kotlin mirror of the model come in a later run; the phone keeps writing the OLD sync fields until then, so the PC must read the phone's old fields and map them into the new model on every sync.

Branch `tasks-ui-overhaul` (worktree `AccountabiliBuddy-tasks-ui`). Bundle: `doc/design-handoff-tasks/` (git-excluded, never stage). Questions: `HANDOFF-QUESTIONS.md`. Build: `"C:/Program Files/dotnet/dotnet.exe" build AccountabiliBuddy.sln` (log to `C:/Users/fblz/AppData/Local/Temp/claude/tasks-ui-logs/`, read only errors/tail).

## Phase status

- [x] Phase 0 Orientation (this file)
- [x] Phase 1 Shared model, migration, merge: **Core part done (M1)**; app wiring is M2 (below); Kotlin mirror is NOT in this run
- [ ] Phase 2 PC services (store DONE (M2.1); session cap DONE (M2.2); undo, history, tidy-up, views, search, title help, bulk DONE (M2.3); reminders engine DONE (M3); calendar export BLOCKED, see Calendar)
- [ ] Phase 3 PC UI (3.1 title bar DONE (M4.1), 3.2 layout DONE (M4.2), 3.3 rows/open task DONE (M4.3; M4 review fixes), 3.4 add/jot, 3.5 menus, 3.6 session, 3.7 reminders, 3.8 keyboard/toasts)
- [ ] Phase 7-PC Settings › Tasks, Labels, Alerts › Task reminders (+ `SettingsShellView` row-index fix)
- Out of this run: Phases 4, 5, 6, 7-Android, 8 Android half.

## M1 done (Core only, app behaves as before)

- Domain: `TaskItem`, `TaskBin`, `TaskStep`, `TaskStepLetters`, `TaskNumber`, `TaskLabel` (+ `IdFor(name)`), `TaskLabelColors` (12-colour set, rgba/#RRGGBBAA, `Opaque`), `TaskReminder` (+ `TaskReminderOccurrence`, `TaskReminderStatus`), `TaskConflict`, `TaskView` (+ `TaskViewConfig`, `TaskViewFilter`, built-ins with fixed ids), `TaskHistoryEntry` (+ `TaskHistoryKind`).
- Sync: `SyncTaskItem` (task + `TaskFieldStamps`), `SyncTaskLabel`, `SyncTaskView`, `SyncTaskLogItem`; `TaskField`, `TaskFieldStamp` (`bases`), `TaskFieldValues`, `TaskFieldMerge`, `TaskEdits` (the ONLY way stores should stamp a change); `SyncFile` optional `Tasks`/`Labels`/`Views`/`TaskLog` (lenient `SyncLenientListConverter`, cleaned in `SyncFileCodec`); `SyncLaterItem.Bin` marker (`NextBin`).
- Tasks (`Core/Tasks/`): `TaskMigration` (+ `TaskMigrationInput`, `TaskMigrationResult`, `TaskMigrationCounts`, `LegacyTaskFilter`), `TaskNumbering`, `TaskSessionMapper`, `OldTaskFieldsWriter`, `OldTaskFieldsReader` (+ `OldTaskFields`, `OldTaskReadInput`, `OldTaskReadResult`, `OldTaskList`), `TaskOrigins`.
- Docs: ADR 0076, `doc/hard-rules.md` ADR 76 bullet, questions 16-28.
- Verified by an out-of-repo check program (56 checks, all pass): migration counts/fields, idempotency, merge symmetry, conflicts, old-field round trip, numbering, codec.

## M2 task 1 done (task store wired; screens unchanged)

- [x] Task store `Services/Tasks/TaskStore.cs` (docs `tasks.items`/`labels`/`views`/`log` sealed, `tasks.viewSearches`, `tasks.categoryIds`, marker `tasks.model`=1), change events with source + "from other device".
- [x] Startup migration `Services/Tasks/TaskModelMigration.cs` (App.axaml.cs after `_taskListSync`): `BackupAsync("tasks-model")`, `TaskMigration.Run`, switch only if `IsBalanced`, marker last; counts-only log `<data>/tasks-model.log`; any exception: no marker, old stores stay. Verified on the dev DB (balanced, switched; second start takes the load path).
- [x] Old stores are views (`UseTaskStore`): `SessionTasksService` draft + categories (`SessionTasksService.TaskStore.cs`), `LaterStore`, `ComebackStore`, `AutoSavedTaskStore`. Their mutation code is unchanged; `SaveAsync` sends only changed items to `TaskStore.ApplyLocal` and reads back from `TaskFacadeLists`. Old docs no longer written.
- [x] Sync: `SyncCoordinator.TaskModelParts` (new parts + `OldTaskFieldsWriter` old fields, after `Decorate`) and `MergeTaskModel` (per file: `Tasks` -> `TaskFieldMerge`, else `OldTaskFieldsReader`); AutoSaved dropped from `TaskListSync.Decorate/ReceiveAsync` (`UsesTaskStore`); received copy strips the new parts. Numbers: `TaskNumbering` with lowest-DeviceId PC (unlinked PC = numbering).
- [x] Backups: `TaskListsExport` + `BackupAccess` carry/restore the new parts (older backups: old fields read as this PC's edits); `DeviceSettingsRegistry` classifies the new docs.
- [x] Pure logic in Core: `TaskLists`, `TaskListRules` (ApplyLocal, MergeFile/MergeLists, Normalize = settle finished Shelf tasks to History + numbers + prune 90 d/30 d, WithParts), `TaskFacadeLists`, `TaskCategoryEdits`. Check program: 114/114 PASS (M1's + M2 facades, local edits, phone and PC<->PC round trips).
- Next task (M2.2): cap by actions; the session bin as stored tasks while a session runs (today: `TaskStore.SessionBin()` view, Q32).

## M2 task 2 done (session actions, swap, focus, pause; moves keep ids)

- [x] Cap in actions: `Core/Tasks/SessionActions.cs` (+ `SessionSlot`, `SessionFocusResult`): `ActionsOf`, `Used`, `Free`, `Slots`, `Joined`, `Ticked` (next step takes the slot), `Focus`, `SwapOut`, `FittedToCap`, `WithFocus`, `Cleaned`. `SessionChecklist.FocusedSteps` + `ActionCap` (PC-local, never synced; cleared in history). `AllDone`/`CanEndAtOnce` unchanged (Q47).
- [x] `SessionTasksService`: `MaxTasks`/`TaskAllowance`/`IsAtTaskLimit` -> `ActionCap`/`ActionAllowance`/`ActionsUsed`/`IsAtActionLimit`; `NeedsPick`/`PickKeepCount` (one action per task at least); `KeepOnlyAsync` -> Queued; `AddRemoteAsync` full -> Queued; `FocusStepAsync`, `SwapOutAsync`, `Slots`, `PickSendsToQueued`; join focus in `SetAsync`; refill in `ToggleAsync`; start takes stored focus and fits the cap (Q48).
- [x] Settings: `Services/Tasks/TaskSettings.cs`, `TaskSettingsService.cs` (`tasks.settings`, device-local; Q50); `TaskStore.SessionActions` reads the setting.
- [x] Session bin decision (Q32): not stored; `tasks.running` holds the running tasks' copies (`HoldRunningAsync`, `AlsoHoldAsync`, `PutBackAsync`, `PlaceAsync`, `ReleaseRunningAsync`); `TaskListRules.Place`/`FromChecklist`/`EndOf`.
- [x] Moves keep ids (Q52): `TaskListRules.ApplyLocal` ignores move departures; Later/Comeback adds take an id; SessionTasksService moves pass it.
- [x] Pause timer (Q45), Step away unchanged (Q44); `SessionRecord` paused/stepped-away stretches; History line.
- [x] Session ended data: `SessionLayoutService` (Q54, not wired). Quiet view data = `Slots` (Q53).
- [x] `doc/hard-rules.md` ADR 76 entry extended. Check program 168/168 (54 new). Dev app started clean.
- Next (M3+): UI for focus/swap ("This session is full" = `Slots` + `SwapOutAsync` + the add), step list popover, session menu (Pause = `StepAwayService.PauseTimer`), Settings › Tasks (cap, next step takes the slot), wire `SessionLayoutService`.

## M2 task 3 done (undo, history, tidy-up, views, search, title help, bulk; services only)

- [x] Undo: `Core/Tasks/TaskUndoStack` (10), `TaskUndoEntry`, `TaskUndoRules` (Capture/Restore by touched ids), toast texts `TaskToasts`; App `TaskActionsService` (Ctrl+Z data API: `UndoAsync`, `Toast` event, `NextUndoText`). Q62.
- [x] Actions (pure, bulk-ready): `TaskActions` (Move, Reorder, Finish, Reopen, TickStep, AddStep, Delete, Archive, Rename, RenameEach, SetLabel, Add/Remove/ToggleTag, SetTimeLimit, SetHighlight, Touch). Into This session only through `TaskActionsService.MoveToSessionAsync` -> `SessionTasksService.AddToTodayAsync` (cap rules; `NeedsRoom` for the full dialog).
- [x] History: `TaskHistoryRules` (List 30 d, Remove, DoAgain, BringBack, WithDeletions, Reconciled), wired in `TaskStore.ApplyLocal` and `Normalize`. Q63, Q64, Q70.
- [x] Untouched + tidy-up: `TaskUntouched`, `TaskTidyUp` (+ card/plan/item/choice), App `TaskTidyUpService`; settings in `TaskSettings`/`TaskSettingsService` (untouched weeks, tidy-up on/day, pin highlights, default view, custom markers). Q65, Q66.
- [x] Views: `TaskViewRules` (filters, group, sort, shuffle, pin), `TaskViewEdits`, `LegacyTaskFilterMapper` (migration now uses it), `TaskViewConfig.Seed`; App `TaskViewService` (active view + working changes, save/save as/delete/default/reshuffle, `MirrorPresetsAsync`: old saved filters follow as views via `TaskFilterPresetStore.Saved`). Q67, Q69.
- [x] Search `TaskSearch` (ID 142/142b incl. former and temporary numbers, text in title/steps/note/tags); title help `TaskTitleSplitter`, `TaskProjectMarkers`, `TaskTitleHelp` (counter, Parse of @label/#tag), `TaskMarkerLearner`. Q68.
- [x] App wiring: `_taskActions`, `_taskViews`, `_taskTidyUp` fields in `App.axaml.cs` (no UI yet); one shared `TaskFilterPresetStore`.
- Check program 320/320 (152 new). Dev app started clean.
- Next (M3 UI): toast/undo bar from `TaskActionsService.Toast`, Ctrl+Z, View menu from `TaskViewService`, History dialog, tidy-up card/flow from `TaskTidyUpService`, add box from `TaskTitleHelp`, bulk bar/menus from `TaskActionsService`.

## M2 must wire (done in M2 task 1, see above; kept for reference)

1. Task store (new doc, e.g. `tasks.items` + `tasks.labels` + `tasks.views` + `tasks.history`, DPAPI-sealed like the old lists) holding `SyncTaskItem`/`SyncTaskLabel`/`SyncTaskView`/`SyncTaskLogItem`; every local change through `TaskEdits.Edit` (title/note bases depend on it).
2. Startup migration: `LocalDatabase.BackupAsync("tasks-model")`, then `TaskMigration.Run` once behind marker `tasks.model` = 1; App maps `Comeback` -> `SyncParked` (ComebackStore.ForBackup shape, local items with text), `TaskFilterPreset`/`TaskViewPreferences` -> `LegacyTaskFilter`; store `ViewSearches` in a device-local document; log `Counts` (numbers only) and refuse to switch over unless `Counts.IsBalanced`. `IsNumberingDevice` from `TaskNumbering.NumberingDevice(ReceivedDevicesService.Active + self)`; `FirstNumber` = 1 on a fresh model.
3. SyncCoordinator write: `Tasks`/`Labels`/`Views`/`TaskLog` (through `SharedListMerge.ForFile`) plus `OldTaskFieldsWriter.Write(...)` for the old fields (replaces `NextTasks`/`Parked`/`Later`/`Categories`/`Comebacks` functions **and `AutoSaved` from `TaskListSync.Decorate`**); `Later` always written (even empty).
4. SyncCoordinator read: file with `Tasks` -> `TaskFieldMerge.Merge` (+ `SharedListMerge.Merge` for labels/views/taskLog); without -> `OldTaskFieldsReader.ReadFile` (it reads `AutoSaved` too, so `TaskListSync.ReceiveAsync` must stop merging `AutoSaved` into `AutoSavedTaskStore`); then `TaskNumbering.AssignFinal` on the numbering PC. Read one file at a time: move pairing only looks inside one file.
5. Old stores (`SessionTasksService` draft, `LaterStore`, `ComebackStore`, `AutoSavedTaskStore`) become views/facades of the task store; running checklist via `TaskSessionMapper` (old category ids in a running checklist are not label ids: keep the label, see mapper).
6. History pruning: keep entries `TaskHistoryEntry.Kept` (90 d) after `When` (UI may list 30 d); see questions 22 and 31.

## Findings that shape the plan (read first)

| # | Finding | Consequence |
|---|---|---|
| 1 | Categories sync **by name + colour only** (`SyncCategory(Name, Color, ColorEditedAt)`; items carry `Category`/`CategoryColor` strings, no id). `02` §3.2 "same ids" cannot hold across devices. | Label id = deterministic GUID from lowercased name; key labels by name when reading old fields. |
| 2 | ADR 72 already shipped `Labels` (plain names, `##word`) and `LocalTo`/handovers on every item. New **tags** = old `Labels`; new **label** = old category; per-task `devices` overlaps `LocalTo`. | Map `Labels`→`tags`, `LocalTo`→`devices=[keeper]`; keep handover machinery until `devices` replaces it (Q8). |
| 3 | The draft (`_nextTasks`, doc `session.nextTasks`) is mirrored into `Current` (`session.tasks`) while no session runs; a running checklist is PC-owned and never merged. | `session` bin must equal `Current.Tasks` while live; remote actions keep going through `AddRemoteAsync`/`EditRemoteAsync`/`ReorderRemoteAsync`. |
| 4 | The 3-cap counts **tasks** today (`MaxTasks`, `TaskAllowance`, `NeedsPick`, `KeepOnlyAsync`, `PickTasksViewModel`, over-limit reasons in `session.overLimit`). Redesign caps **actions** (focused steps), setting 2-5. | Largest behavioural change; ADR 25 "All tasks done" must count actions. |
| 5 | New bin `next` (Shelf › Next) has no old home. | Dual-write puts it in old `later` and marks it with a new optional field so the reverse read does not turn Next into Later (see Data mapping). |
| 6 | Google scope is `calendar.readonly`. | Calendar export needs a broader scope: stop condition 1, skip (Q4). |
| 7 | No primary/owner PC concept exists. | Deterministic rule (Q3). |
| 8 | Next free ADR number here is 73, but the main checkout already holds 73-75. | Use 76 (Q1). |

## PC file map (`03` → code). Paths under `src/AccountabiliBuddy.App/` unless noted

| Spec | Prototype piece | Existing file(s) | Action |
|---|---|---|---|
| A | Title bar (40 DIP; now 34) | `MainWindow.axaml` (`ExtendClientAreaTitleBarHeightHint="34"`, `TitleBar` grid ~l.148) | extend |
| A | Session control (Start pill / ring+time / Away / Ended) | none in title bar today; session UI in `Views/SessionView.axaml`, `ViewModels/SessionViewModel.cs`, `Services/SessionController.cs`, `Views/QuickStartPanel.axaml` | new `Views/SessionControlView.axaml(.cs)`, `ViewModels/SessionControlViewModel.cs`; extend `MainWindow.axaml`, `MainWindowViewModel.cs` |
| A | Sync light + per-device popover + Sync now | `MainWindow.axaml` (`SyncLightButton`), `ViewModels/SyncLightViewModel.cs`, `SyncLightDeviceViewModel.cs`, `Services/SyncLightSettings*.cs` | extend |
| A | Search (Ctrl+F) | `Views/SearchBox.axaml`, `ViewModels/SearchViewModel.cs`, `MainWindow.axaml.cs` `OnKeyDown` | extend (ID search `142`/`142b`, steps) |
| A | Left rail | `MainWindow.axaml` `controls:RailButton`, `Controls/RailButton.cs` | extend (labels only) |
| B | Tasks page shell, Normal/Compact, expand/collapse | `Views/TasksView.axaml(.cs)`, `ViewModels/TasksViewModel.cs`, `TasksViewModel.Labels.cs` | extend (rewrite most of the XAML) |
| B | Today column (This session/Queued/Finished) | `Views/TodayTaskRowsView.axaml(.cs)`, `ViewModels/TodayCardState.cs`, `Services/TodaySource.cs` | extend |
| B | Shelf column, rail "Shelf · N tasks", resize handle, Ctrl+\ | none (Later/Come-back groups inside `TasksView`) | new `Views/ShelfView.axaml(.cs)`, `Views/ShelfRailView.axaml(.cs)`, `ViewModels/ShelfViewModel.cs` |
| B | Add box (pill→full, Send to, @/# suggestions, 60-char help, project markers) | `Views/TasksView.axaml` (add box), `Controls/TaskSuggestions.cs`, `Views/TaskSuggestionsView.axaml(.cs)` | extend; new `ViewModels/AddBoxViewModel.cs`, `Services/TaskTitleSplitter.cs` (rule-based, no AI) |
| B | Rows (Normal/Compact, detail line, highlight, untouched, origin, sync icon) | `ViewModels/TaskListRowViewModel.cs`, `TaskRowViewModel.cs` (session checklist row), `TaskChipViewModel.cs`, `TaskRowKey.cs`, `TaskRowKind.cs`, `TaskStatusKind.cs` | extend |
| B | Open task (chips, steps with letter chips + drag, notes, reminders line, sync line, ID chip) | `ViewModels/TaskDetailViewModel.cs`, `SubtaskRowViewModel.cs`, `TaskStepViewModel.cs`, `SubtaskLetters.cs` (letters already exist) | extend; new `Views/TaskDetailView.axaml` only if split out of `TasksView` |
| B | Tidy-up card | none | new `ViewModels/TidyUpViewModel.cs`, `Services/TaskTidyUpService.cs` |
| B | Session layout (compact auto, Shelf hidden, faded Queued) | `Views/ChecklistView.axaml` (hosted by `SessionView`, `QuickStartPanel`, `StartOfDayWindow`), `ViewModels/ChecklistViewModel.cs`, `ChecklistRowViewModel.cs`, `ChecklistMode.cs`, `ChecklistGroupViewModel.cs` | extend (Quiet/Grouped = `ChecklistMode`) |
| B | Step list popover (P3), swap dialog ("This session is full"), ended strip, hold-to-reset | `Views/PickTasksWindow.axaml`, `ViewModels/PickTasksViewModel.cs`, `PickTaskRowViewModel.cs` (3-task pick) | extend `PickTasks*` into the swap dialog; new `Views/StepListPopover.axaml`, `Views/SessionEndedStrip.axaml` |
| B2 | Today page title + sections + Reminders due today + Jot | `Views/TodayView.axaml(.cs)` (866 lines), `ViewModels/SessionViewModel.cs` (its DataContext) | extend |
| B2 | Jot it down (Ctrl+J, in-app; Enter → Shelf › To sort) | `Services/CaptureService.cs` (global Ctrl+Alt+N → come-back), `Views/CapturePopup.axaml`, `ViewModels/CaptureBoxViewModel.cs`, `Services/ComebackStore.cs` | extend; keep the global hotkey, add in-app Ctrl+J pill on Today |
| C | Row/bulk/Today-section menus | `Views/TaskMenus.cs`, `ViewModels/TaskMenuEntry.cs`, `ITaskMenuSource.cs` | extend |
| C | Reminders & due date menu (2 of 5) | none | new `Views/ReminderMenu.axaml`, `ViewModels/ReminderMenuViewModel.cs` |
| C | View menu, Create/Edit view, saved views, History | `ViewModels/TaskFilterViewModel.cs`, `TaskFilterPresetsViewModel.cs`, `TaskFilterPresetRowViewModel.cs`, `Services/TaskFilterPreset*.cs`, `TaskViewStore.cs`, `TaskViewPreferences.cs`, `TaskSortMode.cs`, `TaskDueFilter.cs` | extend; presets become synced `views` (migrate `tasks.filterPresets`); new `Views/ViewEditorDialog.axaml` |
| C | Dialogs: Changed on both devices, Snooze until…, Rename one by one, Delete N | `Views/EndSessionDialog.axaml` (pattern) | new `Views/ConflictDialog`, `Views/SnoozePickerDialog`, `Views/RenameOneByOneDialog`, `Views/DeleteTasksDialog` (+ VMs) |
| C | End session confirm | `Views/EndSessionDialog.axaml(.cs)`, `ViewModels/EndSessionDialogViewModel.cs`, `Services/SessionEndFlow.cs` | extend (keep wait + reason flow) |
| C/H | Labels in Settings (Rings on ▾, rename, delete confirm, 12 colours, rgba) | `Views/CategoryEditorView.axaml(.cs)`, `ViewModels/CategoryEditorViewModel.cs`, `TaskCategoryRowViewModel.cs`, `CategoryColorEditorViewModel.cs`, `ColorPresetViewModel.cs`, `Controls/CategoryColorButton.axaml(.cs)`, `TaskCategoryBrushes.cs`, `TaskCategoryChoiceViewModel.cs` | extend (UI copy "Label", never "category") |
| D | Toast stack, list view, Can't miss, nudge card | `Views/NudgeToast.axaml`, `ViewModels/NudgeToastViewModel.cs`, `Views/PcAlarmWindow.axaml`, `ViewModels/PcAlarmViewModel.cs`, `Services/PcAlarmService.cs`, `BedtimeReminderService.cs`, `BedtimeAlarmRinger.cs` (alarm pattern) | new `Services/TaskReminderService.cs`, `TaskReminderRinger.cs`, `Views/ReminderStackWindow.axaml`, `Views/CantMissWindow.axaml` + VMs |
| D | "Ring even when muted" (Core Audio) | none found | new `Services/MasterVolumeGuard.cs` behind a Core port (Windows impl in `Platform.Windows`) |
| E | Keyboard map, roving focus | `MainWindow.axaml.cs` `OnKeyDown` (Ctrl+F, Ctrl+R, Esc today) | extend; new `Services/TaskShortcuts.cs` |
| F | Toasts/undo (× button, timer bar, 10-undo stack) | `Views/UndoBarView.axaml`, `ViewModels/UndoBarViewModel.cs`, `Services/RecentlyDeletedStore.cs` | extend; new `Services/TaskUndoStack.cs` |
| G | Per-task sync line, conflict | `Services/Sync/ReceivedDevicesService.cs`, `SyncReceiptsService.cs`, `ViewModels/SyncReceiptsViewModel.cs`, `DeviceSyncState.cs` | extend |
| H | Settings › Tasks page | `ViewModels/SettingsSection.cs` (no Tasks value), `SettingsViewModel.cs` (rows ~l.1024), `Views/SettingsShellView.axaml(.cs)`, `SettingsView.axaml` | extend: add `SettingsSection.Tasks`; new `Views/TaskSettingsView.axaml`, `ViewModels/TaskSettingsViewModel.cs`; row-index fix in `SettingsShellView` first |
| H | Alerts & on-screen › Task reminders | `Views/SettingsView.axaml`, `OverlaySettingsView.axaml` | extend |
| Setup | SetupFeatures entry (Phase 8; PC part) | `Services/Setup/SetupFeatures.cs` | extend |
| Theme | Tokens | `Themes/AbStyles.axaml`, `Instrument.axaml`, `Paper.axaml`, `NightDesk.axaml`, `Services/Theming/AppTheme.cs` | extend (12-colour label set, radii) |

Existing code that stays and consumers to adapt when the stores change: `Services/QuickSessionService.cs`, `PlanFirstService.cs`, `ShutdownService.cs`, `StartOfDayAssistant.cs`, `ViewModels/NextUpViewModel.cs`, `ShutdownCardViewModel.cs`, `ShutdownPlanRowViewModel.cs`, `NotebookViewModel.cs`, `NotebookItemViewModel.cs`, `SessionReviewViewModel.cs`, `DayBarViewModel.cs`, `ListPickerViewModel.cs`, `PendingMoveViewModel.cs`, `RecentlyDeletedRowViewModel.cs`, `Services/Backup/TaskListsExport.cs`, `Backup/BackupAccess.cs`, `Sync/SyncActionsService.cs`, `App.axaml.cs` (wiring ~l.558, 757-763, 954).

## Core/model map (`02` §2). Core = `src/AccountabiliBuddy.Core/`; services in `src/AccountabiliBuddy.App/Services/`

| Target | Existing | What changes |
|---|---|---|
| `TaskItem` (id, number, title, bin, prevBin, position, label, tags, steps, estimate, effort, note, due, reminders, cantMiss, highlight, touchedAt, createdAt, origin, doneAt, devices, calendar, stamps) | `Core/Domain/SessionTask.cs` (Id, Title, DoneAt, Subtasks, CategoryId, Category, CategoryColor, EstimateMinutes, Note, PlannedFor, Labels, LocalTo); sync twins `Core/Sync/SyncNextTask.cs`, `SyncLaterItem.cs`, `SyncParked.cs`, `SyncAutoSavedItem.cs` | new `Core/Domain/TaskItem.cs` (+ `TaskBin`, `TaskStep`, `TaskReminder`, `TaskLabel`, `TaskView`, `TaskHistoryEntry`, one type per file) and `Core/Sync/SyncTaskItem.cs : ISharedItem`; `SessionTask` stays as the running-checklist/history shape and gets a mapper |
| Step | `Core/Domain/SessionSubtask.cs` (Id, Title, DoneAt); `Core/Sync/SyncTaskStep.cs` (phone edit request) | add `Focus`, `EstimateMinutes`; letter stays computed (`SubtaskLetters`) |
| Label | `Core/Domain/TaskCategory.cs` (Id, Name, Color, ColorEditedAt; 8 `Palette`, 24 `Presets`), `TaskCategoryDisplay.cs`, `Core/Sync/SyncCategory.cs`, `TaskCategoryColors.cs` (newer-colour-wins), doc `session.categories` (in `SessionTasksService`) | `TaskLabel` wraps/extends; add `ringOn`, `position`, rgba; palette → 12 colours (`06`); `SyncCategory` stays for old field |
| Shared-list merge | `Core/Sync/SharedListMerge.cs` (Merge, NextStamp, PositionBetween, ForFile, IsLive; 30-day tombstones, 2-min future clamp), `ISharedItem.cs` (Id, EditedAt, DeletedAt, Position, TieBreak, WithStamps, LocalTo, AsLocalStub), `SharedItemTie.cs` | reuse for `tasks`, `labels`, `views`, `history`; **item-level** today, so field-level stamps (`02` §5) need a new merge (`TaskFieldMerge`) that still uses `SharedListMerge` for tombstones/positions |
| Next-session list (draft) | `SessionTasksService` (+ `.Moves`, `.Shared`, `.Deleted` partials; docs `session.tasks`, `session.nextTasks`, `session.taskHistory`, `session.categories`, `session.overLimit`, `session.movedToLater`); `NextTasksForSync`, `MergeNextTasksAsync(incoming, knowsDetails)` | becomes a facade over the new task store; bins `session`/`queued`/`done` |
| Running checklist | `Core/Domain/SessionChecklist.cs` (SessionStart, MadeBeforeStart, Tasks, CurrentId, DroppedOpen, OutsideSession; `Shown`, `AllDone`, `CanEndAtOnce`), `ShownTask.cs`, `SessionStepEdit.cs`; `Core/Sync/SyncSession.cs` (`Tasks`) and `SyncRunning` | extend: focused steps, away/paused intervals; cap counts actions |
| Later list | `Services/LaterStore.cs` (doc `session.later`, `SyncLaterItem` with `DueOn`, `DoneAt`), `LaterDoneChange.cs` | → bin `later`; store becomes a view of the task store |
| Come-backs / parked | `Services/ComebackStore.cs` (doc `capture.comebacks`; `Comeback`, `SyncParked`; legacy `SyncComeback`) | → bin `sort`, origin "Come back to" / "Parked <date time>" |
| Auto-saved | `Services/AutoSavedTaskStore.cs` (doc `tasks.autoSaved`, 30 days; `AutoSavedTask`, `SyncAutoSavedItem`) | → bin `sort`, origin "Auto-saved Wed 11/5" (Q6) |
| Past tasks / history | `SessionTasksService` history (`session.taskHistory`, 90 d), `KeptPastStore` (`tasks.keptPast`), `ReceivedTaskHistoryStore` (`tasks.receivedHistory`), `PastSettledStore` (`tasks.pastSettled`), `Core/Sync/SyncTaskHistoryEntry.cs`, `SyncPastSettled.cs`, `PastTask.cs`, `ReceivedPastTask.cs` | keep as is (ADR 72 session records); new History (30 d, `finished`/`archived`/`deleted`) reads done/deleted tasks; Q5 |
| Deleted | `Services/RecentlyDeletedStore.cs`, `DeletedItem.cs`, `DeletedKind.cs`, `DeletedUsage*.cs` (30 d) | history `deleted` kind; Bring back |
| Local items / handover | `Core/Sync/SyncHandover.cs`, `SyncKeptPast.cs`, `Services/Sync/TaskListSync.cs` (Decorate/Receive/SetLocalAsync), `LocalItemKind.cs` | keep; map to `devices` (Q8) |
| Views (saved filters) | `Services/TaskFilterPreset*.cs`, docs `tasks.filterPresets` (local, DPAPI), `tasks.view` (local) | → synced `views` list; migrate presets |
| Task settings | `SessionTasksService.MaxTasks = 3` (const), `NotifyAllDone` (in `session.tasks`) | new document `tasks.settings` (device-local prefs) + synced bits via `SyncDocument` where `SyncFile.Documents` already carries settings |
| Sync file | `Core/Sync/SyncFile.cs` (`NextTasks`, `Parked`, `Later`, `Comebacks`, `Categories`, `TaskHistory`, `PastSettled`, `AutoSaved`, `Handovers`, `HandoverAcks`), `SyncFileBuilder.cs`, `SyncFileCodec.cs`, `SyncMerge.cs`, `Services/Sync/SyncCoordinator.cs` (writes ~l.262-280, reads ~l.472-520) | add optional `Tasks`, `Labels`, `Views`, `TaskHistory2` names (Q9); format stays 1 |

## Data mapping (migration, `02` §3)

Run once, behind `LocalDatabase.BackupAsync("tasks-model")` (Core/Storage; whole-DB copy, covers the `documents` table), idempotent via a marker document (`tasks.model` = 1). No SQLite schema bump (stays 6), no profile bump (stays v13): task lists are app documents.

| Old store (document) | Old field | → New field |
|---|---|---|
| `session.categories` | `TaskCategory.Id/Name/Color` | `TaskLabel.id` = deterministic GUID of lowercased name (Q2), name, color; `ringOn` null; position by list order |
| `session.nextTasks` (draft, `SyncNextTask`) | `Id` | `id` |
| | `Title` | `title` |
| | `DoneAt` | `doneAt`; bin `done` if done today else history `finished` |
| | `Category`+`CategoryColor` (names) | `label` (by name), colour kept as label colour if label missing |
| | `Subtasks` (`SessionSubtask`) | `steps` (`focus` = first open steps filling free slots when bin `session`) |
| | `EstimateMinutes`, `Note` | `estimateMinutes`, `note` |
| | `PlannedFor` (`PlannedSlot`) | no target field: keep in a side field `plannedFor` (optional) so "Put in plan" survives; Q7 |
| | `Labels` (ADR 72) | `tags` |
| | `LocalTo` | `devices=[keeper]` (Q8) |
| | `EditedAt`/`DeletedAt`/`Position` | stamps/`position`; first 3 actions → `session`, rest `queued`, in order |
| `session.later` (`SyncLaterItem`) | all above + `DueOn` | bin `later`; `dueOn`; `DoneAt` set → history `finished` |
| `capture.comebacks` (`Comeback`/`SyncParked`) | `Text`, `CreatedAt`, `DoneAt`, `Category*`, `Labels`, `LocalTo` | title, `createdAt`, bin `sort`, origin "Come back to"/"Parked <date time>"; ticked ones → history `finished` |
| `tasks.autoSaved` | `Title`, `WrapUpOf`, `SavedAt`, `Labels` | bin `sort`, origin "Auto-saved <weekday m/d>" |
| `session.tasks` (running checklist) | `SessionTask` | unchanged while live; on next start the draft path applies |
| `session.taskHistory`, `tasks.keptPast`, `tasks.receivedHistory`, `tasks.pastSettled` | all | untouched |
| `tasks.filterPresets` / `tasks.view` | presets | `views` (name, cfg); `tasks.view` → default view `cfg` |
| all | `number` | assigned in `createdAt` order from the numbering device (Q3); `touchedAt`=`EditedAt`; `tags`=[] (or old `Labels`); `highlight`=false; `origin` per row above; `calendar`=setting default |

**Reverse mapping (dual-write for one release, and read of the phone's old fields).** Old fields are the phone's only channel until the Android run. Where read/written: write `Services/Sync/SyncCoordinator.cs` ~l.262-280 (functions `NextTasks`, `Parked`, `Later`, `Categories`, `Comebacks` wired at `App.axaml.cs` ~l.757-763, 954); read `SyncCoordinator.cs` ~l.472-520 (`MergeNextTasks`, `MergeParked`, `MergeLater`, categories via `SessionTasksService.AdoptCategoryAsync`/`OtherCategories`); ADR 72 parts via `TaskListSync.Decorate/ReceiveAsync`.

| Sync-file field (C# type, `Core/Sync/SyncFile.cs`) | Writer today | New-model projection (write) | Phone edit → new model (read) |
|---|---|---|---|
| `NextTasks` `IReadOnlyList<SyncNextTask>?` | `NextTasksForSync` | bins `session`+`queued`+`done`(today) ordered by position; `Category`/`CategoryColor` from label (opaque `#RRGGBB`, rgba dropped), `Labels` from tags, steps from `steps` | item-level newest `EditedAt` wins per field-group; unchanged fields inherit PC copy (ADR 50 `knowsDetails` pattern, extended: writer lacks `Tasks` ⇒ keep PC's reminders/highlight/due/number/devices) |
| `Later` `IReadOnlyList<SyncLaterItem>?` (always written, even empty, so phones see `knowsDetails`) | `LaterStore.ForSync` | bins `later` **and `next`** (new optional `Bin` marker on the item, Q10), `DueOn`, `DoneAt` from history `finished` of last 90 d | `later` item whose task is in `next`/`later` keeps its bin unless item `EditedAt` > bin stamp and no marker; moved lists = move bin |
| `Parked` `IReadOnlyList<SyncParked>?` (non-null = "has shared lists") | `ComebackStore.ForSync` | bin `sort` tasks | new item → bin `sort`; tick → `done` |
| `Comebacks` `IReadOnlyList<SyncComeback>?` (legacy, open only) | `SyncCoordinator` ~l.262 | open `sort` tasks | ignored when `Parked` present |
| `Categories` `IReadOnlyList<SyncCategory>?` (name+colour) | `syncCoordinator.Categories` (`App.axaml.cs` 954) | labels (name, opaque colour) | via `AdoptCategoryAsync(name,color)`; `TaskCategoryColors.TakeNewer` |
| `Actions` `IReadOnlyList<SyncAction>?` (`taskAdd`/`taskEdit`/`taskReorder`/`tickTask`/`park`/`tickComeback`, `SyncTaskRequest`) | phone | n/a | handled by `SessionTasksService.AddRemoteAsync/EditRemoteAsync/ReorderRemoteAsync` + `SyncActionsService`; cap enforced here (actions, not tasks) |
| `Running`/`SyncSession.Tasks` `IReadOnlyList<SessionTask>?` | `TasksOf` | focused steps as before | read-only on phone |
| `AutoSaved` `IReadOnlyList<SyncAutoSavedItem>?` | today `TaskListSync.Decorate` from `AutoSavedTaskStore`; **from M2 `OldTaskFieldsWriter` only** (drop it from `Decorate`, or the phone sees items twice) | open `sort` tasks with `AutoSavedFor` within 30 d of `CreatedAt` (they are then in no other old list; after 30 d they move to `parked`) | `OldTaskFieldsReader`: edits and tombstones by id; delete + new Later id = a move (paired) |
| `TaskHistory` `IReadOnlyList<SyncTaskHistoryEntry>?`, `PastSettled`, `Handovers`, `HandoverAcks` | `TaskListSync.Decorate` | unchanged | unchanged |
| **new, optional** `Tasks`, `Labels`, `Views` | PC (new builds) | full model, field stamps | new builds read these first, fall back to old fields |

Rules for the dual path: (1) old fields are always rebuilt from the task store on write, never stored separately; (2) on read, a file with `Tasks` is authoritative for that device; a file without it is mapped item by item with field-level "unchanged by old writer" inheritance; (3) tombstones: old `DeletedAt` → task tombstone; (4) never delete a new-model field because an old item lacks it.

## Numbering device

None exists. `Core/Sync/SyncDeviceCard.cs` (DeviceId, Name, Kind `pc`/`phone`/`watch`, AppVersion, WrittenAt, Methods, HistoryOff, Seen, Companions, NextAlarm, PublicKey) and `Services/Sync/ReceivedDevicesService.cs` have no primary/owner flag; ADR 61 is "What syncs", not ownership. Use `02` §4's rule: the PC with the lowest `Device.DeviceId` among PCs seen in the group (`ReceivedDevicesService.Active` + self), a phone-only group numbers on the phone (the PC run: temporary numbers only). Q3.

## Calendar

`Services/GoogleCalendarClient.cs` `Scope = "https://www.googleapis.com/auth/calendar.readonly"` (used by `CalendarSyncService.cs`, the user's own OAuth client; scopes listed in `GoogleSetupSteps.cs`): **read-only, cannot create calendars or events**. Export needs `calendar.events`/`calendar` (broader scope, new consent): stop condition 1, so do not build the Google export or the "AB Reminders" calendar; keep the setting hidden or disabled. Windows Calendar (WinRT appointments) is not referenced anywhere in the code today.

## Android file map (for the next run). Paths under `android/app/src/main/java/com/accountabilibuddy/companion/` and `android/shared/src/main/java/com/accountabilibuddy/shared/sync/`

| File(s) | Action |
|---|---|
| `ui/TasksScreen.kt` | replace |
| `ui/tasks/TasksModel.kt`, `TasksController.kt`, `TasksPane.kt`, `TasksPaneActions.kt`, `TasksCard.kt`, `TasksPc.kt` | replace internals (keep `TasksDemo.kt` working) |
| `ui/tasks/TaskRow.kt`, `TaskRows.kt`, `TaskRowModel.kt`, `TaskKind.kt`, `TaskRef.kt`, `TaskText.kt` | replace |
| `ui/tasks/TaskDetail.kt`, `TaskDetailSheet.kt`, `TaskDetailActions.kt` | replace |
| `ui/tasks/TaskCategoryMenu.kt`, `TaskCategoriesSheet.kt`, `CategoryColorSheet.kt`, `TaskLabels.kt` | replace (label + tags sheets) |
| `ui/tasks/TaskMenus.kt`, `TaskMenuItem.kt`, `DueDateMenu.kt`, `EstimateChips.kt`, `SheetField.kt` | replace |
| `ui/tasks/AddField.kt`, `TaskPickerSheet.kt` | replace (Add a task, From your lists) |
| `ui/tasks/LaterGroup.kt`, `NextTasksCard.kt`, `ComebacksCard.kt` | replace (bins/Shelf) |
| `ui/tasks/TaskFilter.kt`, `SavedFiltersSheet.kt` | replace (Filter sheet A, views) |
| `ui/CategoriesEditor.kt`, `ui/SettingsCategories.kt`, `ui/settings/*` (`AlertsPage.kt`, `SettingsPageList.kt`, `PageRow.kt`, ...) | replace/extend (Labels, Settings › Tasks, Alerts › Task reminders) |
| `notify/*` (`BedtimeAlarmActivity.kt`, `BedtimeReminder*.kt`, `Channels.kt`, `ReminderRelay.kt`, `ParkedNotice.kt`, `Refresher.kt`) | extend; new reminder alarm activity modelled on `BedtimeAlarmActivity` |
| `ui/SyncLight.kt`, `SyncLightModel.kt`, `SyncLightPopover.kt`, `SyncLightSetting.kt` | extend (Sync card, conflict sheet) |
| shared: `SyncNextTask.kt`, `SyncLaterItem.kt`, `SyncParked.kt`, `SyncComeback.kt`, `SessionTaskItem.kt`, `SharedListMerge.kt`, `SyncFile.kt`, `SyncFileCodec.kt`, `SyncFileJson.kt`, `TaskCategoryPresets.kt` | keep (old fields) + add `TaskItem` mirror and field merge |
| `essentials/*` | do not touch (lmp-lock branch work) |

## Open risks for M1 (also see questions)

- Lossless migration: back up first; verify counts per old store vs new bins; keep old documents (`session.nextTasks`, `session.later`, `capture.comebacks`, `tasks.autoSaved`) in place for one release as the rollback source.
- Dual path correctness is the data-loss risk: phone edits an old item whose task has reminders/steps focus/highlight that the old type cannot carry.
- Cap by actions touches `SessionTasksService` (1.6k lines + 3 partials), `PickTasksViewModel`, `AddRemoteAsync`, `NeedsPick`, ADR 25 end-at-once, over-limit reasons.
- Hard-rules/ADR updates required in M1: `doc/hard-rules.md` (new fields, what each may carry), ADR 76.

## M1-M2 review cleanup (commit "Tasks (PC): M1-M2 review cleanup")

- Migration: a live duplicate's losing title/note is kept as a `TaskConflict` on the winner; ADR 76 notes the Next/Later creation-time and single-stamp limits.
- Numbering: a kept temporary number is skipped when another task's FormerNumbers holds it.
- Migration backup is written once per model version (`tasks.modelAttempt`, a state key); a failed migration shows a notice at start.
- Backup restore of `tasks.categoryIds` / `tasks.viewSearches` goes through `TaskStore.RestoreDeviceMapAsync`.
- Undo restores only the fields the action changed (`TaskUndoEntry.Fields`); tidy-up re-choice restores only the Bin field. Undoing a Bring back re-adds its Recently deleted entries (`RecentlyDeletedStore.ReinstateAsync`).
- Old-list rules use the running session's `ActionCap`.
- Q52: `TaskListRules.StaleDeletes` + `TaskStore.Notice` event + `TaskToasts.MovedElsewhere`; no UI yet (a later milestone shows it).
- Item 9 checked, no change: a ticked Later item/come-back files Finished; deleting it afterwards removes it from History by design (`CloseFinished`).

## M1-M2 milestone review fixes (commit "Tasks (PC): M1-M2 milestone review fixes")

- QR files (`SyncFileBuilder.ForQr`) drop `Tasks`, `Labels`, `Views`, `TaskLog`.
- `TaskViewService.MirrorPresetsAsync` runs each mirror on the UI thread after the previous one (`Dispatcher.UIThread.InvokeAsync`). Audit: the other TaskStore writers already run there (sync merge via `SyncCoordinator.AcceptSealedAsync` → `UIThread.InvokeAsync`, remote actions via `UIThread.Post(syncActions.ApplyAsync)`, backup import from the VM, the lists and VMs themselves).
- Undo skips what changed since (Q75).
- `SessionEngine.PauseTimer` refused outside focus and planning; step away in a break never freezes (Q74, ADR 77).
- `SessionTasksService.SwapOutAsync`: the checklist without the task is worked out after `PutBackAsync` (`MoveOutOfChecklistAsync` takes a function; nothing is written when the task already left).
- `TaskModelMigration`: a marker that can't be read → `Unreadable` (never the old lists on a guess).
- `TaskStore.SessionBin()` and `RunningChecklist` removed (no callers); `TaskStore.IsHeld` added; `SaveRunningAsync` goes through the `_saving` chain.
- Unreadable task list: `ComebackStore.AddAsync` / `AutoSavedTaskStore.AddAsync` refuse and raise `AddRefused`; the app shows a notice (Ctrl+Alt+N, the work gate's come-back, the shutdown card, every other add).
- `taskStore.SessionActions` is the setting again (the list's This session bin is always the next session's draft); the running checklist keeps its own `ActionCap` (replaces "Old-list rules use the running session's ActionCap" above).
- `tasks.tidyUp` classified as a state key.
- Re-check fixes: undoing a Bring back reinstates its Recently deleted entries after the restore, only for tasks not skipped (`TaskUndoRules.ToReinstate`). A task ticked done (another device) while `MoveOutOfChecklistAsync` puts it back stays done in the checklist (no drop) and leaves the list again (`TaskStore.TakeBackAsync`: held again, tombstoned, no History entry).

## M3 done (reminders engine; UI is M6/M7)

- Core (`src/AccountabiliBuddy.Core/`): `Tasks/TaskReminderTimes` (rule -> instant, DST), `TaskReminderEntry`, `TaskReminderQueue` (entries, due queue, next ring), `TaskReminderActions` (snooze/dismiss/start written as stamped `Reminders` edits), `TaskSnooze` + `TaskSnoozeShortcut` + `SnoozeDay` (minutes, 3 shortcuts, absolute, picker summary, 12-hour text), `TaskReminderRingPolicy` (rising volume, dismiss wait, hold, stop-after, repeat), `TaskReminderText`, `TaskReminderDisplay`, `MasterVolumePlan`; `Ports/IMasterVolume` + `MasterVolumeState`.
- Platform.Windows: `Audio/WindowsMasterVolume` + `CoreAudioInterop` (hand-written COM, no package).
- App (`Services/Tasks/`): `TaskReminderSettings(Service)` (doc `tasks.reminderSettings`: volume 70, rising on, ring even when muted off, dismiss protection 5 s, stop after 10 min, display Toast, snooze shortcuts, new reminders start at "At start time"), `TaskReminderService` (timer off the trusted clock, refresh on store/settings change and wake, `Pending`, `SnoozeAsync/DismissAsync/StartAsync`), `TaskReminderRinger` (window + bell + volume guard), `MasterVolumeGuard` (saved restore, crash recovery). `ViewModels/TaskReminderRingViewModel`, `Views/TaskReminderWindow` are the stand-in UI. Wired in `App.axaml.cs` (TaskList branch; wake refresh; dispose).
- M6 hooks: `TaskReminderService.Pending/Entries/SessionFull`, `TaskSnooze.*`, `TaskReminderRingPolicy`; settings UI (M7) edits `TaskReminderSettingsService.UpdateAsync`. Decisions: HANDOFF-QUESTIONS 76-84.
- Checks: `tasks-ui-checks` Program.cs (M3 block at the end; 417/417 pass). Real run: a seeded due task showed the window, Dismiss waited out protection, Dismiss wrote `Dismissed` to the task and closed it. Core Audio read/raise+mute/restore verified on this PC.

## M3 task 2 done (side-task timer setting; services/engine only, UI is M5/M7)

- Owner's decision (2026-11-11): "When I start a side task" = Keep timer running (default) / Pause timer, per mode in `tasks.settings`(`.pb`) (`TaskSettings.SideTask`, `PendingSideTask`, `PendingSideTaskFrom`). Same rules as "When I step away": `TaskSettingsService.SetSideTaskAsync` (looser waits, refused during a session; stricter at once), `SideTaskNow` (stricter of `SessionState.SideTaskAtStart` and now), restore via `RestoreAsync`, not in the compare view.
- Engine: `SessionEngine.StartSideTask(length, keepTimerRunning)` sets `AwayTimerRuns`; in a break it never freezes (Pause -> `AwayPausesInFocus`, frozen from the focus's start by `FrozenIfAwayAtFocus`). `RememberSideTask` at every session start (`SessionController.SideTaskSetting`). `StepAwayService.SideTaskSetting` (wired to `SideTaskNow`) picks the mode; `CheckNow` checks a side task's end first so its card/chime still come with the timer running.
- M7 settings UI: add a "When I start a side task" row next to "When I step away" calling `SetSideTaskAsync` (show the pending wait like the step-away row). Not added to SetupFeatures (no UI yet).
- Docs: ADR 0077 Side tasks bullet, `doc/hard-rules.md` side-task sentences, HANDOFF-QUESTIONS 85-87 (Q74's flag closed).
- Checks: `tasks-ui-checks` Program.cs (M3.2 block; 454/454 pass) + new `tasks-ui-checks/AppChecks` (App-level `TaskSettingsService`/`SessionController` on a temp DB; 20/20 pass). Dev app started once on dev data: no exceptions.

## M3 task 3 done (task reminders to Google Calendar, ADR 78; services only, UI is M5/M7)

- Core (`Core/Tasks/`): `TaskCalendarMapping` (task -> event: title and times only; due time = 15-min event, whole-day due = all-day, reminders without due = 15-min anchor at the last reminder; overrides 0-40320 min, max 5; fixed event id `ab`+task hex; `Wanted` with the picked calendar for due-dated tasks), `TaskCalendarDiff.Plan` (deletes, updates, creates; only recorded events), `TaskCalendarOptIn.WithDefault` (first reminder/due takes "Add new reminders to"), records `TaskCalendarEvent/Wanted/Record/Action(Kind)`, `TaskCalendarDestination`.
- App: `GoogleCalendarWriter` (find/make "AB Reminders", insert/replace/delete by id; 409 -> replace, 404 on replace -> insert, 410 delete fine), `GoogleCalendarClient.AppCreatedScope/EventsScope`, `SignInAsync(includeGrantedScopes)`, `RefreshWithScopesAsync` -> `GoogleAccessToken`, `GoogleTokens.Scopes`; `Services/Tasks/TaskCalendarExportService` (doc `tasks.calendarExport`, log `tasks-calendar.log` counts only, 2 s debounce, backoff 30 s -> 30 min, `NeedsConsent`/`Problem`/`StatusChanged`, `AllowExportAsync`, `AllowPickedCalendarAsync`, `SetPickedCalendarAsync`, `SyncNowAsync`); `TaskReminderSettings.NewRemindersTo`/`PickedCalendarId`; `CalendarSyncService.SkipCalendar` hides AB Reminders from reading. Wired in `App.axaml.cs` after the reminder service.
- M5 (reminders menu): call `TaskCalendarOptIn.WithDefault(before, after, settings.NewRemindersTo)` on each edit; the "Add to Google Calendar" toggle sets `TaskItem.Calendar`.
- M7 (settings): "Add new reminders to" row (`TaskReminderSettingsService.UpdateAsync`), Allow button (`AllowExportAsync`, show `Problem`/`NeedsConsent`), experimental picked calendar (pick from the export account's calendars, `AllowPickedCalendarAsync` then `SetPickedCalendarAsync`; off = `SetPickedCalendarAsync(null)`). Add a SetupFeatures entry then (re-consent for existing installs).
- Docs: ADR 0078 (as-built section), CLAUDE.md outbound line, `doc/hard-rules.md` ADR 78 entry, HANDOFF-QUESTIONS 4 and 83, setup guide scopes (`GoogleSetupSteps`), Integrations and Settings hint texts.
- Checks: `tasks-ui-checks/CalendarExportChecks` (mapping, JSON, diff, opt-in, service on a fake Google handler: consent gate, create/update/delete, offline, 409, calendar re-made, picked calendar on/off; 52/52 pass). Dev app started once on dev data: no exceptions.

## M3 task 3 fix round (review of 1cbba319..bef5b000 + owner's item 9)

- Shared event ids: `TaskCalendarActionKind.Forget` + `TaskCalendarMapping.KeptElsewhere(Ids)`; `TaskCalendarDiff.Plan(wanted, recorded, keptElsewhere)` forgets (no DELETE) a task that still wants its event but isn't kept on this PC.
- App calendar identity: `GoogleCalendarWriter.FindOwnedCalendarsAsync` lists candidates, `ClaimCalendarAsync` (PATCH description) confirms one this client made; new calendars get the description too. 403/404 in AB Reminders -> forget and remake, once at once, then once per backoff (`_appCalendarTrouble`).
- `GoogleErrorReason` (Google's `error.errors[0].reason`, put on the exception by `CalendarNetwork`): rate limits back off; `insufficientPermissions` -> NeedsConsent (picked calendar: its actions wait, records kept).
- `AllowAsync` re-reads the source after sign-in (disconnected meanwhile -> refused); `IsAppCalendar` by recorded id only.
- Item 9: `TaskDateOnlyEvent` + `TaskReminderSettings.DateOnlyEvent` ("Tasks with a date but no time": Timed at 9:00 AM default | All-day event); M7 adds the row next to "Add new reminders to".
- Docs: ADR 78, hard-rules (headline + ADR 78 entry), Q83. Checks: `CalendarExportChecks` 80/80.

## M3 task 4 done (per-account task settings in sync; data API only, the chooser UI is M7)

- Core: `Sync/SyncTaskAccountSettings` (`defaultViewId`, `pickedCalendarId`, a `TaskFieldStamp` each; `AdoptedAt` = Unix epoch for taken-over local values; `EditedAt` ignores those), `Sync/TaskAccountSettingsMerge` (field-level, newest stamp then device then value, 2-min clamp; `Clean` for files/backups), `Sync/SyncLenientObjectConverter<T>`, `Tasks/TaskAccountSettingsEdits` (`WithDefaultView`, `WithPickedCalendar`, `WithBoth`, `Adopted`, `Restored`). `SyncFile.TaskAccountSettings` (optional, lenient, cleaned in the codec, null in `ForQr` and in the received copy); `TaskLists.Account` carried by every `TaskListRules` rule; `MergeFile` merges it whenever a file carries it; `WithParts` writes it once something was chosen.
- App: `TaskStore.AccountKey` "tasks.account" (sealed, read leniently; registry `TaskKeys`); `TaskAccountSettingsService` (`DefaultViewId`, `PickedCalendarId`, `PickedCalendarBy`, `Changed`, `SetDefaultViewAsync`, `SetPickedCalendarAsync`, `AdoptLegacyAsync`, `ImportAsync`; chooser: `AccountCopy()`, static `FromBackup(BackupContent)`, static `Fresh`, `ApplyAsync(choice)`, `ResetAsync()`; `TaskAccountSettingsChoice` + `TaskAccountSettingsOrigin`). `TaskViewService` and `TaskCalendarExportService` read the account's values; `TaskSettingsService.SetDefaultViewAsync` removed (`LegacyDefaultViewBlocking`/`ClearLegacyDefaultViewAsync`), `TaskReminderSettingsService.ClearLegacyPickedCalendarAsync`; `App.AdoptLegacyTaskSettingsAsync` runs before the export starts. `TaskListsExport.AccountSettings` + `BackupAccess.Tasks(..., account)`; an older device backup's default view goes to `AdoptLegacyAsync`.
- Export without `calendar.events`: pick made on another device (stamp `By`) -> due-dated tasks exported nowhere, own AB Reminders events for them deleted, `Problem` says why (no NeedsConsent), logged once with the count; pick made here -> as before (wait, NeedsConsent).
- M7: setup assistant/Settings chooser: `backups.Read(text, password)` -> `TaskAccountSettingsService.FromBackup`, `AccountCopy()`, `Fresh`; apply with `ApplyAsync`. Settings › Tasks default view -> `TaskViewService.SetDefaultAsync`. Picked calendar row as in M3.3 (`AllowPickedCalendarAsync` then `SetPickedCalendarAsync`). Phone (Kotlin) mirror of `taskAccountSettings` is out of this run (it ignores the part).
- Docs: hard-rules (ADR 76 entry: the fifth part; ADR 78 entry), ADR 0076 note, ADR 0078 amendment, HANDOFF-QUESTIONS 66 and 83.
- Checks: new `tasks-ui-checks/AccountSettingsChecks` (47/47), `CalendarExportChecks` +9 (89/89; two old lines now set the pick through the account), Program.cs 454/454, AppChecks 20/20.

## M3 task 3 fix round 2 (+ per-account pick safety)

- `TaskCalendarMapping.KeptElsewhere(task, self, zone, dateOnly)` needs `EventFor != null`; otherwise Delete.
- `GoogleCalendarWriter.ClaimCalendarAsync`: 403 insufficientPermissions = not ours. `EventExistsAsync` (GET by id) before deleting an AB Reminders copy for a pick made elsewhere.
- `GoogleCalendarEntry.Description`; `IsAppCalendar` falls back to name + `AppCalendarDescription` when this PC has no record.
- Pick safety: `TaskAccountSettingsService.PickedCalendarChosen` (adopted stamp is not "elsewhere"); `TaskCalendarExportService.SharesTaskLists` (set from `SyncSettings.ShareTaskLists`; off = another device's pick ignored). `TaskAccountSettingsEdits.IsChosen`: same value over an adopted stamp is restamped.
- `DeviceSettingsRegistry`: `tasks.reminderSettings` device (restorer in App goes through `TaskReminderSettingsService`, legacy pick to the account); `tasks.reminderMissed`, `tasks.volumeRestore`, `tasks.calendarExport` state.
- Checks: CalendarExportChecks 101/101, AccountSettingsChecks 47/47, AppChecks 20/20, Program 454/454.

## M3 milestone review fixes (Tier 2, 6ccfe2a3..466ce416)

- Reading: `CalendarSyncService` drops events with `TaskCalendarMapping.IsTaskEventId` (`ab` + 32 hex) in every Google calendar.
- Confirmations of another device's pick: per-task wait 10 min doubling to 6 h (`_confirmWaits`); the scheduled retry waits for the earliest one (budget kept).
- Ringer: `FollowVolume` raises only while the front reminder rings out loud (no raise for a quiet in-session one; put back when it turns quiet).
- `App`: `MasterVolumeGuard.RestoreLeftoverBlocking` runs on every start, before the task-model branch.
- Export: missing client secret / unreadable sign-in shows its cause, no backoff retry; a 403 in AB Reminders keeps the records and re-claims it next run (only a refused claim starts over, then replans).
- ADR 78: reading-side sentence; Consequences line on "Sync task lists" off. Item 4 (Can't miss hold) is M6.
- Checks: CalendarExportChecks 112/112, AccountSettingsChecks 47/47, AppChecks 20/20, Program 458/458. The ringer's volume rule has no check (it needs the reminder window).

## M4 task 1 done (title-bar session control + sync light; rail sizes)

- [x] Title bar 40 DIP (`ExtendClientAreaTitleBarHeightHint`, row, caption and title buttons), brand 12 muted at 16; right cluster: session control, sync light, sync button, search, window buttons. Search flyout moved to 44.
- [x] Session control: new `Views/SessionControlView.axaml(.cs)`, `ViewModels/SessionControlViewModel.cs`, `SessionControlState.cs`; `MainWindowViewModel.SessionControl`. Idle "Start session" + Ctrl+Shift+S chip -> `QuickSessionService.Show()` (Q89; Ctrl+Shift+S in `MainWindow.OnKeyDown`). Running: ring (`ProgressRing`, share left; dashed `Ellipse` when counting up; Sleep colour in a break; faded + pause sign while paused) + "32 min left"; hover/keyboard focus opens [Step away | ⌄] + "ends 1:23 AM" (MaxWidth/opacity transition, Motion-gated). Menu: Step away/I'm back/Resume, Pause timer, Side task…, Add 10 minutes, End session… (Q91). Away: "Away 6 min" + I'm back, kept open (Q92). Ended: target (fading) "Ended · switches next time", click = Switch now (Q90).
- [x] Services used: `StepAwayService.StepAway/Return/PauseTimer/AskSideTaskLength`, `SessionController.ExtendFocus`, `SessionEndFlow.TryEndSessionAsync` (wait + reason; nothing around it), `SessionLayoutService` now created in `App` (`_sessionLayout`), `TasksOpened()` on a switch to the Tasks tab; user ends switch back at once (`OnSessionEnded`, Q90). Times from the session's trusted clock, 12-hour.
- [x] Sync light: tip "Synced · Pixel 8, Home desktop" / "Behind · …" / "Sync problem · …" (`SyncLightViewModel.TipText`); popover rows get a green check / red cloud sign (`SyncLightDeviceViewModel.IsSynced/IsFailing/FailingIcon`) (Q94). All existing behaviour kept.
- [x] Rail: 72 column, 60-wide items, 44 × 30 pill, icon 20, label 11.5 (Q95). No pages added or removed.
- [x] `doc/hard-rules.md`: the title-bar control is listed with the step-away surfaces (End = the same end-early flow). Questions 89-97.
- Left for later: "End and mark the rest unfinished" (3.6, with the session-task end rule); the "Session ended at 12:52 AM" strip, `NoticeSeen`/`DismissNotice` and the Tasks session layout (3.6, `SessionLayoutService.InSessionLayout`); 3-themes × light/dark visual pass of the running/away/ended states (built from tokens only; idle checked in the dev app, Instrument dark); the conflict/sync line in the open task (3.3/G).

## M4 task 2 done (Tasks page layout, Shelf, sections; Today page sections)

- [x] Layout prefs: `Services/Tasks/TaskLayoutPreferences.cs`, `TaskLayoutStore.cs` (doc `tasks.layout`, device-local, both modes, in `DeviceSettingsRegistry.DeviceKeys`; created in `App` before the main window VM). Q98.
- [x] Sections: `ViewModels/TaskSectionViewModel.cs` (title, hint, bell icon or label dot, quiet fade .66/.40, pin, "+" with an inline add box, "Nothing to see here.", drag `MoveRow`), `TaskSectionsPanelViewModel.cs` (panels "today", "todaytab", "shelf"; folds, Expand / Collapse all, Pinned open menu on header right-click). Views `Views/TaskSectionView.axaml(.cs)` (header, add box, rows, drag within the section) and `Views/TaskRowView.axaml(.cs)` (the old row template moved out of `TasksView`, Normal 44+ / Compact 32 via the section's `compact` class). Q99, Q108.
- [x] Tasks page (`Views/TasksView.axaml(.cs)`): Today card ("Tasks" 30, Normal/Compact segments, unfold), add row (old, until 3.4), notices / "Add to Today" card / new-category box / Undo bar kept, Today sections; resize handle (split %, 30-70, double-click 46); Shelf card (`Views/ShelfView.axaml(.cs)`, `ViewModels/ShelfViewModel.cs`: "Shelf ▾" views + Group by + Hide Today, "Bins ▾", unfold, hide, search pill → `TaskViewService.SetSearch`, label chips → `Modify`, Categories editor from "+", tidy-up card, bins/groups from `TaskViewService.Query`, Archive under them); rails `Views/ShelfRailView.axaml(.cs)` (Shelf right, Today left); detail card only for an opened task. Ctrl+\ in `MainWindow.OnKeyDown`. Cards use the new `Ab.TasksRadius` (Instrument 14, Paper 18, Night Desk 22). Q100, Q102-Q106.
- [x] `TasksViewModel`: `UseTaskModel(store, actions, views, tidyUp, reminders)` (called by `App` on the task-list branch); one coalesced rebuild (`QueueRefresh`) fills `_sessionRows`, `_queuedRows`, `_doneRows`, `_reminderRows` and the Shelf's per-group lists with keyed reuse (`Replace`, `ShowsSameAs`); the Today page shares the same row lists. Queued during a session = `TaskRowKind.Queued` through `TaskActionsService` (Q101). `TasksOpened()` on a switch to Tasks; `OpenRequested` takes a row opened on Today to the Tasks tab.
- [x] Today page (`Views/TodayView.axaml`, `SessionViewModel.TaskPage`): "Today Tue 11/10" + This session, Queued, Reminders due today (bell), Finished today in a card left of the existing content (on top under 1300 wide). Q107, Q109, Q110.
- Removed from the old page (Q102): counts line, old filter flyout and saved filters, page search, category chips, select-mode button/bar, Unfinished from past sessions (Archive keeps them), status tags.
- Left for later: 3.3 row content (detail line icons, highlight, untouched, origin, sync icon, quick actions ← / →, hover open ▾, drag between sections, multi-select, the open task inline, "Move to This session" for an idle Queued row); 3.4 "+ Add a task" pill, Send to, Jot it down (Ctrl+J) on Today; 3.5 the full View menu (filters, sort, save view), tidy-up flow (Start is disabled), History, dialogs; 3.6 session layout (compact on, Shelf hidden "hidden during session", faded Queued/Finished rules beyond the fade, the idle/focus card's repeat of the checklist on Today); 3.8 keyboard / toasts (the task actions' toasts are not shown yet).
- Build 0 errors; dev app started on dev data with no exception (Today shows the sections; page rebuild 32-47 ms, as before).

## M4 task 3 done (rows and the open task)

- [x] Rows (`ViewModels/TaskListRowViewModel.cs`, `Views/TaskRowView.axaml(.cs)`): title 14.5/500 (2 lines, tooltip "142 · title"), detail line of `TaskRowBit`s (new `TaskRowBit`, `TaskRowBitTone`; ⏱ time limit, steps n/m, 📅 due with overdue/today tones, 🔔 reminder, #tag chips, Finished time or origin, "untouched N wk", "only on this PC", the conflict cloud; Q113, Q114), label dot + name chip (Q112), This session's time at the right (Q116), highlight star + accent tint 14 % (`TaskActionsService.ToggleHighlightAsync`), hover quick actions ← / target / → (`TaskRowAction`, Q117), open arrow, ⋯, drag handle on hover, right-click = the row's menu. Rows read their task-list item from one lookup per rebuild (`_rebuildStored`); `ShowsSameAs` compares the new fields (bits by value), so changed rows refresh and the rest keep their controls. Finish animation: struck through 1.5 s, fold 0.3 s, then the tick (Q119).
- [x] Reorder and moves (`TasksViewModel.Rows.cs` `DropAsync`, `Views/TaskSectionView.axaml.cs`, `TaskRowDrop`): drag within and between sections on the whole page (an end-of-section line when over a header or empty space), Alt+↑/↓ (keyboard stays on the row). Into This session only through `MoveToSessionAsync` (full = notice); idle Queued → This session = new `TaskActions.PlaceInSession` after `SessionTasksService.HasDraftRoomFor` (Q118). Rules Q120, Q121. Sections know their `Bin`; `MoveRow` replaced by the panel's `Drop`.
- [x] Windows multi-select (`TaskRowClick`, `OnPanelRowClick`, `TaskSectionsPanelViewModel.VisibleRows/RowClick/SelectAllRows/ClearSelectionOfRows`): Ctrl/Shift/Ctrl+Shift+click, Ctrl+A, Esc; old select mode removed (Q122). `TasksViewModel.SelectedIds` for the bulk menu.
- [x] Open task (detail card in `Views/TasksView.axaml(.cs)`, `TaskDetailViewModel`, `TaskStepViewModel`, `TaskSnapshot` + `Item`/`FocusedSteps`): title edits on click (as before), chips row (label, #tags, + Tag, time limit menu incl. Custom…, highlight; Q123), Steps · n/m + "add up to … of …", steps with letter chips (`TaskStepLetters`), focus tag/target, step time, drag + Alt+↑/↓ (Q124), add a step, notes ("Add a private note"), read-only reminders line, Effort · soon, footer sync line (Q115), ⋯, ID chip with Copy ID (Q125). Queued tasks open fully (steps, time limit, note through new task actions `RenameStep`, `RemoveStep`, `MoveStep`, `SetNote`). Open arrow up on the open row only while the card shows.
- [x] App: `TasksViewModel.UntouchedWeeks` (Tasks setting) and `SyncLight` (title bar's sync light) set in `App.axaml.cs`.
- Behaviour changes: the old select mode (circles, bar, "Set category" on many) is gone; the detail card's 25/50/90 estimate chips became the time limit chip's menu; a tick now lands after a 1.8 s strike-and-fold (a second tick inside it takes it back); rows of the Shelf and Queued can be dragged and moved with the quick actions; right-click on a row opens its menu.
- Flagged, not changed: `SessionTasksService.DraftActions` counts the draft's Queued tasks toward the cap while no session runs (Q126).
- Left for later: 3.4 add box / Send to / Jot; 3.5 the bulk right-click menu for a selection, the Tags and Reminders & due date menus, Sync devices, Copy ID / Rename one by one in the row menu, label style setting; 3.6 the swap dialog for a full This session (now a notice), the step list popover and session layout; 3.8 the toasts (finish / Undo, Copy ID "Copied …", move toasts) and the roving keyboard focus (↑/↓ within a list, F2, H, Del, Enter); Phase 7 Settings (label style, untouched weeks UI). Not checked by eye: the 3 themes × light/dark look of the tints, ring and chips (built from tokens).
- Build 0 errors; dev app started on dev data with no exception.

## M4 milestone review fixes

- [x] 1 (blocking) `TaskActionsService.MoveToSessionAsync`: a task the running checklist took (`AddToTodayAsync` → Added while a session runs) leaves the list with `TaskStore.TakeBackAsync(_session, id)` (held for the session, no History entry); it was in This session and still on the Shelf / in Queued. Every page path goes through here: the Queued row's target and menu, drag into This session, the Shelf rows' drag. (The old "Add to Today" menu paths keep their own source removal.)
- [x] 2 (blocking, Q126 closed) `SessionTasksService.DraftActions` counts This session's draft tasks only; `HasDraftRoomFor` = `DraftActions() + actions <= ActionCap`. A Shelf task into This session with no session running now checks its `ActionCount` the same way (it only checked for one free action).
- [x] 3 (blocking, Q48 closed) `StartChecklistAsync` starts with `ThisSessionDraftTasks` only; Queued's tasks stay in the list (not held, not tombstoned: `TombstoneNextTasksAsync(joining)`), so the pick sees the same set (Q128 for the phone's old field).
- [x] 4 A Queued task ticked during a session shows under Finished today (row kind Queued, ticked; untick = `ReopenAsync`; its open task is read only) and never joins the next session as done (Q127). Its "Move to This session" menu entry shows only while it is in Queued.
- [x] Nits: pending ticks keyed by task in `TaskFinishQueue` (new), flushed on Quit (Q129); `_shelfRows` lists of groups no longer shown are dropped; the stale comment above `IsInDraft` is gone.
- Build 0 errors; dev app started on dev data with no exception.
- M4 re-check fixes: a finished Queued task goes to History once its finish day is over (`TaskListRules.Settled(lists, now, zone)`, the store passes its zone; Q130); Quit stores waiting ticks before the session end flow; `MoveToSessionAsync` skips a task the running checklist already holds (no second copy); every row of a task (rebuilt, Today page) strikes, folds and resets from `TaskFinishQueue.Changed`. Check program 467/467 (9 new m4/ checks).
- Left for 3.6 (not changed): `SessionTasksService.TombstoneConsumed` treats every past session's task id as used, so "End and mark the rest unfinished" (back to Queued) needs an exception there or those tasks are tombstoned at the next list change; `TaskActionsService.MoveToSessionAsync` with an `overLimitReason` skips the draft room check (and goes over the cap through the session add), which matters once the swap dialog's "Add anyway" passes a reason.
