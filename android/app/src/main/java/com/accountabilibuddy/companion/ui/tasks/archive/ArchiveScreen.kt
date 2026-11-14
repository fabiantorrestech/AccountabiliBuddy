package com.accountabilibuddy.companion.ui.tasks.archive

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.DeleteOutline
import androidx.compose.material.icons.outlined.EditNote
import androidx.compose.material.icons.outlined.ExpandLess
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material.icons.outlined.MoreVert
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material.icons.outlined.StickyNote2
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.runtime.collectAsState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbPageHeader
import com.accountabilibuddy.companion.ui.kit.AbPageHeaderDefaults
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.tasks.TasksPc
import com.accountabilibuddy.companion.ui.tasks.page.ShelfChip
import com.accountabilibuddy.companion.ui.tasks.page.ShelfController
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.ArchiveFilter
import com.accountabilibuddy.shared.tasks.ArchiveItem
import com.accountabilibuddy.shared.tasks.ArchiveKind
import com.accountabilibuddy.shared.tasks.ArchiveList
import com.accountabilibuddy.shared.tasks.ArchiveUiRules
import java.time.Instant
import java.time.ZoneId

/**
 * The phone's full Archive (AR1c, ADR 87): the PC's Notebook on the phone, the same lines in the same order ([ArchiveList] over [ArchiveRows] from the
 * shared builder): come-backs, every device's session notes ("from Work PC"), the tasks left unfinished and the tasks done on every PC and the phone,
 * filters and search as on the PC, and Recently deleted with Restore. A tick, untick or delete of a past task is a settle record (never a raw edit of
 * its owner's history); a note's edit or delete is stamped and reaches every device. Note and task text is shown here and never logged.
 */
