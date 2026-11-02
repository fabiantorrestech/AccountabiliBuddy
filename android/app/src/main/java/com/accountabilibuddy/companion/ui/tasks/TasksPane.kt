package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.runtime.remember
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbChipRow
import com.accountabilibuddy.companion.ui.kit.AbInfoBanner
import com.accountabilibuddy.companion.ui.kit.AbPageHeader
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The Tasks tab's list (board 5v): the title and a quiet magnifier, the category chips, then the groups This session, Come back
 * to, Later (folded) and Done today (folded), and the "Add a task" field pinned at the bottom. It only draws what [model]
 * holds and forwards taps through [actions]; the host owns the state and decides what each does. It has no Scaffold and no fixed
 * width, so a tablet can put the list and the detail side by side later.
 *
 * @param pc The PC with a running session, or null (This session is then the next session's list on this phone).
 * @param comebackOldPc The name of an older PC (before shared lists, ADR 48) whose own come-backs are shown; null normally.
 * @param category The chip picked; null is All.
 * @param addPlaceholder The words in the add field, which also say why it can't be used now.
 */
@Composable
fun TasksPane(
    model: TasksModel,
    pc: TasksPc?,
    comebackOldPc: String?,
    category: String?,
    onCategory: (String?) -> Unit,
    search: String,
    onSearch: (String) -> Unit,
    searching: Boolean,
    onSearching: (Boolean) -> Unit,
    laterOpen: Boolean,
    onLaterOpen: (Boolean) -> Unit,
    doneOpen: Boolean,
    onDoneOpen: (Boolean) -> Unit,
    actions: TasksPaneActions,
    addPlaceholder: String,
    addEnabled: Boolean,
    onAdd: (String) -> Unit,
    onAddLater: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    val filtering = category != null || search.isNotBlank()
    Column(modifier.fillMaxSize().imePadding()) {
        LazyColumn(
            Modifier.weight(1f),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 12.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            item {
                AbPageHeader("Tasks") {
                    AbQuietIconButton(
                        if (searching) Icons.Outlined.Close else Icons.Outlined.Search,
                        if (searching) "Close search" else "Search tasks",
                        onClick = { onSearching(!searching); if (searching) onSearch("") },
                    )
                }
            }
            if (searching) item { SearchField(search, onSearch) }
            item {
                AbChipRow {
                    AbChip("All", selected = category == null, onClick = { onCategory(null) })
                    model.categories.forEach { choice ->
                        AbChip(choice.name, selected = category.equals(choice.name, ignoreCase = true), onClick = { onCategory(choice.name) })
                    }
                }
            }
            comebackOldPc?.let { name ->
                item { AbInfoBanner("$name has an older version, so these lists don't reach it. Update AccountabiliBuddy on it.", icon = Icons.Outlined.Info) }
            }
            if (pc != null) {
                item { TasksCard(model.session, model.sessionTotal, pc, filtering, actions) }
            } else {
                item { NextTasksCard(model.session, model.sessionTotal, filtering, actions) }
            }
            if (model.comeBacks.isNotEmpty() || !filtering) {
                item { ComebacksCard(model.comeBacks, comebackOldPc, filtering, pc, actions) }
            }
            if (model.laterTotal > 0 || !filtering) {
                item {
                    LaterGroup(
                        rows = model.later, total = model.laterTotal, nextDue = model.nextDue, expanded = laterOpen, onToggle = { onLaterOpen(!laterOpen) },
                        pc = pc, filtering = filtering, actions = actions, onAdd = onAddLater,
                    )
                }
            }
            if (model.doneToday.isNotEmpty()) {
                item { DoneTodayGroup(model.doneToday, model.doneAllToday, expanded = doneOpen, onToggle = { onDoneOpen(!doneOpen) }, actions = actions) }
            }
        }
        AddField(
            placeholder = addPlaceholder, onSubmit = onAdd, accentPlus = true, enabled = addEnabled,
            modifier = Modifier.padding(start = 16.dp, end = 16.dp, bottom = 12.dp, top = 4.dp),
        )
    }
}

/** The search box under the title: it finds titles, steps and notes in what is already loaded, and keeps the text in memory only. */
@Composable
private fun SearchField(text: String, onChange: (String) -> Unit) {
    val colors = Ab.colors
    val focus = remember { FocusRequester() }
    val keyboard = LocalSoftwareKeyboardController.current
    LaunchedEffect(Unit) { focus.requestFocus() }
    Surface(Modifier.fillMaxWidth().heightIn(min = 48.dp), shape = Ab.shapes.pill, color = colors.surface2) {
        Row(Modifier.padding(horizontal = 16.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Icon(Icons.Outlined.Search, contentDescription = null, tint = colors.muted, modifier = Modifier.size(20.dp))
            Box(Modifier.weight(1f).padding(vertical = 10.dp), contentAlignment = Alignment.CenterStart) {
                if (text.isEmpty()) Text("Search titles, steps and notes", style = Ab.type.body, color = colors.faint, maxLines = 1)
                BasicTextField(
                    value = text, onValueChange = onChange, singleLine = true, modifier = Modifier.fillMaxWidth().focusRequester(focus),
                    textStyle = Ab.type.body.copy(color = colors.ink), cursorBrush = SolidColor(colors.ink),
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search), keyboardActions = KeyboardActions(onSearch = { keyboard?.hide() }),
                )
            }
        }
    }
}
