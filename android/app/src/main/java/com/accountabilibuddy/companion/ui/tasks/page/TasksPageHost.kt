package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.BookmarkBorder
import androidx.compose.material.icons.outlined.CheckCircle
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Inbox
import androidx.compose.material.icons.outlined.MoveToInbox
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material.icons.outlined.Start
import androidx.compose.material.icons.outlined.Today
import androidx.compose.material.icons.outlined.Tune
import androidx.compose.material3.Icon
import androidx.compose.material3.SnackbarDuration
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.SnackbarResult
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.tasks.TasksUiPrefs
import com.accountabilibuddy.companion.ui.UiPrefs
import com.accountabilibuddy.companion.ui.kit.AbPageHeader
import com.accountabilibuddy.companion.ui.kit.AbPageHeaderDefaults
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.tasks.TasksPc
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.tasks.ShelfPage
import com.accountabilibuddy.shared.tasks.ShelfPages
import com.accountabilibuddy.shared.tasks.ShelfPaging
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskBins
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskListUpdateNotice
import com.accountabilibuddy.shared.tasks.TaskRowText
import com.accountabilibuddy.shared.tasks.TaskViewConfig
import com.accountabilibuddy.shared.tasks.TodayStripCounts
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.time.OffsetDateTime
import java.time.ZoneOffset

/**
 * The phone's Tasks tab (round 2 R7.3; boards Tasks-Phone, -Label, -Session; brief §4.5): planning only. From the top, each at a
 * fixed place whatever loads or changes (stable layout, brief §3.1): the header "Tasks" with Views, Options and Search (no gear); the
 * **Today strip** (counts, opens the Today tab, the drop target into Today › Queued); the chip row (or the search field in its
 * place); then the Shelf **one page at a time** ([ShelfPager]: a page per bin, label or tag with peeks of the neighbours and the page
 * indicator), or, while a session runs, the hidden-Shelf card; and the "+" button.
 *
 * It reads the phone's one task list and what this device remembers ([TasksUiPrefs]: the view in use, and per grouping the page
 * order, start page and wrap around) and changes tasks only through [ShelfController]. The search text and an opened task live in
 * memory only. Tasks always opens on the start page, and a Shelf shown during a session hides again when Tasks is left (neither is
 * kept when the tab goes away).
 *
 * @param controlPc The PC this phone controls, to know whether a session runs.
 */