@Composable
fun ArchiveScreen(pc: TasksPc?, now: Instant, controller: ShelfController, onBack: () -> Unit) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val zone = ZoneId.systemDefault()
    val colors = Ab.colors
    BackHandler(onBack = onBack)

    val data = rememberArchiveData(app, pc, now, zone)
    var filter by rememberSaveable { mutableStateOf(ArchiveFilter.EVERYTHING.name) }
    var search by rememberSaveable { mutableStateOf("") }
    var searching by rememberSaveable { mutableStateOf(false) }
    var deletedOpen by rememberSaveable { mutableStateOf(false) }
    val chosen = ArchiveFilter.valueOf(filter)
    var menuFor by remember { mutableStateOf<ArchiveItem?>(null) }
    var editing by remember { mutableStateOf<ArchiveItem?>(null) }
    var confirming by remember { mutableStateOf<ArchiveItem?>(null) }

    val shown = remember(data.items, chosen, search) { ArchiveList.shown(data.items, chosen, search) }
    val deleted = remember(data.rows, search, zone) { ArchiveList.deleted(data.rows, search) { ArchiveList.whenText(it, zone) } }
    val showsDeleted = ArchiveList.showsDeleted(deletedOpen, search)

    // The page takes every touch that reaches it: a tap in a blank spot must not fall through to the Tasks page under it (its header buttons, its rows).
    val rowKeys = remember(shown) { ArchiveUiRules.rowKeys(shown) }
    val deletedKeys = remember(deleted) { ArchiveUiRules.uniqueKeys(deleted.map { "deleted:${it.row.id}" }) }
    Column(Modifier.fillMaxSize().background(colors.bg).pointerInput(Unit) { awaitEachGesture { awaitFirstDown(requireUnconsumed = false) } }) {
        Box(Modifier.padding(horizontal = AbPageHeaderDefaults.PaneEdge)) {
            AbPageHeader(
                "Archive",
                subtitle = { Text(ArchiveList.summary(data.items), style = Ab.type.small, color = colors.muted, maxLines = 1, overflow = TextOverflow.Ellipsis) },
                actions = {
                    HeaderButton(Icons.AutoMirrored.Outlined.ArrowBack, "Back to Tasks", onBack)
                    HeaderButton(if (searching) Icons.Outlined.Close else Icons.Outlined.Search, if (searching) "Close search" else "Search the Archive") {
                        if (searching) search = ""
                        searching = !searching
                    }
                },
            )
        }
        if (searching) {
            SearchField(search) { search = it }
        } else {
            Row(Modifier.fillMaxWidth().height(48.dp).horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                ArchiveFilter.entries.forEach { option -> ShelfChip(option.label, option == chosen) { filter = option.name } }
            }
        }

        LazyColumn(Modifier.weight(1f).fillMaxWidth()) {
            if (shown.isEmpty()) {
                item { EmptyNote(if (search.isBlank()) "Nothing here yet." else "Nothing in the Archive matches that.") }
            }
            itemsIndexed(shown, key = { index, _ -> rowKeys[index] }) { _, item ->
                ArchiveRow(
                    item,
                    onTick = {
                        ArchiveUiRules.tick(
                            item.kind, item.isTicked, item.id,
                            tickParked = app.sharedLists::tickParked,
                            untickParked = app.sharedLists::untickParked,
                            tickPast = app.sharedLists::tickPast,
                            untickPast = app.sharedLists::untickPast,
                        )
                    },
                    onMenu = { menuFor = item },
                )
            }
            item(key = "deleted-head") {
                DeletedHeader(ArchiveList.deletedHeader(deleted.size), open = showsDeleted, onToggle = { deletedOpen = !deletedOpen })
            }
            if (showsDeleted) {
                if (deleted.isEmpty()) item(key = "deleted-none") { EmptyNote("Nothing deleted in the last 30 days.") }
                itemsIndexed(deleted, key = { index, _ -> deletedKeys[index] }) { _, line ->
                    val row = line.row
                    DeletedRow(line.text, row.title, canRestore = row.canRestore || row.canRestoreByStamp) {
                        // A task of the one task list comes back through its History entry; a past task through a settle record stamped over the delete.
                        val entry = row.entryId
                        if (entry != null) controller.bringBack(entry) else row.restoreAs?.let { app.sharedLists.restorePast(row.id, it) }
                    }
                }
            }
            item(key = "end") { Box(Modifier.height(24.dp)) }
        }
    }

    menuFor?.let { item ->
        AbSheet(title = item.text.take(60), subtitle = item.whenText, onDismiss = { menuFor = null }, height = AbSheetHeight.Half) {
            if (item.canEdit && !item.isStep && (item.kind == ArchiveKind.NOTE || item.kind == ArchiveKind.COMEBACK)) {
                AbSheetRow("Edit", onClick = { editing = item; menuFor = null }, icon = Icons.Outlined.EditNote)
            }
            if (item.canDelete && !item.isStep) {
                AbSheetRow("Delete", onClick = { menuFor = null; if (ArchiveUiRules.deleteNeedsConfirm(item)) confirming = item else deleteItem(app, item) }, icon = Icons.Outlined.DeleteOutline, danger = true)
            }
            if (!item.canEdit && item.editHint != null) {
                Text(item.editHint!!, style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(horizontal = 24.dp, vertical = 10.dp))
            }
        }
    }

    editing?.let { item ->
        var text by remember(item.id) { mutableStateOf(item.text) }
        AbSheet(title = if (item.kind == ArchiveKind.NOTE) "Edit note" else "Edit come-back", onDismiss = { editing = null }, height = AbSheetHeight.Half, aboveKeyboard = true) {
            Column(Modifier.padding(horizontal = 24.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                AbTextField(text, { text = it }, label = if (item.kind == ArchiveKind.NOTE) "Note" else "Come-back", singleLine = false, modifier = Modifier.fillMaxWidth())
                item.editWarning?.let { Text(it, style = Ab.type.small, color = colors.muted) }
                AbPrimaryButton("Save", onClick = {
                    if (item.kind == ArchiveKind.NOTE) app.sessionNotes.edit(item.id, text) else app.sharedLists.editParked(item.id, text)
                    editing = null
                }, enabled = text.isNotBlank())
            }
        }
    }

    confirming?.let { item ->
        AbDialog(
            title = ArchiveUiRules.deleteTitle(item),
            onDismiss = { confirming = null },
            confirmText = "Delete",
            onConfirm = {
                deleteItem(app, item)
                confirming = null
            },
            dismissText = "Keep it",
        ) {
            Text(item.deleteWarning ?: "It goes at once.", style = Ab.type.bodyMedium, color = colors.muted)
        }
    }
}

