package com.accountabilibuddy.companion.ui

import androidx.compose.material3.SnackbarDuration
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.SnackbarResult
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.sync.OpenComebacks
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.sync.TaskCategoryChoices.Choice
import com.accountabilibuddy.companion.sync.TaskFields
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.tasks.SheetField
import com.accountabilibuddy.companion.ui.tasks.TaskDetail
import com.accountabilibuddy.companion.ui.tasks.TaskDetailActions
import com.accountabilibuddy.companion.ui.tasks.TaskDetailSheet
import com.accountabilibuddy.companion.ui.tasks.TaskKind
import com.accountabilibuddy.companion.ui.tasks.TaskRef
import com.accountabilibuddy.companion.ui.tasks.TaskRowModel
import com.accountabilibuddy.companion.ui.tasks.TasksController
import com.accountabilibuddy.companion.ui.tasks.TasksModel
import com.accountabilibuddy.companion.ui.tasks.TasksPane
import com.accountabilibuddy.companion.ui.tasks.TasksPaneActions
import com.accountabilibuddy.companion.ui.tasks.TasksPc
import com.accountabilibuddy.shared.sync.SyncAction
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.time.Instant

/**
 * Tasks (ADR 24, 30, 48, 50): the running session's checklist or the next session's tasks, Come back to, Later and Done today.
 * This is the host of the redesigned pane: it reads the lists, keeps what the screen remembers (the chip, the search, which
 * groups are open, which task's details are showing) and gives the list to [TasksPane] and a task's details to [TaskDetailSheet];
 * every change goes through [TasksController], which decides whether it is made here or asked of the PC.
 *
 * The search text, the typed task and the details live in memory only (never in the saved state, never logged).
 */
