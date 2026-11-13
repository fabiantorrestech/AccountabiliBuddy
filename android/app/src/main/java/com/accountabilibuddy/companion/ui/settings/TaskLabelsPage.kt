package com.accountabilibuddy.companion.ui.settings

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.tasks.TaskSettingsActions
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbListRow
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.tasks.page.LabelDot
import com.accountabilibuddy.companion.ui.tasks.page.labelColor
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.TaskLabel
import com.accountabilibuddy.shared.tasks.TaskLabelColors
import com.accountabilibuddy.shared.tasks.TaskLabelEdits
import com.accountabilibuddy.shared.tasks.TaskLabelNameCheck
import com.accountabilibuddy.shared.tasks.TaskLabelWords
import com.accountabilibuddy.shared.tasks.TaskRingDevices

/**
 * The Labels screen (R7.4; the PC's Labels screen): a row per label with its colour dot, "3 tasks · rings: all devices", a floating +
 * for a new label. A row opens the label's sheet: its name, its colour (12 swatches, a typed colour, an opacity from 20 to 100 %) and
 * "Rings on" per phone (PCs always ring for their own reminders); Delete asks first and leaves the label's tasks with No label.
 */
@Composable
fun TaskLabelsPage() {
    val app = LocalContext.current.applicationContext as CompanionApp
    val actions = remember { TaskSettingsActions(app) }
    val lists by app.taskModel.lists.collectAsStateWithLifecycle()
    val labels = lists.labels.map { it.label }.filter { it.isLive }.sortedBy { it.position }
    var editing by rememberSaveable { mutableStateOf<String?>(null) }
    var adding by rememberSaveable { mutableStateOf(false) }
    val phones = actions.phones()

    Box(Modifier.fillMaxSize()) {
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 96.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            item {
                AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 10.dp), verticalSpacing = 0.dp) {
                    AbSectionLabel("${labels.size} labels", Modifier.padding(start = 4.dp, bottom = 4.dp))
                    if (labels.isEmpty()) {
                        Text("No labels yet. Tap + to add one.", style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(4.dp))
                    }
                    labels.forEachIndexed { index, label ->
                        AbListRow(
                            label.name, subtitle = TaskLabelWords.subtitle(TaskLabelEdits.useCount(lists, label.id), label.ringOn, phones.size),
                            onClick = { editing = label.id }, divider = index < labels.lastIndex,
                            trailing = { LabelDot(label.color, 14.dp) },
                        )
                    }
                }
            }
        }
        FloatingActionButton(
            onClick = { adding = true }, modifier = Modifier.align(Alignment.BottomEnd).padding(20.dp),
            containerColor = Ab.colors.ink, contentColor = Ab.colors.bg,
        ) { Icon(Icons.Outlined.Add, contentDescription = "New label") }
    }

    if (adding) LabelSheet(null, lists, actions, phones, onDismiss = { adding = false })
    editing?.let { id ->
        val label = labels.firstOrNull { it.id == id }
        if (label == null) editing = null else LabelSheet(label, lists, actions, phones, onDismiss = { editing = null })
    }
}

