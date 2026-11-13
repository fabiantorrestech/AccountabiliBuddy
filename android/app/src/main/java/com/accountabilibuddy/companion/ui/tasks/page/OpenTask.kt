package com.accountabilibuddy.companion.ui.tasks.page

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.MoreVert
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import com.accountabilibuddy.shared.tasks.ShelfSteps
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.ContentCopy
import androidx.compose.material.icons.outlined.Devices
import androidx.compose.material.icons.outlined.Notifications
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.TaskStepLetters
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskRowText
import java.time.LocalDate
import java.time.ZoneId

/**
 * A task opened in place under its row (bundle spec §4 C, in R7.3a's reading form): "Steps · 1/3" and the steps with their letters
 * (a tap ticks one), the note, the reminders line, how it syncs and the ID chip ("142 ⧉", a tap copies it). Changing the label, time
 * limit, tags and the rest is the ⋮ sheet's. [rings] says whether this phone rings for its reminders ("Rings on this phone").
 *
 * With [edit] (R7.3b) the steps can be changed here: "Add a step" at the foot of the list, and each step's ⋮ menu (Make it its own
 * task, Move up, Move down, Delete step). The heading adds "add up to 35 min of 50" when the steps have times.
 */
@Composable
fun OpenTask(task: TaskItem, self: String, zone: ZoneId, onTickStep: (String) -> Unit, modifier: Modifier = Modifier, rings: String? = null, edit: StepEdits? = null) {
    val colors = Ab.colors
    val context = LocalContext.current
    val steps = task.steps.orEmpty()
    val note = task.note
    Column(modifier.fillMaxWidth().padding(start = 40.dp, end = 8.dp, bottom = 10.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        if (steps.isNotEmpty()) {
            Text(ShelfSteps.heading(task).orEmpty(), style = Ab.type.small, fontWeight = FontWeight.SemiBold, color = colors.muted)
            steps.forEachIndexed { index, step ->
                Row(
                    Modifier.fillMaxWidth().heightIn(min = 44.dp).clip(RoundedCornerShape(12.dp))
                        .clickable(role = Role.Checkbox, onClickLabel = if (step.isDone) "Untick" else "Tick") { onTickStep(step.id) },
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    Box(
                        Modifier.size(20.dp).then(if (step.isDone) Modifier.background(colors.met, CircleShape) else Modifier.border(2.dp, colors.faint, CircleShape)),
                        contentAlignment = Alignment.Center,
                    ) {
                        if (step.isDone) Icon(Icons.Outlined.Check, contentDescription = null, tint = colors.bg, modifier = Modifier.size(13.dp))
                    }
                    Text(TaskStepLetters.of(index), style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.muted)
                    Text(
                        step.title, style = Ab.type.bodyMedium.copy(fontSize = 14.5.sp), color = if (step.isDone) colors.muted else colors.ink,
                        textDecoration = if (step.isDone) TextDecoration.LineThrough else null, maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f),
                    )
                    val limit = step.estimateMinutes
                    if (limit != null) {
                        Icon(Icons.Outlined.Timer, contentDescription = null, tint = colors.muted, modifier = Modifier.size(14.dp))
                        Text(TaskRowText.minutes(limit), style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.muted)
                    }
                    if (edit != null) StepMenu(step.title, step.isDone, first = index == 0, last = index == steps.lastIndex, onPick = { action -> edit.onStep(step.id, step.title, action) })
                }
            }
        }
        if (edit != null) AddStepField(edit.onAdd)
        if (!note.isNullOrBlank()) {
            Text(
                note, style = Ab.type.bodyMedium.copy(fontSize = 14.sp), color = colors.ink,
                modifier = Modifier.fillMaxWidth().background(colors.surface2, RoundedCornerShape(14.dp)).padding(12.dp),
            )
        }
        TaskRowText.reminder(task, LocalDate.now(zone), zone)?.let { reminder ->
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Icon(Icons.Outlined.Notifications, contentDescription = "Reminders", tint = colors.muted, modifier = Modifier.size(16.dp))
                // With "Rings on" (M7 T2): whether this phone is one of the devices that ring for it.
                Text(listOfNotNull(reminder, rings).joinToString(" · "), style = Ab.type.small, color = colors.muted)
            }
        }
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Icon(Icons.Outlined.Devices, contentDescription = null, tint = colors.faint, modifier = Modifier.size(16.dp))
            Text("Syncs to: ${TaskRowText.devices(task, self).replaceFirstChar { it.lowercase() }}", style = Ab.type.small, color = colors.faint, maxLines = 1, modifier = Modifier.weight(1f))
            val id = TaskRowText.id(task)
            if (id != null) {
                Row(
                    Modifier.clip(CircleShape).background(colors.surface2).clickable(role = Role.Button) { copyTaskId(context, id) }
                        .semantics { contentDescription = "Copy ID $id" }.padding(horizontal = 10.dp, vertical = 5.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                ) {
                    Text(id, style = Ab.type.small.copy(fontFeatureSettings = "tnum"), color = colors.muted)
                    Icon(Icons.Outlined.ContentCopy, contentDescription = null, tint = colors.muted, modifier = Modifier.size(13.dp))
                }
            } else {
                Spacer(Modifier.size(1.dp))
            }
        }
    }
}