@Composable
fun TasksScreen(controlPc: ReceivedDevice?, snackbar: SnackbarHostState) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val scope = rememberCoroutineScope()
    val nextTasks by app.sharedLists.nextTasks.collectAsState()
    val parked by app.sharedLists.parked.collectAsState()
    val later by app.sharedLists.later.collectAsState()
    val received by app.sync.received.collectAsState()
    val pending by app.actions.pending.collectAsState()
    // Looked at again every half minute: a session that ended on the PC stops being "running" here too.
    val now by produceState(Instant.now()) {
        while (true) {
            delay(30_000)
            value = Instant.now()
        }
    }

    var category by rememberSaveable { mutableStateOf<String?>(null) }
    var searching by rememberSaveable { mutableStateOf(false) }
    var laterOpen by rememberSaveable { mutableStateOf(false) }
    var doneOpen by rememberSaveable { mutableStateOf(false) }
    var search by remember { mutableStateOf("") }
    var open by remember { mutableStateOf<TaskRef?>(null) }
    var editing by remember { mutableStateOf<TaskRowModel?>(null) }
    var addingToday by remember { mutableStateOf<TaskRowModel?>(null) }
    var held by remember { mutableStateOf<Pair<String, Choice?>?>(null) }

    val pc = TasksPc.of(controlPc, received, now, app.runningTaskEdits.isAvailable)
    val comebacks = OpenComebacks.of(parked, received, now)
    // Items whose "Add to Today" waits for a PC's answer: shown as sent, and not sent again.
    val waiting = app.todayHandoff.waitingIds(pending)
    val model = TasksModel.build(nextTasks, pc, pending, comebacks, parked, later, waiting, received, category, search, now)
    val chip = category?.let { name -> model.categories.firstOrNull { it.name.equals(name, ignoreCase = true) } }

    val controller = remember(app, snackbar) {
        TasksController(app) { message, undo ->
            scope.launch {
                snackbar.currentSnackbarData?.dismiss()
                val result = snackbar.showSnackbar(message, actionLabel = if (undo != null) "Undo" else null, duration = SnackbarDuration.Short)
                if (result == SnackbarResult.ActionPerformed) undo?.invoke()
            }
        }
    }

    val actions = TasksPaneActions(
        onOpen = { row ->
            when (row.ref.kind) {
                TaskKind.Next, TaskKind.Running, TaskKind.Later -> open = row.ref
                TaskKind.ComeBack -> if (!row.readOnly) editing = row
                else -> Unit
            }
        },
        onTick = { row -> controller.tick(row, pc, comebacks.legacySource) },
        onAddToToday = { row -> if (controller.needsReason(pc)) addingToday = row else controller.addToToday(row.ref, pc) },
        onMoveToLater = { row -> controller.moveToLater(row.ref) },
        onMoveToComeBack = { row -> controller.moveToComeBack(row.ref) },
        onMove = { row, by -> controller.move(row.ref, by, pc, model.session) },
        onEditComeback = { row -> editing = row },
        onPutBack = { row -> controller.putBack(row) },
        onDelete = { row -> controller.delete(row.ref) },
        // Next to a PC older than the shared lists a come-back made here would never reach it, so Later can't move there.
        canMoveToComeBack = comebacks.legacySource == null,
    )

    TasksPane(
        model = model, pc = pc, comebackOldPc = comebacks.legacySource,
        // The newest PC's version says whether Later reaches it (the received copy keeps no Later list to look at).
        laterOldPc = com.accountabilibuddy.companion.sync.PcFileControl.pcs(received).firstOrNull()?.file?.device
            ?.takeUnless { com.accountabilibuddy.companion.sync.PcFileControl.supportsLaterDetails(it) }?.name,
        category = category, onCategory = { category = it },
        search = search, onSearch = { search = it }, searching = searching, onSearching = { searching = it },
        laterOpen = laterOpen, onLaterOpen = { laterOpen = it }, doneOpen = doneOpen, onDoneOpen = { doneOpen = it },
        actions = actions,
        addPlaceholder = when {
            pc == null -> "Add a task"
            pc.canEdit -> "Add a task on ${pc.name}"
            else -> "Update ${pc.name} to add tasks from here"
        },
        addEnabled = pc == null || pc.canEdit,
        onAdd = { text ->
            when {
                pc != null -> controller.addRunning(pc, text, chip)
                controller.needsReason(null) -> held = text to chip
                else -> controller.addNext(text, chip)
            }
        },
        onAddLater = { text -> controller.addLater(text, chip) },
        // Not narrowed by the chip or the search; what is already on its way to a PC or belongs to an older PC can't be taken.
        pickRows = {
            TasksModel.build(nextTasks, pc, pending, comebacks, parked, later, waiting, received, null, "", now)
                .let { all -> all.comeBacks + all.later }
                .filter { row -> !row.sending && !row.readOnly }
        },
        onPick = { row -> if (controller.needsReason(pc)) addingToday = row else controller.addToToday(row.ref, pc) },
    )

    // The details of the task that was tapped, read fresh from the lists each time, so a sync that changes it shows here.
    val detail = open?.let { ref -> detailOf(ref, nextTasks, later, pc, pending) }
    LaunchedEffect(open, detail == null) { if (open != null && detail == null) open = null }
    val shown = open
    if (shown != null && detail != null) {
        TaskDetailSheet(
            detail = detail,
            categories = model.categories,
            actions = detailActions(shown, detail, controller, pc, onAddToToday = { row -> if (controller.needsReason(pc)) addingToday = row else controller.addToToday(row.ref, pc) }),
            onCommit = { before, after -> controller.commit(shown, before, after, pc) },
            onDismiss = { open = null },
        )
    }

    editing?.let { row ->
        EditComebackDialog(row.title, onSave = { text -> controller.editComeback(row.ref.id, text); editing = null }, onDismiss = { editing = null })
    }

    // Past 3 tasks, "Add to Today" and a typed task ask for a reason first (a running session's PC keeps its own count).
    addingToday?.let { row ->
        OverLimitDialog(
            title = row.title, onPark = null,
            onAddAnyway = { reason -> if (controller.addToToday(row.ref, pc, reason)) addingToday = null },
            onCancel = { addingToday = null },
        )
    }
    held?.let { (text, chosen) ->
        OverLimitDialog(
            title = text,
            // Next to an older PC a parked thought would go to a list that PC never gets and this view does not show.
            onPark = if (comebacks.legacySource == null) ({ controller.park(text); held = null }) else null,
            onAddAnyway = { reason ->
                if (app.overLimit.record(reason)) {
                    controller.addNext(text, chosen)
                    held = null
                }
            },
            onCancel = { held = null },
        )
    }
}