// The New label / label sheet. A new label is made on Add; an existing one saves with Save (name and colour) and its rings switches at once.
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun LabelSheet(label: TaskLabel?, lists: com.accountabilibuddy.shared.tasks.TaskLists, actions: TaskSettingsActions, phones: List<com.accountabilibuddy.companion.tasks.RingPhone>, onDismiss: () -> Unit) {
    val colors = Ab.colors
    val startColor = label?.color ?: TaskLabelColors.firstUnused(lists.labels.map { it.label })
    var name by rememberSaveable(label?.id) { mutableStateOf(label?.name ?: "") }
    var base by rememberSaveable(label?.id) { mutableStateOf(TaskLabelColors.opaque(startColor)) }
    var typed by rememberSaveable(label?.id) { mutableStateOf("") }
    var opacity by remember(label?.id) { mutableFloatStateOf(TaskLabelColors.opacityOf(startColor).toFloat()) }
    var deleting by rememberSaveable { mutableStateOf(false) }
    val color = TaskLabelColors.withOpacity(base, opacity.toDouble())
    val check = TaskLabelEdits.checkName(lists, label?.id, name)
    val typedOk = typed.isBlank() || TaskLabelColors.typed(typed) != null

    AbSheet(title = if (label == null) "New label" else label.name, subtitle = label?.let { TaskLabelWords.uses(TaskLabelEdits.useCount(lists, it.id)) }, onDismiss = onDismiss, height = AbSheetHeight.Tall) {
        Column24 {
            AbTextField(name, { name = it.take(40) }, "Label name", Modifier.fillMaxWidth(), isError = check == TaskLabelNameCheck.Taken)
            if (check == TaskLabelNameCheck.Taken) Text("You already have a label with that name.", style = Ab.type.small, color = colors.drift)
            AbSectionLabel("Colour")
            FlowRow(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                TaskLabelColors.SET.forEach { swatch ->
                    val selected = TaskLabelColors.sameOpaque(swatch, base)
                    Box(
                        Modifier.size(36.dp).background(labelColor(swatch) ?: Color.Gray, CircleShape)
                            .then(if (selected) Modifier.border(3.dp, colors.ink, CircleShape) else Modifier)
                            .clickable(role = Role.RadioButton) { base = swatch; typed = "" }
                            .semantics { contentDescription = "Colour $swatch" + if (selected) ", chosen" else "" },
                    )
                }
            }
            AbTextField(
                typed, { typed = it.take(24); TaskLabelColors.typed(it)?.let { ok -> base = TaskLabelColors.opaque(ok); opacity = TaskLabelColors.opacityOf(ok).toFloat() } },
                "Your own colour (#RRGGBB or rgba)", Modifier.fillMaxWidth(), isError = !typedOk, capitalization = androidx.compose.ui.text.input.KeyboardCapitalization.None,
            )
            AbSectionLabel("Opacity ${(opacity * 100).toInt()}%")
            Slider(
                value = opacity, onValueChange = { opacity = (Math.round(it * 20) / 20f).coerceIn(TaskLabelColors.MIN_OPACITY.toFloat(), 1f) },
                valueRange = TaskLabelColors.MIN_OPACITY.toFloat()..1f,
                colors = SliderDefaults.colors(thumbColor = colors.ink, activeTrackColor = colors.ink, inactiveTrackColor = colors.surface2),
            )
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Box(Modifier.size(28.dp).background((labelColor(base) ?: Color.Gray).copy(alpha = opacity), CircleShape))
                Text(name.ifBlank { "Label" }, style = Ab.type.body, color = colors.ink)
            }
            AbPrimaryButton(
                if (label == null) "Add" else "Save", modifier = Modifier.fillMaxWidth(), enabled = check == TaskLabelNameCheck.Fine && typedOk,
                onClick = {
                    val ok = if (label == null) actions.addLabel(name, color) else actions.changeLabel(label.id, name, color)
                    if (ok) onDismiss()
                },
            )
        }
        if (label != null) {
            AbSectionLabel("Rings on", Modifier.padding(horizontal = 24.dp, vertical = 8.dp))
            val pcs = actions.pcNames()
            pcs.forEach { pc -> AbSheetRow(pc, onClick = null, subtitle = "A PC always rings for its own reminders") }
            phones.forEach { phone ->
                val on = TaskRingDevices.rings(label.ringOn, phone.id)
                AbSheetRow(
                    phone.name, onClick = { actions.setLabelRing(label.id, phone.id, !on) }, role = Role.Switch,
                    subtitle = if (on) "Rings for this label's reminders" else "Doesn't ring for this label",
                    trailing = { com.accountabilibuddy.companion.ui.kit.AbSwitch(on) },
                )
            }
            Text(
                "A phone rings for a task's reminder only when this list and the account's list (Settings › Tasks › Ringing) both name it.",
                style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(horizontal = 24.dp, vertical = 6.dp),
            )
            AbSheetRow("Delete label", onClick = { deleting = true }, danger = true)
        }
    }

    if (deleting && label != null) {
        val uses = TaskLabelEdits.useCount(lists, label.id)
        AbDialog(
            title = "Delete ${label.name}?", onDismiss = { deleting = false }, confirmText = if (uses == 0) "Delete" else "Delete · $uses tasks become No label",
            onConfirm = { deleting = false; actions.deleteLabel(label.id); onDismiss() }, dismissText = "Keep",
        ) { Text("Its tasks stay; they just have no label.", style = Ab.type.body, color = colors.ink) }
    }
}

@Composable
private fun Column24(content: @Composable androidx.compose.foundation.layout.ColumnScope.() -> Unit) {
    androidx.compose.foundation.layout.Column(Modifier.padding(horizontal = 24.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(12.dp), content = content)
}