@Composable
fun TasksPageHost(controlPc: ReceivedDevice?, snackbar: SnackbarHostState, onOpenToday: () -> Unit, onOpenSettings: () -> Unit) {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val scope = rememberCoroutineScope()
    val prefs = remember { TasksUiPrefs.of(context) }
    val state by prefs.state.collectAsState()
    val lists by app.taskModel.lists.collectAsState()
    val received by app.sync.received.collectAsState()
    val motion by UiPrefs.motion.collectAsState()
    // Looked at again every half minute: "Overdue", "untouched 4 wk" and a session that ended on the PC follow the clock.
    val now by produceState(DotNetTime.cut(OffsetDateTime.now(ZoneOffset.UTC))) {
        while (true) {
            delay(30_000)
            value = DotNetTime.cut(OffsetDateTime.now(ZoneOffset.UTC))
        }
    }
    val zone = app.taskModel.zone()
    val self = remember { app.sync.deviceId.toString() }
    val pc = TasksPc.of(controlPc, received, now.toInstant(), app.runningTaskEdits.isAvailable)

    var searching by remember { mutableStateOf(false) }
    var search by remember { mutableStateOf("") }
    var chipLabel by remember { mutableStateOf<String?>(null) }
    var chipBin by remember { mutableStateOf<TaskBin?>(null) }
    var currentKey by remember { mutableStateOf<String?>(null) }
    var lastIndex by remember { mutableIntStateOf(0) }
    var shelfShown by remember { mutableStateOf(false) }
    var expanded by remember { mutableStateOf<String?>(null) }
    var menu by remember { mutableStateOf<Pair<String, TaskMenuPage>?>(null) }
    var options by remember { mutableStateOf<OptionsPage?>(null) }
    var adding by remember { mutableStateOf<ShelfPage?>(null) }
    var addingAnywhere by remember { mutableStateOf(false) }
    var newLabel by remember { mutableStateOf(false) }

    val controller = remember(app, snackbar) {
        ShelfController(app) { toast ->
            scope.launch {
                snackbar.currentSnackbarData?.dismiss()
                val result = snackbar.showSnackbar(toast.message, actionLabel = if (toast.undo != null) "Undo" else null, duration = SnackbarDuration.Short)
                if (result == SnackbarResult.ActionPerformed) toast.undo?.invoke()
            }
        }
    }

    val config = state.config
    val grouping = state.grouping
    val byBins = grouping == TaskViewConfig.GROUP_BINS || grouping == null
    val labels = lists.labels.map { it.label }.filter { it.isLive }.sortedBy { it.position }
    val inSession = pc != null
    val pages = remember(lists, config, now, search, state.orders, chipLabel, chipBin, inSession) {
        ShelfPages.build(lists, config, now, zone, search.takeIf { it.isNotBlank() }, state.orderOf(grouping), chipLabel, chipBin, sessionRuns = inSession)
    }
    val keys = pages.map { it.key }
    // The page showing: the one last picked while it exists, else the start page (Tasks opens on it), else the nearest one left.
    val picked = currentKey?.let { keys.indexOf(it) } ?: -1
    val index = when {
        picked >= 0 -> picked
        currentKey == null -> ShelfPaging.startIndex(keys, state.startOf(grouping))
        else -> lastIndex.coerceIn(0, (keys.size - 1).coerceAtLeast(0))
    }
    // A new grouping starts on its own start page.
    LaunchedEffect(grouping) { currentKey = null }
    val showPage: (Int) -> Unit = { to ->
        keys.getOrNull(to)?.let {
            currentKey = it
            lastIndex = to
            expanded = null
        }
    }

    val shelfTasks = lists.tasks.count { it.task.isLive && TaskBins.isShelf(it.task.bin) }
    // During a session the Shelf hides behind "Show the Shelf"; a typed search shows it (search still works, as on the PC).
    val hidden = inSession && state.hideShelfInSession && !shelfShown && search.isBlank()
    val counts = TodayStripCounts.of(lists, now, zone)
    val sessionPart = TodayStripCounts.sessionPart(inSession, pc?.checklist?.doneCount ?: 0, pc?.checklist?.tasks?.size ?: 0, counts.ready)
    val stripText = TodayStripCounts.phoneText(sessionPart, counts.queued, counts.reminders)
    val filtering = search.isNotBlank() || TaskViewWords.filters(config) || chipLabel != null || chipBin != null
    val emptyText = when {
        search.isNotBlank() -> "Nothing matches \"${search.trim()}\"."
        filtering -> "Nothing matches."
        else -> "Nothing to see here."
    }

    Box(Modifier.fillMaxSize()) {
        Column(Modifier.fillMaxSize()) {
            Box(Modifier.padding(horizontal = AbPageHeaderDefaults.PaneEdge)) {
                AbPageHeader("Tasks") {
                    HeaderIcon(Icons.Outlined.BookmarkBorder, "Saved views", active = false) { options = OptionsPage.Views }
                    HeaderIcon(Icons.Outlined.Tune, "Options: view, filter and sort", active = options != null || TaskViewWords.filters(config)) { options = OptionsPage.Main }
                    HeaderIcon(if (searching) Icons.Outlined.Close else Icons.Outlined.Search, if (searching) "Close search" else "Search", active = searching) {
                        if (searching) search = ""
                        searching = !searching
                    }
                }
            }
            TodayStrip(stripText, onOpen = onOpenToday)
            // R7.2 review item 3: a PC older than the one task list ("PC first"): its task edits don't reach this phone (Q583).
            val updateNotice = if (app.sync.sharesTaskLists()) TaskListUpdateNotice.text(TaskListUpdateNotice.pcsWithoutTasks(received.map { it.file })) else null
            updateNotice?.let { line ->
                Text(line, style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = AbPageHeaderDefaults.PaneEdge, vertical = 4.dp))
            }
            // One 48 dp slot: the chips, or the search field in their place, so opening the search moves nothing.
            Box(Modifier.fillMaxWidth().height(ChipRowHeight)) {
                if (searching) {
                    SearchSlot(search, onChange = { search = it })
                } else {
                    ShelfChips(
                        byBins = byBins,
                        caption = if (grouping == TaskViewConfig.GROUP_TAG) "Grouped by tag" else "Grouped by label",
                        labels = labels, chipLabel = chipLabel, onChipLabel = { chipLabel = it },
                        chipBin = chipBin, onChipBin = { chipBin = it }, onNewLabel = { newLabel = true },
                    )
                }
            }
            if (hidden) {
                ShelfHiddenCard(shelfTasks, onShow = { shelfShown = true }, modifier = Modifier.padding(top = 2.dp))
                Spacer(Modifier.weight(1f))
            } else if (pages.isEmpty()) {
                Box(Modifier.weight(1f).fillMaxWidth().padding(start = 16.dp, end = 16.dp, bottom = 26.dp).clip(RoundedCornerShape(24.dp)).background(Ab.colors.surface)) {
                    ShelfEmpty(emptyText, Modifier.fillMaxSize())
                }
            } else {
                ShelfPager(pages, index, wrap = state.wrapOf(grouping), motionScale = motion.scale, onIndex = showPage, modifier = Modifier.weight(1f)) { page ->
                    ShelfPageCard(page, emptyText, onAdd = { adding = page }) { task ->
                        val label = labels.firstOrNull { it.id.equals(task.labelId, ignoreCase = true) }
                        val onLabelPage = page.key.startsWith("label:")
                        ShelfRow(
                            task = task,
                            parts = TaskRowText.detail(task, label?.name, now, zone, showBin = page.bin == null, showLabel = !onLabelPage),
                            labelColor = label?.color,
                            showDot = !onLabelPage,
                            compact = config.density == TaskViewConfig.DENSITY_COMPACT,
                            expanded = expanded == task.id,
                            menuOpen = menu?.first == task.id,
                            swipe = swipeOf(task, controller),
                            onTick = { if (task.isDone) controller.reopen(task.id) else controller.finish(task.id) },
                            onTap = { expanded = if (expanded == task.id) null else task.id },
                            onMenu = { menu = task.id to TaskMenuPage.Main },
                            onHoldTitle = { menu = task.id to TaskMenuPage.Rename },
                            // R7.3b: a hold lifts the row for a drag (and starts selecting); until then it opens the row's menu.
                            onHold = { menu = task.id to TaskMenuPage.Main },
                        ) {
                            OpenTask(task, self, zone, onTickStep = { step -> controller.tickStep(task.id, step) })
                        }
                    }
                }
            }
        }
        Fab(onClick = { addingAnywhere = true }, modifier = Modifier.align(Alignment.BottomEnd).padding(end = 16.dp, bottom = 20.dp))
    }

    menu?.let { (id, start) ->
        val task = lists.tasks.firstOrNull { it.id == id && it.task.isLive }?.task
        if (task == null) {
            LaunchedEffect(id) { menu = null }
        } else {
            TaskMenuSheet(task, lists, self, zone, controller, pc, onDismiss = { menu = null }, start = start)
        }
    }

    options?.let { start ->
        TasksOptionsSheet(state, prefs::update, lists, now, zone, controller, onOpenSettings = onOpenSettings, onDismiss = { options = null }, start = start)
    }

    // "+" on a page adds there; the round "+" adds to the page you are looking at (Next while the Shelf is hidden).
    val target = adding ?: if (addingAnywhere) pages.getOrNull(index).takeIf { !hidden } else null
    if (adding != null || addingAnywhere) {
        val bin = target?.bin ?: TaskBin.NEXT
        val place = listOfNotNull(TaskBins.place(bin), target?.takeIf { it.labelId != null || it.tag != null }?.title).joinToString(" · ")
        QuickAddSheet(
            place,
            onAdd = { title -> controller.add(title, bin, target?.labelId ?: chipLabel?.takeIf { byBins && it != ShelfPages.NO_LABEL }, target?.tag?.let { listOf(it) }) },
            onDismiss = { adding = null; addingAnywhere = false },
        )
    }

    if (newLabel) {
        AbSheet(title = "New label", subtitle = "A label is the coloured dot, and each task has one", onDismiss = { newLabel = false }, height = AbSheetHeight.Half) {
            NewLabelForm(labels.map { it.name }, onAdd = { name -> if (controller.createLabel(name, null)) newLabel = false })
        }
    }
}