// The task [ref] as the sheet shows it, null when it is not on its list any more.
private fun detailOf(ref: TaskRef, nextTasks: List<com.accountabilibuddy.shared.sync.SyncNextTask>, later: List<com.accountabilibuddy.shared.sync.SyncLaterItem>, pc: TasksPc?, pending: List<SyncAction>): TaskDetail? =
    when (ref.kind) {
        TaskKind.Next -> nextTasks.firstOrNull { it.id == ref.id && !it.isDone }?.let { task ->
            TaskDetail(
                ref, "This session", TaskFields(task.title, task.subtasks.orEmpty(), task.estimateMinutes, task.note.orEmpty(), task.category, task.categoryColor, null),
                isDone = false, editsDetails = true, hasDueDate = false,
            )
        }
        TaskKind.Later -> later.firstOrNull { it.id == ref.id && it.doneAt == null }?.let { item ->
            TaskDetail(
                ref, "Later", TaskFields(item.title, item.subtasks.orEmpty(), item.estimateMinutes, item.note.orEmpty(), item.category, item.categoryColor, item.dueOn),
                isDone = false, editsDetails = true, hasDueDate = true,
            )
        }
        TaskKind.Running -> pc?.checklist?.tasks?.firstOrNull { it.id == ref.id }?.let { task ->
            TaskDetail(
                ref, "${pc.name} session", TaskFields(task.title, task.subtasks, null, "", task.category, task.categoryColor, null),
                isDone = task.isDone, editsDetails = false, hasDueDate = false,
                sendingSteps = pending.filter { it.target == pc.pcId && it.kind == SyncAction.TICK_TASK }.mapNotNull { it.item }.toSet(),
                pcNote = if (pc.canEdit) "Changes go to ${pc.name}, which applies them under its own rules. Estimates, notes and categories are set on ${pc.name}."
                else "Update AccountabiliBuddy on ${pc.name} to edit its tasks from here.",
            )
        }
        else -> null
    }

private fun detailActions(
    ref: TaskRef, detail: TaskDetail, controller: TasksController, pc: TasksPc?, onAddToToday: (TaskRowModel) -> Unit,
): TaskDetailActions {
    // The sheet only needs the row to say which task it is; the lists are the truth.
    val row = TaskRowModel(ref, detail.fields.title, "", detail.fields.category, detail.fields.categoryColor)
    return when (ref.kind) {
        TaskKind.Next -> TaskDetailActions(
            onMarkDone = { controller.tick(row, null, null) },
            onMoveToLater = { controller.moveToLater(ref) },
            onDelete = { controller.delete(ref) },
        )
        TaskKind.Later -> TaskDetailActions(
            onMarkDone = { controller.tick(row, null, null) },
            onAddToToday = if (pc == null || pc.canEdit) ({ onAddToToday(row) }) else null,
            onDelete = { controller.delete(ref) },
        )
        TaskKind.Running -> TaskDetailActions(
            onMarkDone = if (!detail.isDone && pc != null) ({ controller.tick(row, pc, null) }) else null,
            // Ticking is the old tickTask action (ADR 26): it works on any PC that took ticks before, edits or not.
            onTickRunningStep = if (pc != null) ({ stepId -> controller.tickStep(pc, stepId) }) else null,
        )
        else -> TaskDetailActions()
    }
}

@Composable
private fun EditComebackDialog(text: String, onSave: (String) -> Unit, onDismiss: () -> Unit) {
    var edited by remember { mutableStateOf(text) }
    AbDialog(title = "Edit thought", onDismiss = onDismiss, confirmText = "Save", onConfirm = { if (edited.isNotBlank()) onSave(edited) }) {
        SheetField(value = edited, onValueChange = { edited = it }, placeholder = "What should you come back to?", onDone = {})
    }
}