/** Puts a task's number on the clipboard (the number only: never a title). */
fun copyTaskId(context: Context, id: String) {
    context.getSystemService(ClipboardManager::class.java)?.setPrimaryClip(ClipData.newPlainText("Task ID", id))
}

/** What the open task can do to its steps ([OpenTask]'s `edit`). */
class StepEdits(val onAdd: (String) -> Unit, val onStep: (stepId: String, title: String, action: StepAction) -> Unit)

/** A step menu's choices. */
enum class StepAction { OwnTask, Up, Down, Delete }

// A step's ⋮: Make it its own task (not for a ticked step), Move up / down, Delete step.
@Composable
private fun StepMenu(title: String, done: Boolean, first: Boolean, last: Boolean, onPick: (StepAction) -> Unit) {
    val colors = Ab.colors
    var open by remember { mutableStateOf(false) }
    Box {
        Box(
            Modifier.size(44.dp).clip(CircleShape).clickable(role = Role.Button) { open = true }.semantics { contentDescription = "Step menu: $title" },
            contentAlignment = Alignment.Center,
        ) {
            Icon(Icons.Outlined.MoreVert, contentDescription = null, tint = colors.muted, modifier = Modifier.size(18.dp))
        }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }, containerColor = colors.surface2) {
            val pick = { action: StepAction ->
                open = false
                onPick(action)
            }
            if (!done) DropdownMenuItem(text = { Text("Make it its own task", color = colors.ink) }, onClick = { pick(StepAction.OwnTask) })
            if (!first) DropdownMenuItem(text = { Text("Move up", color = colors.ink) }, onClick = { pick(StepAction.Up) })
            if (!last) DropdownMenuItem(text = { Text("Move down", color = colors.ink) }, onClick = { pick(StepAction.Down) })
            DropdownMenuItem(text = { Text("Delete step", color = colors.drift) }, onClick = { pick(StepAction.Delete) })
        }
    }
}

// "Add a step": typed in place; Done (or the +) adds it at the end and keeps the field for the next one.
@Composable
private fun AddStepField(onAdd: (String) -> Unit) {
    val colors = Ab.colors
    var text by remember { mutableStateOf("") }
    val add = {
        if (text.isNotBlank()) {
            onAdd(text.trim())
            text = ""
        }
    }
    Row(
        Modifier.fillMaxWidth().heightIn(min = 44.dp).clip(RoundedCornerShape(12.dp)).border(1.dp, colors.line, RoundedCornerShape(12.dp)).padding(start = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.weight(1f), contentAlignment = Alignment.CenterStart) {
            if (text.isEmpty()) Text("Add a step", style = Ab.type.bodyMedium.copy(fontSize = 14.5.sp), color = colors.faint)
            BasicTextField(
                value = text, onValueChange = { text = it }, singleLine = true, modifier = Modifier.fillMaxWidth().semantics { contentDescription = "Add a step" },
                textStyle = Ab.type.bodyMedium.copy(fontSize = 14.5.sp, color = colors.ink), cursorBrush = SolidColor(colors.accent),
                keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences, imeAction = ImeAction.Done), keyboardActions = KeyboardActions(onDone = { add() }),
            )
        }
        Box(
            Modifier.size(44.dp).clip(CircleShape).clickable(role = Role.Button, enabled = text.isNotBlank(), onClick = add).semantics { contentDescription = "Add the step" },
            contentAlignment = Alignment.Center,
        ) {
            Icon(Icons.Outlined.Add, contentDescription = null, tint = if (text.isNotBlank()) colors.accent else colors.faint, modifier = Modifier.size(20.dp))
        }
    }
}