// A row's own swipes: a Shelf task goes right to Today (Queued) and left to Later (Next when it is in Later already); a Today task
// goes right to Done and left to the Shelf.
private fun swipeOf(task: TaskItem, controller: ShelfController): RowSwipe? = when {
    task.isDone -> null
    TaskBins.isShelf(task.bin) -> {
        val left = if (task.bin == TaskBin.LATER) TaskBin.NEXT else TaskBin.LATER
        RowSwipe(
            rightLabel = "Today", rightIcon = Icons.Outlined.Today, onRight = { controller.toQueued(listOf(task.id)) },
            leftLabel = TaskBins.name(left), leftIcon = if (left == TaskBin.NEXT) Icons.Outlined.Start else Icons.Outlined.MoveToInbox, onLeft = { controller.move(listOf(task.id), left, null) },
        )
    }
    else -> RowSwipe(
        rightLabel = "Done", rightIcon = Icons.Outlined.CheckCircle, onRight = { controller.finish(task.id) },
        leftLabel = "Shelf", leftIcon = Icons.Outlined.Inbox, onLeft = { controller.move(listOf(task.id), TaskBin.NEXT, null) },
    )
}

// A header icon (44 dp, in ink); filled with a surface2 circle while its sheet is open or it is in effect (a filter is on).
@Composable
private fun HeaderIcon(icon: ImageVector, description: String, active: Boolean, onClick: () -> Unit) {
    val colors = Ab.colors
    Box(
        Modifier.size(44.dp).clip(CircleShape).background(if (active) colors.surface2 else colors.bg).clickable(role = Role.Button, onClick = onClick)
            .semantics { contentDescription = description },
        contentAlignment = Alignment.Center,
    ) {
        Icon(icon, contentDescription = null, tint = colors.ink, modifier = Modifier.size(22.dp))
    }
}