// A note's delete (every device), a come-back's, or a past task's: a settle record (another PC's task, on every device) or the keeper's removal (one kept
// here); the owner's history is never edited.
private fun deleteItem(app: CompanionApp, item: ArchiveItem) = ArchiveUiRules.delete(
    item,
    deleteNote = app.sessionNotes::delete,
    deleteComeback = app.sharedLists::deleteParked,
    deletePast = app.sharedLists::deletePast,
)

@Composable
private fun HeaderButton(icon: androidx.compose.ui.graphics.vector.ImageVector, description: String, onClick: () -> Unit) {
    val colors = Ab.colors
    Box(
        Modifier.size(48.dp).clip(CircleShape).clickable(role = Role.Button, onClick = onClick).semantics { contentDescription = description },
        contentAlignment = Alignment.Center,
    ) { Icon(icon, contentDescription = null, tint = colors.ink, modifier = Modifier.size(22.dp)) }
}

@Composable
private fun SearchField(text: String, onChange: (String) -> Unit) {
    val colors = Ab.colors
    val focus = remember { FocusRequester() }
    val keyboard = LocalSoftwareKeyboardController.current
    LaunchedEffect(Unit) { runCatching { focus.requestFocus() } }
    Row(
        Modifier.fillMaxWidth().height(48.dp).padding(horizontal = 16.dp, vertical = 5.dp).background(colors.surface2, CircleShape).padding(horizontal = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Icon(Icons.Outlined.Search, contentDescription = null, tint = colors.muted, modifier = Modifier.size(18.dp))
        Box(Modifier.weight(1f), contentAlignment = Alignment.CenterStart) {
            if (text.isEmpty()) Text("Search come-backs, notes and tasks", style = Ab.type.bodyMedium, color = colors.faint, maxLines = 1)
            BasicTextField(
                value = text, onValueChange = onChange, singleLine = true, modifier = Modifier.fillMaxWidth().focusRequester(focus).semantics { contentDescription = "Search the Archive" },
                textStyle = Ab.type.bodyMedium.copy(color = colors.ink), cursorBrush = SolidColor(colors.ink),
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search), keyboardActions = KeyboardActions(onSearch = { keyboard?.hide() }),
            )
        }
    }
}

@Composable
private fun EmptyNote(text: String) {
    Text(text, style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 24.dp, vertical = 16.dp))
}

