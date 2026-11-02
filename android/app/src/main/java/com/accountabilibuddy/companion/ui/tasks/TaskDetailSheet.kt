package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ArrowDownward
import androidx.compose.material.icons.outlined.ArrowUpward
import androidx.compose.material.icons.outlined.CalendarToday
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.TaskCategoryChoices.Choice
import com.accountabilibuddy.companion.sync.TaskFields
import com.accountabilibuddy.companion.sync.TaskSteps
import com.accountabilibuddy.companion.ui.kit.AbCheck
import com.accountabilibuddy.companion.ui.kit.AbInsetRow
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbSheet
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.SessionTaskItem
import java.time.Instant

/**
 * One task's details in a bottom sheet (board 5x's detail pane, on a phone): the title, category and (Later) due date, the steps,
 * the estimate chips, the private note and the moves. Edits are kept in a copy and written when a box loses focus, when a chip
 * or a step is changed and when the sheet closes ([onCommit] gets the fields before and after, so only what changed is written).
 *
 * The note is private text: it is kept Keystore-sealed with the lists, travels only in the sealed sync files to the user's own
 * devices, and is never logged or put in a notification. A running task belongs to its PC: only its title and steps are
 * edited here, as requests to the PC.
 */
@Composable
fun TaskDetailSheet(
    detail: TaskDetail,
    categories: List<Choice>,
    actions: TaskDetailActions,
    onCommit: (before: TaskFields, after: TaskFields) -> Unit,
    onDismiss: () -> Unit,
) {
    TaskDetailSheetBody(detail, categories, actions, onCommit, onDismiss)
}

@Composable
private fun TaskDetailSheetBody(
    detail: TaskDetail,
    categories: List<Choice>,
    actions: TaskDetailActions,
    onCommit: (before: TaskFields, after: TaskFields) -> Unit,
    onDismiss: () -> Unit,
) {
    val colors = Ab.colors
    val commitTo by rememberUpdatedState(onCommit)
    // What the sheet opened with, and the copy being edited. A commit writes the difference and moves the base up to the copy.
    var base by remember { mutableStateOf(detail.fields) }
    var draft by remember { mutableStateOf(detail.fields) }
    // A step typed in "Add a step" and not yet added still goes in when the sheet closes (the field keeps its text here).
    val newStep = remember { mutableStateOf("") }
    var stepsChangedElsewhere by remember { mutableStateOf(false) }

    // The task as the lists have it now (a sync can change it while the sheet is open). A field the user has not touched follows it;
    // a field they are editing keeps their edit. Steps are written as one list, so when the user has edited the steps and another
    // device changed them too, the sheet says that saving replaces them.
    LaunchedEffect(detail.fields) {
        val live = detail.fields
        if (live == base) return@LaunchedEffect
        stepsChangedElsewhere = draft.steps != base.steps && live.steps != base.steps
        fun <T> follow(held: T, edited: T, now: T): Pair<T, T> = if (edited == held) now to now else held to edited
        val title = follow(base.title, draft.title, live.title)
        val steps = follow(base.steps, draft.steps, live.steps)
        val estimate = follow(base.estimateMinutes, draft.estimateMinutes, live.estimateMinutes)
        val note = follow(base.note, draft.note, live.note)
        val category = follow(base.category to base.categoryColor, draft.category to draft.categoryColor, live.category to live.categoryColor)
        val due = follow(base.dueOn, draft.dueOn, live.dueOn)
        base = TaskFields(title.first, steps.first, estimate.first, note.first, category.first.first, category.first.second, due.first)
        draft = TaskFields(title.second, steps.second, estimate.second, note.second, category.second.first, category.second.second, due.second)
    }

    fun commit() {
        if (newStep.value.isNotBlank()) {
            draft = draft.copy(steps = TaskSteps.added(draft.steps, newStep.value))
            newStep.value = ""
        }

        if (draft != base) {
            stepsChangedElsewhere = false
            val before = base
            base = draft
            commitTo(before, draft)
        }
    }
    DisposableEffect(Unit) { onDispose { commit() } }

    fun change(next: TaskFields, now: Boolean = true) {
        draft = next
        if (now) commit()
    }

    fun closeAfter(action: () -> Unit) {
        commit()
        action()
        onDismiss()
    }

    val running = detail.ref.kind == TaskKind.Running
    AbSheet(onDismiss = { commit(); onDismiss() }, modifier = Modifier.imePadding()) {
        Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            AbSectionLabel(detail.label)
            SheetField(
                value = draft.title, onValueChange = { change(draft.copy(title = it), now = false) }, placeholder = "Task", onDone = { commit() },
            )
            if (detail.editsDetails || detail.hasDueDate) {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                    if (detail.editsDetails) {
                        TaskCategoryMenu(categories, draft.category, onSelect = { choice -> change(draft.copy(category = choice?.name, categoryColor = choice?.color)) })
                    }
                    if (detail.hasDueDate) DueDateMenu(draft.dueOn, onChange = { change(draft.copy(dueOn = it)) })
                }
            }

            actions.onMarkDone?.let { run -> AbTonalButton("Mark done", onClick = { closeAfter(run) }, icon = Icons.Outlined.Check, modifier = Modifier.fillMaxWidth()) }

            AbSectionLabel("Steps")
            if (stepsChangedElsewhere) {
                Text("The steps also changed on another device. Closing this sheet writes your version of the steps and replaces theirs.", style = Ab.type.small, color = Ab.colors.waiting)
            }
            val liveDone = detail.fields.steps.associate { it.id to it.isDone }
            draft.steps.forEachIndexed { index, step ->
                val done = if (running) liveDone[step.id] == true || step.id in detail.sendingSteps else step.isDone
                StepRow(
                    step = step, done = done, canMoveUp = index > 0, canMoveDown = index < draft.steps.lastIndex,
                    onToggle = {
                        if (running) step.id?.let { actions.onTickRunningStep?.invoke(it) }
                        else change(draft.copy(steps = TaskSteps.toggled(draft.steps, step.id, Instant.now())))
                    },
                    canToggle = !running || (actions.onTickRunningStep != null && !done),
                    onTitle = { text -> change(draft.copy(steps = draft.steps.map { if (it.id == step.id) it.copy(title = text) else it }), now = false) },
                    onCommit = { commit() },
                    onMove = { by -> change(draft.copy(steps = TaskSteps.moved(draft.steps, step.id, by))) },
                    onDelete = { change(draft.copy(steps = TaskSteps.removed(draft.steps, step.id))) },
                )
            }
            AddField("Add a step", onSubmit = { text -> change(draft.copy(steps = TaskSteps.added(draft.steps, text))) }, typed = newStep)

            if (detail.editsDetails) {
                AbSectionLabel("Estimate")
                EstimateChips(draft.estimateMinutes, onChange = { change(draft.copy(estimateMinutes = it)) })
                AbSectionLabel("Note")
                SheetField(
                    value = draft.note, onValueChange = { change(draft.copy(note = it.take(TaskFields.MAX_NOTE_LENGTH)), now = false) },
                    placeholder = "Only you see this", onDone = { commit() }, minLines = 3, maxLines = 8,
                )
                Text(
                    "Private. It is sealed on this phone and goes only to your own devices, in the sealed sync files.",
                    style = Ab.type.small, color = colors.muted,
                )
            }
            detail.pcNote?.let { Text(it, style = Ab.type.small, color = colors.muted) }

            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                actions.onAddToToday?.let { run -> AbTonalButton("Move to This session", onClick = { closeAfter(run) }, icon = Icons.Outlined.CalendarToday, modifier = Modifier.fillMaxWidth()) }
                actions.onMoveToLater?.let { run -> AbTonalButton("Move to Later", onClick = { closeAfter(run) }, icon = Icons.Outlined.Schedule, modifier = Modifier.fillMaxWidth()) }
                actions.onDelete?.let { run -> AbTonalButton("Delete", onClick = { closeAfter(run) }, icon = Icons.Outlined.Delete, modifier = Modifier.fillMaxWidth()) }
            }
        }
    }
}

