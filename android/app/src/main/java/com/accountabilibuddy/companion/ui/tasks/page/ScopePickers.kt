package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Devices
import androidx.compose.material.icons.outlined.PhoneAndroid
import androidx.compose.material3.Icon
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
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbSwitch
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.tasks.SheetField
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.tasks.scopeDeviceName
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.ScopeGroup
import com.accountabilibuddy.shared.tasks.TaskLabel
import com.accountabilibuddy.shared.tasks.TaskScopeRules
import com.accountabilibuddy.shared.tasks.TaskScopeView

/**
 * ADR 89 (LB1c): the label and tag pickers group what this phone shows under "Shared" then "This device". Icons (the PC's LB1b may differ;
 * reconciled later): Shared = Devices (Icons.Outlined.Devices), This device = PhoneAndroid.
 */
@Composable
fun ScopeGroupHead(group: ScopeGroup<*>) {
    Row(
        Modifier.fillMaxWidth().padding(start = 24.dp, end = 24.dp, top = 12.dp, bottom = 2.dp).semantics { heading() },
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Icon(if (group.deviceOnly) Icons.Outlined.PhoneAndroid else Icons.Outlined.Devices, contentDescription = null, tint = Ab.colors.muted, modifier = Modifier.size(16.dp))
        Text(group.title, style = Ab.type.small, color = Ab.colors.muted)
    }
}

/** The label picker's rows: [view]'s labels in two groups. Each row is drawn by [row]. */
@Composable
fun LabelGroups(view: TaskScopeView, row: @Composable (TaskLabel) -> Unit) {
    view.labelGroups().forEach { group ->
        ScopeGroupHead(group)
        group.items.forEach { row(it) }
    }
}

/**
 * The New label form (ADR 89): a name, "Only on this phone" (off: a new label is shared) when this phone is in a group, then Add. A name that
 * exists on other devices only isn't refused: the note says which, Add joins it (this phone shows it with them), and "Make shared" is the
 * explicit way to show it everywhere, which asks first ([TaskScopeRules.MAKE_SHARED_ASK]).
 */
@Composable
fun NewLabelForm(view: TaskScopeView, canKeepHere: Boolean, onAdd: (name: String, onlyHere: Boolean, makeShared: Boolean) -> Unit) {
    val app = LocalContext.current.applicationContext as CompanionApp
    var name by remember { mutableStateOf("") }
    var onlyHere by remember { mutableStateOf(false) }
    var ask by remember { mutableStateOf(com.accountabilibuddy.shared.tasks.ScopeAsk<String>()) }
    val exists = view.labels.any { it.name.trim().equals(name.trim(), ignoreCase = true) }
    val elsewhere = if (name.isBlank()) null else view.hiddenNameDevices(name) { scopeDeviceName(app, it) }
    Column(Modifier.padding(horizontal = 24.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        SheetField(value = name, onValueChange = { name = it.take(40) }, placeholder = "Label name", onDone = { if (name.isNotBlank() && !exists) onAdd(name, onlyHere, false) }, singleLine = true)
        Text(
            when {
                exists -> "You already have a label with that name."
                elsewhere != null -> TaskScopeRules.joinNote(name, elsewhere)
                else -> "It gets the first colour none of your labels uses. Change it in Settings › Tasks › Labels."
            },
            style = Ab.type.small, color = Ab.colors.muted,
        )
        if (canKeepHere && elsewhere == null) {
            AbSheetRow(
                TaskScopeRules.ONLY_ON_THIS_PHONE, onClick = { onlyHere = !onlyHere }, role = Role.Switch, subtitle = "Other devices won't show it",
                trailing = { AbSwitch(onlyHere) },
            )
        }
        AbPrimaryButton("Add", onClick = { onAdd(name, onlyHere, false) }, enabled = name.isNotBlank() && !exists, modifier = Modifier.fillMaxWidth())
        if (elsewhere != null && !exists) AbTextButton(TaskScopeRules.MAKE_SHARED, onClick = { ask = ask.ask(name.trim()) })
    }
    if (ask.asking) {
        AbDialog(
            title = "Make '${ask.confirmed()}' shared?", onDismiss = { ask = ask.dismissed() }, confirmText = TaskScopeRules.MAKE_SHARED,
            onConfirm = { val asked = ask.confirmed(); ask = ask.dismissed(); if (asked != null) onAdd(asked, false, true) }, dismissText = "Keep",
        ) { Text(TaskScopeRules.MAKE_SHARED_ASK + ".", style = Ab.type.body, color = Ab.colors.ink) }
    }
}
