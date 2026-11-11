# HANDOFF-NOTES: Tasks redesign (PC + shared model run)

Scope: PC + shared model only (bundle Phases 1-3 + the PC part of 7). Android UI and the Kotlin mirror of the model come in a later run; the phone keeps writing the OLD sync fields until then, so the PC must read the phone's old fields and map them into the new model on every sync.

Branch `tasks-ui-overhaul` (worktree `AccountabiliBuddy-tasks-ui`). Bundle: `doc/design-handoff-tasks/` (git-excluded, never stage). Questions: `HANDOFF-QUESTIONS.md`. Build: `"C:/Program Files/dotnet/dotnet.exe" build AccountabiliBuddy.sln` (log to `C:/Users/fblz/AppData/Local/Temp/claude/tasks-ui-logs/`, read only errors/tail).

## Phase status

- [x] Phase 0 Orientation (this file)
- [x] Phase 1 Shared model, migration, merge: **Core part done (M1)**; app wiring is M2 (below); Kotlin mirror is NOT in this run
- [ ] Phase 2 PC services (store DONE (M2.1); session cap, reminders, undo, history, tidy-up, views; calendar export BLOCKED, see Calendar)
- [ ] Phase 3 PC UI (3.1 title bar, 3.2 layout, 3.3 rows/open task, 3.4 add/jot, 3.5 menus, 3.6 session, 3.7 reminders, 3.8 keyboard/toasts)
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