@Composable
private fun StepRow(
    step: SessionTaskItem,
    done: Boolean,
    canToggle: Boolean,
    canMoveUp: Boolean,
    canMoveDown: Boolean,
    onToggle: () -> Unit,
    onTitle: (String) -> Unit,
    onCommit: () -> Unit,
    onMove: (Int) -> Unit,
    onDelete: () -> Unit,
) {
    val colors = Ab.colors
    AbInsetRow(contentPadding = androidx.compose.foundation.layout.PaddingValues(start = 4.dp, end = 0.dp, top = 2.dp, bottom = 2.dp)) {
        Box(
            Modifier.size(width = 44.dp, height = 48.dp)
                .clickable(enabled = canToggle, role = Role.Checkbox, onClick = onToggle)
                .semantics { contentDescription = if (done) "Step done" else "Tick step" },
            contentAlignment = Alignment.Center,
        ) { AbCheck(done) }
        BasicTextField(
            value = step.title, onValueChange = onTitle, modifier = Modifier.weight(1f).onFocusChanged { if (!it.isFocused) onCommit() },
            textStyle = Ab.type.bodyMedium.copy(color = if (done) colors.muted else colors.ink, textDecoration = if (done) TextDecoration.LineThrough else null),
            cursorBrush = SolidColor(colors.ink),
            keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences, imeAction = ImeAction.Done),
            keyboardActions = KeyboardActions(onDone = { onCommit() }),
        )
        val items = buildList {
            if (canMoveUp) add(TaskMenuItem("Move up", Icons.Outlined.ArrowUpward) { onMove(-1) })
            if (canMoveDown) add(TaskMenuItem("Move down", Icons.Outlined.ArrowDownward) { onMove(1) })
            add(TaskMenuItem("Delete step", Icons.Outlined.Delete, onDelete))
        }
        TaskRowMenu(items)
    }
}
