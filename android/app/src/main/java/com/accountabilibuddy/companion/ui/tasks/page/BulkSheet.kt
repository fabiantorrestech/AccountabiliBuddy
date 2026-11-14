package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material.icons.outlined.Speed
import androidx.compose.material.icons.outlined.Tag
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.sheets.AbSheetValue
import com.accountabilibuddy.companion.ui.tasks.SheetField
import com.accountabilibuddy.companion.ui.tasks.TasksPc
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.TaskLabels
import com.accountabilibuddy.shared.tasks.ShelfToasts
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskRowText

/** The pages of select mode's sheets (bundle spec §12 and its sheet list): the bulk bar's Move and Options, and the selected list. */
enum class BulkPage { Move, Options, TimeLimit, Label, NewLabel, Tags, Selected }

/**
 * Select mode's sheets for the selected tasks ([tasks]): **Move** (the Move to grid: Today's This session and Queued, the Shelf's
 * To sort, Next and Later), **Options** (Time limit ›, Label ›, Tags ›, Effort soon, Rename one by one ›) with their pages, and the
 * **selected list** (each row's × unselects it; unselecting the last one closes it). Every sub-page has a back arrow, also when it
 * was opened directly. Each change is one stamped edit per task with one Undo for all of them.
 *
 * @param pc The PC with a running session (This session is then asked of it), or null.
 * @param onMoved A move was made: select mode ends.
 * @param onRenameEach Rename one by one: the rename sheet for each task in turn.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun BulkSheet(
    start: BulkPage,
    tasks: List<TaskItem>,
    lists: TaskLists,
    controller: ShelfController,
    pc: TasksPc?,
    onUnselect: (String) -> Unit,
    onMoved: () -> Unit,
    onRenameEach: () -> Unit,
    onDismiss: () -> Unit,
) {
    val colors = Ab.colors
    var page by remember { mutableStateOf(start) }
    val ids = tasks.map { it.id }
    val app = androidx.compose.ui.platform.LocalContext.current.applicationContext as com.accountabilibuddy.companion.CompanionApp
    val scopeView = com.accountabilibuddy.companion.ui.tasks.rememberScopeView(app)
    val count = ShelfToasts.tasksWord(tasks.size)
    val toOptions = { if (start == BulkPage.Options) page = BulkPage.Options else onDismiss() }
    val back: (() -> Unit)? = when (page) {
        BulkPage.Move, BulkPage.Options, BulkPage.Selected -> null
        BulkPage.NewLabel -> ({ page = BulkPage.Label })
        else -> toOptions
    }

    AbSheet(
        title = when (page) {
            BulkPage.Move -> "Move $count"
            BulkPage.Options -> "Options"
            BulkPage.TimeLimit -> "Time limit"
            BulkPage.Label -> "Label for ${tasks.size}"
            BulkPage.NewLabel -> "New label"
            BulkPage.Tags -> "Tags"
            BulkPage.Selected -> "${tasks.size} selected"
        },
        subtitle = when (page) {
            BulkPage.Move -> "Where they go"
            BulkPage.Selected -> "Tap × to unselect one"
            else -> "For the $count you selected"
        },
        onDismiss = onDismiss,
        height = AbSheetHeight.Tall,
        onBack = back,
    ) {
        when (page) {
            BulkPage.Move -> MoveToGrid("Move to", tasks.map { it.bin }.distinct().singleOrNull(), onPick = { bin ->
                controller.move(ids, bin, pc)
                onMoved()
                onDismiss()
            })

            BulkPage.Options -> {
                AbSheetRow("Time limit", onClick = { page = BulkPage.TimeLimit }, icon = Icons.Outlined.Timer, trailing = { AbSheetValue(null) })
                SheetMarkRow("Label", onClick = { page = BulkPage.Label }, leading = { LabelDot(null, 12.dp) }) { AbSheetValue(null) }
                AbSheetRow("Tags", onClick = { page = BulkPage.Tags }, icon = Icons.Outlined.Tag, trailing = { AbSheetValue(null) })
                AbSheetRow("Effort", onClick = null, icon = Icons.Outlined.Speed, subtitle = "Coming soon", enabled = false)
                AbSheetRow("Rename one by one", onClick = { onRenameEach(); onDismiss() }, icon = Icons.Outlined.Edit, subtitle = "Each title in turn", trailing = { AbSheetValue(null) })
            }

            BulkPage.TimeLimit -> {
                AbSheetRow("None", onClick = { controller.setEstimateAll(ids, null); toOptions() }, role = Role.RadioButton)
                TIME_LIMIT_CHOICES.forEach { minutes ->
                    AbSheetRow(TaskRowText.minutes(minutes), onClick = { controller.setEstimateAll(ids, minutes); toOptions() }, role = Role.RadioButton)
                }
                var custom by remember { mutableStateOf("") }
                Row(Modifier.padding(horizontal = 24.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    AbTextField(custom, { typed -> custom = typed.filter { it.isDigit() }.take(3) }, "Custom, in minutes", Modifier.weight(1f), keyboardType = KeyboardType.Number)
                    AbPrimaryButton("Set", onClick = { controller.setEstimateAll(ids, custom.toIntOrNull()); toOptions() }, enabled = (custom.toIntOrNull() ?: 0) > 0)
                }
            }

            BulkPage.Label -> {
                SheetMarkRow("No label", onClick = { controller.setLabelAll(ids, null); toOptions() }, leading = { LabelDot(null, 12.dp) })
                LabelGroups(scopeView) { item ->
                    SheetMarkRow(item.name, onClick = { controller.setLabelAll(ids, item.id); toOptions() }, leading = { LabelDot(item.color, 12.dp) })
                }
                AbSheetRow("New label…", onClick = { page = BulkPage.NewLabel }, icon = Icons.Outlined.Add)
                SheetFoot("A label is the coloured dot, and each task has one: this replaces the label each of them has.")
            }

            BulkPage.NewLabel -> NewLabelForm(scopeView, canKeepHere = app.sync.isJoined, onAdd = { name, here, shared -> if (controller.createLabel(name, ids, here, shared)) toOptions() })

            BulkPage.Tags -> {
                var typed by remember { mutableStateOf("") }
                val add = { tag: String ->
                    if (tag.isNotBlank()) controller.addTagAll(ids, tag)
                    typed = ""
                }
                Column(Modifier.padding(horizontal = 24.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    Text("A tag is added to each of them; the tags they have stay.", style = Ab.type.small, color = colors.muted)
                    SheetField(value = typed, onValueChange = { typed = it }, placeholder = "Add a tag", onDone = { add(typed) }, singleLine = true)
                    // ADR 89: "Shared" first, then "This device"; another device's device-only tags never show.
                    scopeView.tagGroups(scopeView.knownTags()).forEach { group ->
                        ScopeGroupHead(group)
                        FlowRow(Modifier.padding(horizontal = 24.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            group.items.forEach { tag -> ShelfChip("#$tag", selected = tasks.all { TaskLabels.has(it.tags, tag) }, height = 36) { add(tag) } }
                        }
                    }
                }
            }

            BulkPage.Selected -> {
                tasks.forEach { task ->
                    AbSheetRow(
                        task.title, onClick = { onUnselect(task.id); if (tasks.size <= 1) onDismiss() }, subtitle = com.accountabilibuddy.shared.tasks.TaskBins.place(task.bin),
                        trailing = { Icon(Icons.Outlined.Close, contentDescription = "Unselect", tint = colors.muted) },
                    )
                }
            }
        }
    }
}