// One Archive line: a tick box (come-backs and tasks; a step shows the state only, a note has its note mark), the text and "Session of Tue 11/12
// 4:05 PM · done Tue 11/12 5:00 PM · on Work PC" under it, and a ⋮ when it has something to do. 56 dp at least, all touch targets 48 dp or more.
@Composable
private fun ArchiveRow(item: ArchiveItem, onTick: () -> Unit, onMenu: () -> Unit) {
    val colors = Ab.colors
    val hasMenu = !item.isStep && (item.canDelete || item.canEdit || item.editHint != null)
    Row(
        Modifier.fillMaxWidth().heightIn(min = 56.dp).padding(start = if (item.isStep) 44.dp else 16.dp, end = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        when {
            item.kind == ArchiveKind.NOTE -> Box(Modifier.size(48.dp), contentAlignment = Alignment.Center) {
                Icon(Icons.Outlined.StickyNote2, contentDescription = "Note", tint = colors.muted, modifier = Modifier.size(22.dp))
            }

            item.canTick -> TickTarget(item.isTicked, onTick)
            else -> Box(Modifier.size(48.dp), contentAlignment = Alignment.Center) {
                Box(Modifier.size(10.dp).background(if (item.isTicked) colors.met else colors.faint, CircleShape))
            }
        }
        Column(Modifier.weight(1f).padding(vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(item.text, style = Ab.type.bodyMedium, color = colors.ink, maxLines = 6, overflow = TextOverflow.Ellipsis)
            Text(item.whenText, style = Ab.type.small, color = colors.muted)
            item.editHint?.let { if (!item.isStep) Text(it, style = Ab.type.micro, color = colors.faint) }
        }
        if (hasMenu) {
            Box(
                Modifier.size(48.dp).clip(CircleShape).clickable(role = Role.Button, onClick = onMenu).semantics { contentDescription = "More for ${item.kind.label}" },
                contentAlignment = Alignment.Center,
            ) { Icon(Icons.Outlined.MoreVert, contentDescription = null, tint = colors.muted, modifier = Modifier.size(22.dp)) }
        } else {
            Box(Modifier.size(48.dp))
        }
    }
}

// The row's 24 dp square tick in a 48 dp touch target (the Tasks page's TickBox look: a faint 2 dp border; filled with a check when ticked).
@Composable
private fun TickTarget(checked: Boolean, onTick: () -> Unit) {
    val colors = Ab.colors
    val shape = androidx.compose.foundation.shape.RoundedCornerShape(7.dp)
    Box(
        Modifier.size(48.dp).clickable(role = Role.Checkbox, onClick = onTick).semantics { contentDescription = if (checked) "Done. Tap to open it again" else "Mark done" },
        contentAlignment = Alignment.Center,
    ) {
        Box(
            Modifier.size(24.dp).clip(shape).then(if (checked) Modifier.background(colors.met, shape) else Modifier.border(2.dp, colors.faint, shape)),
            contentAlignment = Alignment.Center,
        ) {
            if (checked) Icon(Icons.Outlined.Check, contentDescription = null, tint = colors.bg, modifier = Modifier.size(16.dp))
        }
    }
}

@Composable
private fun DeletedHeader(text: String, open: Boolean, onToggle: () -> Unit) {
    val colors = Ab.colors
    Row(
        Modifier.fillMaxWidth().heightIn(min = 56.dp).clickable(role = Role.Button, onClick = onToggle).padding(horizontal = 24.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(text, style = Ab.type.bodyMedium, color = colors.muted, modifier = Modifier.weight(1f).semantics { heading() })
        Icon(if (open) Icons.Outlined.ExpandLess else Icons.Outlined.ExpandMore, contentDescription = if (open) "Fold Recently deleted" else "Open Recently deleted", tint = colors.muted, modifier = Modifier.size(24.dp))
    }
}

@Composable
private fun DeletedRow(whenText: String, title: String, canRestore: Boolean, onRestore: () -> Unit) {
    val colors = Ab.colors
    Row(Modifier.fillMaxWidth().heightIn(min = 56.dp).padding(start = 24.dp, end = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f).padding(vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(title, style = Ab.type.bodyMedium, color = colors.ink, maxLines = 3, overflow = TextOverflow.Ellipsis)
            Text(whenText, style = Ab.type.small, color = colors.muted)
        }
        if (canRestore) {
            Box(
                Modifier.heightIn(min = 48.dp).clip(CircleShape).clickable(role = Role.Button, onClick = onRestore).padding(horizontal = 14.dp).semantics { contentDescription = "Restore $title" },
                contentAlignment = Alignment.Center,
            ) { Text("Restore", style = Ab.type.bodyMedium, color = colors.ink) }
        }
    }
}
