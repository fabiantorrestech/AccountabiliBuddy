package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.ContentCopy
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.Devices
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material.icons.outlined.Notifications
import androidx.compose.material.icons.outlined.Speed
import androidx.compose.material.icons.outlined.Star
import androidx.compose.material.icons.outlined.Tag
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbSwitch
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetCheck
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.sheets.AbSheetValue
import com.accountabilibuddy.companion.ui.tasks.SheetField
import com.accountabilibuddy.companion.ui.tasks.TasksPc
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.TaskLabels
import com.accountabilibuddy.shared.tasks.TaskScopeRules
import com.accountabilibuddy.shared.tasks.TaskReminderRings
import com.accountabilibuddy.shared.tasks.TaskBins
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskRowText
import java.time.LocalDate
import java.time.ZoneId

/** The time limits the picker offers (bundle spec §4 E 7), in minutes. */
private val TIME_LIMITS = listOf(10, 15, 25, 50, 90)

/**
 * The ⋮ sheet of a task (board Tasks-Phone-Task-Menu; the same menu, in the same order, as the PC's row menu), 80% tall: the title
 * is the task's, the subtitle where it is ("Shelf · Next"). Rename (or hold the title) · the **Move to** grid (Today: This session,
 * Queued; Shelf: To sort, Next, Later; the current one filled) · Time limit · Reminders · Label (its row's mark is the label's colour
 * dot) · Tags · Highlight (switch) · Effort (greyed, "Coming soon") · Sync · Delete (red) · Copy ID (last).
 *
 * The rows with › open a sub-page of the same sheet with a back arrow ([TaskMenuPage]); [start] opens one directly (holding a title
 * opens Rename). Every change goes through [controller].
 *
 * @param pc The PC with a running session (This session is then asked of it), or null.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun TaskMenuSheet(
    task: TaskItem,
    lists: TaskLists,
    self: String,
    zone: ZoneId,
    controller: ShelfController,
    pc: TasksPc?,
    onDismiss: () -> Unit,
    start: TaskMenuPage = TaskMenuPage.Main,
) {
    val colors = Ab.colors
    val context = LocalContext.current
    var page by remember { mutableStateOf(start) }
    val app = context.applicationContext as com.accountabilibuddy.companion.CompanionApp
    val scopeView = com.accountabilibuddy.companion.ui.tasks.rememberScopeView(app)
    val label = scopeView.labels.firstOrNull { it.id.equals(task.labelId, ignoreCase = true) }
    val back: (() -> Unit)? = when {
        page == TaskMenuPage.Main -> null
        page == start -> onDismiss
        page == TaskMenuPage.NewLabel -> ({ page = TaskMenuPage.Label })
        else -> ({ page = TaskMenuPage.Main })
    }
    val closeOrMain = { if (start == TaskMenuPage.Main) page = TaskMenuPage.Main else onDismiss() }

    AbSheet(
        title = when (page) {
            TaskMenuPage.Main -> task.title
            TaskMenuPage.Rename -> "Rename"
            TaskMenuPage.TimeLimit -> "Time limit"
            TaskMenuPage.Reminders -> "Reminders & due date"
            TaskMenuPage.Label -> "Label"
            TaskMenuPage.NewLabel -> "New label"
            TaskMenuPage.Tags -> "Tags"
            TaskMenuPage.Sync -> "Sync"
        },
        subtitle = if (page == TaskMenuPage.Main) TaskBins.place(task.bin) else task.title,
        onDismiss = onDismiss,
        height = AbSheetHeight.Tall,
        onBack = back,
    ) {
        when (page) {
            TaskMenuPage.Main -> {
                AbSheetRow("Rename", onClick = { page = TaskMenuPage.Rename }, icon = Icons.Outlined.Edit, subtitle = "Or open the task and tap its title", trailing = { AbSheetValue(null) })
                MoveToGrid("Move to", task.bin, onPick = { bin ->
                    if (bin != task.bin) controller.move(listOf(task.id), bin, pc)
                    onDismiss()
                })
                AbSheetRow(
                    "Time limit", onClick = { page = TaskMenuPage.TimeLimit }, icon = Icons.Outlined.Timer,
                    trailing = { AbSheetValue(task.estimateMinutes?.let { TaskRowText.minutes(it) } ?: "None") },
                )
                AbSheetRow("Reminders", onClick = { page = TaskMenuPage.Reminders }, icon = Icons.Outlined.Notifications, trailing = { AbSheetValue(TaskRowText.remindersSet(task)) })
                SheetMarkRow("Label", onClick = { page = TaskMenuPage.Label }, leading = { LabelDot(label?.color, 12.dp) }) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        if (label != null) LabelDot(label.color, 8.dp)
                        AbSheetValue(label?.name ?: "No label")
                    }
                }
                AbSheetRow("Tags", onClick = { page = TaskMenuPage.Tags }, icon = Icons.Outlined.Tag, trailing = { AbSheetValue(TaskLabels.text(task.tags).ifEmpty { "None" }) })
                AbSheetRow(
                    "Highlight", onClick = { controller.setHighlight(task.id, !task.highlight) }, icon = Icons.Outlined.Star, subtitle = "Stars it and pins it to the top",
                    role = Role.Switch, trailing = { AbSwitch(task.highlight) },
                )
                AbSheetRow("Effort", onClick = null, icon = Icons.Outlined.Speed, subtitle = "Coming soon", enabled = false)
                AbSheetRow("Sync", onClick = { page = TaskMenuPage.Sync }, icon = Icons.Outlined.Devices, trailing = { AbSheetValue(TaskRowText.devices(task, self)) })
                AbSheetRow("Delete", onClick = { controller.delete(listOf(task.id)); onDismiss() }, icon = Icons.Outlined.Delete, danger = true)
                val id = TaskRowText.id(task)
                AbSheetRow(
                    "Copy ID", onClick = { if (id != null) copyTaskId(context, id); onDismiss() }, icon = Icons.Outlined.ContentCopy, enabled = id != null,
                    subtitle = if (id == null) "It gets its number at the next sync" else null,
                    trailing = { if (id != null) Text(id, style = Ab.type.small.copy(fontFeatureSettings = "tnum"), color = colors.muted) },
                )
            }

            TaskMenuPage.Rename -> {
                var text by remember { mutableStateOf(task.title) }
                val save = {
                    if (text.isNotBlank()) {
                        controller.rename(task.id, text)
                        closeOrMain()
                    }
                }
                Column(Modifier.padding(horizontal = 24.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    SheetField(value = text, onValueChange = { text = it }, placeholder = "Task title", onDone = save)
                    AbPrimaryButton("Save", onClick = save, enabled = text.isNotBlank() && text.trim() != task.title, modifier = Modifier.fillMaxWidth())
                }
            }

            TaskMenuPage.TimeLimit -> {
                val current = task.estimateMinutes
                AbSheetRow("None", onClick = { controller.setEstimate(task.id, null); closeOrMain() }, role = Role.RadioButton, trailing = { AbSheetCheck(current == null) })
                TIME_LIMITS.forEach { minutes ->
                    AbSheetRow(TaskRowText.minutes(minutes), onClick = { controller.setEstimate(task.id, minutes); closeOrMain() }, role = Role.RadioButton, trailing = { AbSheetCheck(current == minutes) })
                }
                var custom by remember { mutableStateOf(current?.takeIf { it !in TIME_LIMITS }?.toString().orEmpty()) }
                Row(Modifier.padding(horizontal = 24.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    AbTextField(custom, { typed -> custom = typed.filter { it.isDigit() }.take(3) }, "Custom, in minutes", Modifier.weight(1f), keyboardType = KeyboardType.Number)
                    AbPrimaryButton("Set", onClick = { controller.setEstimate(task.id, custom.toIntOrNull()); closeOrMain() }, enabled = (custom.toIntOrNull() ?: 0) > 0)
                }
            }

            TaskMenuPage.Reminders -> TaskRemindersPage(task, lists, self, zone, controller)

            TaskMenuPage.Label -> {
                SheetMarkRow("No label", onClick = { controller.setLabel(task.id, null); closeOrMain() }, leading = { LabelDot(null, 12.dp) }) { AbSheetCheck(label == null) }
                LabelGroups(scopeView) { item ->
                    SheetMarkRow(item.name, onClick = { controller.setLabel(task.id, item.id); closeOrMain() }, leading = { LabelDot(item.color, 12.dp) }) {
                        AbSheetCheck(item.id.equals(task.labelId, ignoreCase = true))
                    }
                }
                AbSheetRow("New label…", onClick = { page = TaskMenuPage.NewLabel }, icon = Icons.Outlined.Add)
                SheetFoot("A label is the coloured dot, and each task has one. Edit the list in Settings › Tasks › Labels.")
            }

            TaskMenuPage.NewLabel -> NewLabelForm(scopeView, canKeepHere = app.sync.isJoined, onAdd = { name, here, shared -> if (controller.createLabel(name, task.id, here, shared)) closeOrMain() })

            TaskMenuPage.Tags -> {
                val tags = task.tags.orEmpty()
                var typed by remember { mutableStateOf("") }
                var onlyHere by remember { mutableStateOf(false) }
                val known = scopeView.knownTags()
                val add = {
                    val tag = typed.trim().trimStart('#')
                    if (tag.isNotEmpty()) {
                        val isNew = !TaskLabels.has(known, tag)
                        controller.setTags(task.id, tags + tag)
                        // ADR 89: a NEW tag typed with "Only on this phone" on stays on this phone.
                        if (isNew && onlyHere && app.sync.isJoined) controller.setTagScope(tag, onlyHere = true)
                    }
                    typed = ""
                }
                Column(Modifier.padding(horizontal = 24.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    if (tags.isEmpty()) Text("No tags yet.", style = Ab.type.small, color = colors.muted)
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        tags.forEach { tag -> ShelfChip("#$tag  ×", selected = true, height = 36) { controller.setTags(task.id, tags.filterNot { TaskLabels.same(it, tag) }) } }
                    }
                    SheetField(value = typed, onValueChange = { typed = it }, placeholder = "Add a tag", onDone = add, singleLine = true)
                    if (app.sync.isJoined) {
                        AbSheetRow(TaskScopeRules.ONLY_ON_THIS_PHONE, onClick = { onlyHere = !onlyHere }, role = Role.Switch, subtitle = "For a new tag: other devices won't show it", trailing = { AbSwitch(onlyHere) })
                    }
                    val others = known.filterNot { TaskLabels.has(tags, it) }
                    // ADR 89: "Shared" first, then "This device".
                    scopeView.tagGroups(others).forEach { group ->
                        ScopeGroupHead(group)
                        FlowRow(Modifier.padding(horizontal = 24.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            group.items.forEach { tag -> ShelfChip("#$tag", selected = false, height = 36) { controller.setTags(task.id, tags + tag) } }
                        }
                    }
                }
            }

            TaskMenuPage.Sync -> TaskSyncPage(task, self, controller)
        }
    }
}