// The search field in the chip row's place: it finds titles, steps, notes and tags in what is loaded; the text is kept in memory only.
@Composable
private fun SearchSlot(text: String, onChange: (String) -> Unit) {
    val colors = Ab.colors
    val focus = remember { FocusRequester() }
    val keyboard = LocalSoftwareKeyboardController.current
    LaunchedEffect(Unit) { runCatching { focus.requestFocus() } }
    Row(
        Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, bottom = 10.dp).height(38.dp).background(colors.surface2, CircleShape).padding(horizontal = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Icon(Icons.Outlined.Search, contentDescription = null, tint = colors.muted, modifier = Modifier.size(18.dp))
        Box(Modifier.weight(1f), contentAlignment = Alignment.CenterStart) {
            if (text.isEmpty()) Text("Search titles, steps and notes", style = Ab.type.bodyMedium, color = colors.faint, maxLines = 1)
            BasicTextField(
                value = text, onValueChange = onChange, singleLine = true, modifier = Modifier.fillMaxWidth().focusRequester(focus),
                textStyle = Ab.type.bodyMedium.copy(color = colors.ink), cursorBrush = SolidColor(colors.ink),
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search), keyboardActions = KeyboardActions(onSearch = { keyboard?.hide() }),
            )
        }
    }
}

// The round "+" (board: 60 dp, radius 18, the accent, above the tabs at the right).
@Composable
private fun Fab(onClick: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val shape = RoundedCornerShape(18.dp)
    Surface(onClick = onClick, modifier = modifier.size(60.dp).shadow(8.dp, shape).semantics { contentDescription = "Add a task" }, shape = shape, color = colors.accent, contentColor = colors.onAccent) {
        Box(contentAlignment = Alignment.Center) { Icon(Icons.Outlined.Add, contentDescription = null, modifier = Modifier.size(30.dp)) }
    }
}
