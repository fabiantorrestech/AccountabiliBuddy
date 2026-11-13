package com.accountabilibuddy.companion.ui.tasks.page

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.scrollBy
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyListState
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
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.layout.boundsInRoot
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInRoot
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.tasks.TasksUiPrefs
import com.accountabilibuddy.companion.ui.UiPrefs
import com.accountabilibuddy.companion.ui.kit.AbPageHeader
import com.accountabilibuddy.companion.ui.kit.AbPageHeaderDefaults
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.shell.LocalBottomBarOverride
import com.accountabilibuddy.companion.ui.tasks.TasksPc
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.TaskLabels
import com.accountabilibuddy.shared.tasks.ShelfDrag
import com.accountabilibuddy.shared.tasks.ShelfDrop
import com.accountabilibuddy.shared.tasks.ShelfDropSpot
import com.accountabilibuddy.shared.tasks.ShelfPage
import com.accountabilibuddy.shared.tasks.ShelfPages
import com.accountabilibuddy.shared.tasks.ShelfPaging
import com.accountabilibuddy.shared.tasks.ShelfPicker
import com.accountabilibuddy.shared.tasks.ShelfReorder
import com.accountabilibuddy.shared.tasks.ShelfToasts
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskBins
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskReminderRings
import com.accountabilibuddy.shared.tasks.TaskRowText
import com.accountabilibuddy.shared.tasks.TaskViewConfig
import com.accountabilibuddy.shared.tasks.TaskViewRules
import com.accountabilibuddy.shared.tasks.TodayStripCounts
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.time.OffsetDateTime
import java.time.ZoneOffset
import kotlin.math.roundToInt

/** How far above the finger the dragged card rides, so the finger never hides it (dp). */
private const val GHOST_ABOVE_DP = 56

/** The band at the top and bottom of a list where a drag scrolls it (dp), and the fastest step per frame (dp). */
private const val SCROLL_EDGE_DP = 56
private const val SCROLL_STEP_DP = 14

/** How far above the picker the finger goes to close it again ("Drag back up to close this"; dp). */
private const val PICKER_CLOSE_DP = 24

/** A row ticked and kept in its place, struck through, until its toast goes (bundle spec §4: struck through, then the collapse). */
private class FinishingRow(val task: TaskItem, val pageKey: String, val index: Int, val undo: () -> Unit)

/**
 * The phone's Tasks tab (round 2 R7.3; boards Tasks-Phone, -Label, -Session, -Drag, -Drag-Bin, -Label-Drag, -Drag-Pick, -Moved; brief
 * §4.5): planning only. From the top, each at a fixed place whatever loads or changes (stable layout, brief §3.1): the header "Tasks"
 * with Views, Options and Search (no gear; in select mode × · "2 selected" · "Select all 4" in its place); the **Today strip** (counts,
 * opens the Today tab; while a row is lifted it lights up as the drop into Today › Queued); the chip row (or the search field in its
 * place); then the Shelf **one page at a time** ([ShelfPager]), or, while a session runs, the hidden-Shelf card; the "+" button and the
 * toast.
 *
 * **A hold** of 450 ms anywhere on a closed row (its title too) lifts it ([shelfDrag], the page's one gesture: no row, list, swipe or
 * pager gesture takes it): a haptic tick, the row stays at 35% and a tilted card follows the finger, the strip lights up, drop zones
 * slide in over the card's edges (the neighbour pages; an end has none unless wrap around is on) and the bottom bar becomes "Hold to
 * pick a bin" (held ~300 ms: the picker of every page) and the red Cancel. Holding a zone ~600 ms turns the page; near the list's top
 * or bottom the list scrolls. Let go: on the strip → Today › Queued (never This session), on a zone or a picker tile → that page, on
 * the list → a new place (or that page, after a page turn), on Cancel → nothing, the selection kept. Let go without moving → select
 * mode with that row. In select mode a hold on a selected row lifts the whole selection as a stack. A hold on an open task opens its
 * ⋮ sheet (its title: Rename). Every move has Undo in the board's toast (×, timer bar, tap to go there and see the rows flash).
 *
 * It reads the phone's one task list and what this device remembers ([TasksUiPrefs]) and changes tasks only through
 * [ShelfController]; with the task list closed nothing lifts and nothing is written. The search text, an opened task, the selection
 * and the toast live in memory only.
 *
 * @param controlPc The PC this phone controls, to know whether a session runs.
 */
@Composable
fun TasksPageHost(controlPc: ReceivedDevice?, @Suppress("UNUSED_PARAMETER") snackbar: SnackbarHostState, onOpenToday: () -> Unit, onOpenSettings: () -> Unit) {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val scope = rememberCoroutineScope()
    val haptics = LocalHapticFeedback.current
    val density = LocalDensity.current
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
    var addAnother by remember { mutableStateOf(false) }
    var newLabel by remember { mutableStateOf(false) }
    var selecting by remember { mutableStateOf(false) }
    var selection by remember { mutableStateOf(listOf<String>()) }
    var bulk by remember { mutableStateOf<BulkPage?>(null) }
    var renameQueue by remember { mutableStateOf(listOf<String>()) }
    var waiting by remember { mutableStateOf<List<String>?>(null) }
    var toast by remember { mutableStateOf<TasksToast?>(null) }
    var flash by remember { mutableStateOf(setOf<String>()) }
    val finishing = remember { mutableStateMapOf<String, FinishingRow>() }
    val drag = remember { ShelfDragState() }
    val listStates = remember { HashMap<String, LazyListState>() }

    // A new toast replaces the one showing (bundle spec §14); the one it replaces is gone, as if its time were up.
    val showToast: (TasksToast) -> Unit = { next ->
        val before = toast
        toast = next
        before?.onGone?.invoke()
    }
    val dropToast: (TasksToast) -> Unit = { gone ->
        if (toast === gone) {
            toast = null
            gone.onGone?.invoke()
        }
    }
    val controller = remember(app) { ShelfController(app, sessionFull = { ids -> waiting = ids }) { next -> showToast(next) } }

    val config = state.config
    val grouping = state.grouping
    val byBins = grouping == TaskViewConfig.GROUP_BINS || grouping == null
    val labels = lists.labels.map { it.label }.filter { it.isLive }.sortedBy { it.position }
    val inSession = pc != null
    val pages = remember(lists, config, now, search, state.orders, chipLabel, chipBin, inSession) {
        ShelfPages.build(lists, config, now, zone, search.takeIf { it.isNotBlank() }, state.orderOf(grouping), chipLabel, chipBin, sessionRuns = inSession)
    }
    val keys = pages.map { it.key }
    val wrap = state.wrapOf(grouping)
    // The page showing: the one last picked while it exists, else the start page (Tasks opens on it), else the nearest one left.
    val picked = currentKey?.let { keys.indexOf(it) } ?: -1
    val index = when {
        picked >= 0 -> picked
        currentKey == null -> ShelfPaging.startIndex(keys, state.startOf(grouping))
        else -> lastIndex.coerceIn(0, (keys.size - 1).coerceAtLeast(0))
    }
    val shown = pages.getOrNull(index)
    // A new grouping starts on its own start page.
    LaunchedEffect(grouping) { currentKey = null }
    val showPage: (Int) -> Unit = { to ->
        keys.getOrNull(to)?.let {
            currentKey = it
            lastIndex = to
            expanded = null
        }
    }

    val liveIds = remember(lists) { lists.tasks.filter { it.task.isLive && !it.task.isDone }.map { it.id } }
    // Tasks finished, deleted or moved away on another device leave the selection; with none left select mode ends.
    LaunchedEffect(liveIds) {
        if (selection.isNotEmpty()) {
            selection = ShelfDrag.pruned(selection, liveIds)
            if (selection.isEmpty()) selecting = false
        }
    }
    val stopSelecting = {
        selecting = false
        selection = emptyList()
    }
    BackHandler(enabled = selecting && !drag.active) { stopSelecting() }
    LaunchedEffect(flash) {
        if (flash.isNotEmpty()) {
            delay(ShelfToasts.FLASH_MS)
            flash = emptySet()
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
    val canReorder = ShelfReorder.canReorder(config)
    val sheetOpen = menu != null || options != null || adding != null || addingAnywhere || newLabel || bulk != null || waiting != null || renameQueue.isNotEmpty()

    // ---- the drag ----

    val tiles = if (drag.active && drag.pickerOpen) ShelfPicker.tiles(grouping, pages, labels, shown?.key) else emptyList()
    val pageOf: (String) -> ShelfPage? = { key -> pages.firstOrNull { it.key == key } ?: tiles.firstOrNull { it.page.key == key }?.page ?: ShelfPicker.tiles(grouping, pages, labels, null).firstOrNull { it.page.key == key }?.page }
    // The rows each page draws in order (a ticked row kept struck through at its place until its toast goes), and the ids a drag
    // finds its place among.
    val rowsOf: (ShelfPage) -> List<TaskItem> = { page ->
        val base = page.tasks.toMutableList()
        finishing.values.filter { it.pageKey == page.key && base.none { task -> task.id == it.task.id } }.sortedBy { it.index }
            .forEach { kept -> base.add(kept.index.coerceIn(0, base.size), kept.task) }
        val spot = drag.spot
        if (drag.active && canReorder && page.key == drag.origin && page.key == shown?.key && spot is ShelfDropSpot.Card) {
            ShelfReorder.displayed(base, drag.ids, spot.insertAt) { it.id }
        } else {
            base
        }
    }
    val displayed = remember { HashMap<String, List<String>>() }
    val pxPerDp = density.density

    // How many of the page's other rows lie above [y] (the rows as drawn, the dragged ones left out).
    fun insertAt(y: Float): Int {
        val key = shown?.key ?: return 0
        val listState = listStates[key] ?: return 0
        val top = drag.lists[key]?.top ?: return 0
        val info = listState.layoutInfo
        val visible = info.visibleItemsInfo.associateBy { it.key as? String }
        val first = info.visibleItemsInfo.firstOrNull()?.index ?: 0
        val dragged = drag.ids.map { it.lowercase() }.toSet()
        var above = 0
        displayed[key].orEmpty().forEachIndexed { at, id ->
            if (id.lowercase() in dragged) return@forEachIndexed
            val item = visible[id]
            val isAbove = if (item != null) top + item.offset + item.size / 2f < y else at < first
            if (isAbove) above++
        }
        return above
    }

    fun spotAt(at: Offset): ShelfDropSpot {
        if (drag.pickerOpen) {
            drag.tiles.entries.firstOrNull { it.value.contains(at) }?.let { return ShelfDropSpot.Tile(it.key) }
            if (drag.picker?.contains(at) == true) return ShelfDropSpot.Bar
        }
        if (drag.barCancel?.contains(at) == true) return ShelfDropSpot.Cancel
        if (drag.barPick?.contains(at) == true) return ShelfDropSpot.Bar
        if (drag.strip?.contains(at) == true) return ShelfDropSpot.Strip
        val pager = drag.pager
        if (pager != null && pages.size > 1 && at.y >= pager.top && at.y <= pager.bottom) {
            val reach = (ZoneInset.value + ShelfDrag.ZONE_DP + ShelfDrag.ZONE_REACH_DP) * pxPerDp
            val side = when {
                at.x <= pager.left + reach -> -1
                at.x >= pager.right - reach -> 1
                else -> 0
            }
            // A side the hold began in counts once the finger has left it.
            drag.blockedSides = drag.blockedSides.filter { it == side }.toSet()
            if (side != 0 && side !in drag.blockedSides && ShelfDrag.zoneTarget(keys, index, side, wrap) != null) {
                return if (side < 0) ShelfDropSpot.Left else ShelfDropSpot.Right
            }
        }
        val list = shown?.key?.let { drag.lists[it] }
        if (list != null && list.contains(at)) return ShelfDropSpot.Card(insertAt(at.y))
        return ShelfDropSpot.Elsewhere
    }

    var pickerJob by remember { mutableStateOf<Job?>(null) }
    fun follow(at: Offset) {
        drag.pointer = at
        // "Drag back up to close this": above the open picker it closes again.
        val picker = drag.picker
        if (drag.pickerOpen && picker != null && at.y < picker.top - PICKER_CLOSE_DP * pxPerDp) {
            drag.pickerOpen = false
            drag.picker = null
            drag.tiles.clear()
        }
        val before = drag.spot
        val spot = spotAt(at)
        if (spot == before) return
        drag.spot = spot
        // A light tick on reaching a place that takes the drop.
        if (spot !is ShelfDropSpot.Card && spot != ShelfDropSpot.Elsewhere && spot != ShelfDropSpot.Bar) haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove)
        if (spot == ShelfDropSpot.Bar && !drag.pickerOpen && pickerJob?.isActive != true) {
            pickerJob = scope.launch {
                delay(ShelfDrag.PICKER_HOLD_MS)
                if (drag.active && drag.spot == ShelfDropSpot.Bar) {
                    drag.pickerOpen = true
                    haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                }
            }
        } else if (spot != ShelfDropSpot.Bar) {
            pickerJob?.cancel()
        }
    }

    fun endDrag() {
        pickerJob?.cancel()
        drag.end()
    }

    val handlers = rememberUpdatedState(
        ShelfDragHandlers(
            canLift = { app.taskModel.isActive && !sheetOpen && !hidden && !drag.active },
            onLift = { id, at ->
                val order = pages.flatMap { page -> page.tasks.map { it.id } }
                val lifted = ShelfDrag.lifted(id, selecting, selection).filter { it in liveIds || liveIds.any { live -> live.equals(it, ignoreCase = true) } }
                    .sortedBy { lift -> order.indexOfFirst { it.equals(lift, ignoreCase = true) }.let { at -> if (at < 0) Int.MAX_VALUE else at } }
                if (lifted.isEmpty()) {
                    false
                } else {
                    drag.lift(lifted, id, shown?.key, at)
                    val pager = drag.pager
                    val reach = (ZoneInset.value + ShelfDrag.ZONE_DP + ShelfDrag.ZONE_REACH_DP) * pxPerDp
                    drag.blockedSides = when {
                        pager == null -> emptySet()
                        at.x <= pager.left + reach -> setOf(-1)
                        at.x >= pager.right - reach -> setOf(1)
                        else -> emptySet()
                    }
                    expanded = null
                    haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                    true
                }
            },
            onMove = { at -> follow(at) },
            onRelease = { at ->
                follow(at)
                val ids = drag.ids
                val origin = drag.origin
                val drop = ShelfDrag.resolve(drag.spot, keys, index, origin, wrap, drag.travelled, canReorder)
                when (drop) {
                    ShelfDrop.ToQueued -> controller.toQueued(ids)
                    is ShelfDrop.ToPage -> pageOf(drop.key)?.let { controller.dropOnPage(ids, it) }
                    is ShelfDrop.Reorder -> pages.firstOrNull { it.key == origin }?.let { page -> controller.reorder(page.tasks.map { it.id }, ids, drop.insertAt) }
                    ShelfDrop.Select, ShelfDrop.Cancel, ShelfDrop.Nothing -> Unit
                }
                if (drop is ShelfDrop.ToQueued || drop is ShelfDrop.ToPage || drop is ShelfDrop.Reorder) haptics.performHapticFeedback(HapticFeedbackType.Confirm)
                val (nowSelecting, nowSelected) = ShelfDrag.selectionAfter(drop, ids, selecting, selection)
                selecting = nowSelecting
                selection = nowSelected
                endDrag()
            },
            onCancel = { endDrag() },
        ),
    )

    // Holding a zone turns the page (and again, every 600 ms, while it is held).
    val turnSide = when (drag.spot) {
        ShelfDropSpot.Left -> -1
        ShelfDropSpot.Right -> 1
        else -> 0
    }
    LaunchedEffect(drag.active, turnSide, index) {
        if (!drag.active || turnSide == 0) return@LaunchedEffect
        delay(ShelfDrag.PAGE_TURN_MS)
        ShelfPaging.neighbour(index, keys.size, turnSide, wrap)?.let { to ->
            showPage(to)
            haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove)
        }
    }
    // Near the list's top or bottom edge the list scrolls (below the strip: the strip is outside the list and never scrolls it).
    val shownKey = rememberUpdatedState(shown?.key)
    val followNow = rememberUpdatedState<(Offset) -> Unit> { at -> follow(at) }
    LaunchedEffect(drag.active) {
        if (!drag.active) return@LaunchedEffect
        val edge = SCROLL_EDGE_DP * pxPerDp
        val fastest = SCROLL_STEP_DP * pxPerDp
        while (isActive && drag.active) {
            withFrameNanos { }
            if (drag.spot !is ShelfDropSpot.Card) continue
            val key = shownKey.value ?: continue
            val rect = drag.lists[key] ?: continue
            val listState = listStates[key] ?: continue
            val y = drag.pointer.y
            val step = when {
                y < rect.top + edge -> -fastest * ((rect.top + edge - y) / edge).coerceIn(0f, 1f)
                y > rect.bottom - edge -> fastest * ((y - (rect.bottom - edge)) / edge).coerceIn(0f, 1f)
                else -> 0f
            }
            if (step != 0f && listState.scrollBy(step) != 0f) followNow.value(drag.pointer)
        }
    }

    // The bottom bar while dragging: drawn in the tabs' place (or, with no tabs, at the page's foot).
    val barHolder = LocalBottomBarOverride.current
    val dragBar: @Composable () -> Unit = {
        DragBar(
            ShelfPicker.barText(grouping, drag.pickerOpen), drag.pickerOpen, overPick = drag.spot == ShelfDropSpot.Bar, overCancel = drag.spot == ShelfDropSpot.Cancel,
            onPickBounds = { drag.barPick = it }, onCancelBounds = { drag.barCancel = it },
        )
    }
    val currentBar = rememberUpdatedState(dragBar)
    DisposableEffect(barHolder, drag.active) {
        if (drag.active) barHolder?.content = { currentBar.value() }
        onDispose { barHolder?.content = null }
    }

    val notice = rememberTaskUpdateNotice(received.map { it.file }, app.sync.sharesTaskLists())

    Box(
        Modifier
            .fillMaxSize()
            .onGloballyPositioned { drag.host = it.positionInRoot() }
            .shelfDrag(drag, handlers),
    ) {
        Column(Modifier.fillMaxSize()) {
            Box(Modifier.padding(horizontal = AbPageHeaderDefaults.PaneEdge)) {
                // Select mode's header lies over the page header at its size, so entering it moves nothing.
                Box(if (selecting) Modifier.alpha(0f).clearAndSetSemantics { } else Modifier) {
                    AbPageHeader("Tasks") {
                        HeaderIcon(Icons.Outlined.BookmarkBorder, "Saved views", active = false) { options = OptionsPage.Views }
                        HeaderIcon(Icons.Outlined.Tune, "Options: view, filter and sort", active = options != null || TaskViewWords.filters(config)) { options = OptionsPage.Main }
                        HeaderIcon(if (searching) Icons.Outlined.Close else Icons.Outlined.Search, if (searching) "Close search" else "Search", active = searching) {
                            if (searching) search = ""
                            searching = !searching
                        }
                    }
                }
                if (selecting) {
                    val visible = shown?.tasks?.map { it.id }.orEmpty()
                    SelectHeader(
                        count = selection.size,
                        selectAllText = ShelfDrag.selectAllText(selection, visible),
                        onStop = stopSelecting,
                        onView = { if (selection.isNotEmpty()) bulk = BulkPage.Selected },
                        onSelectAll = {
                            selection = ShelfDrag.selectAll(selection, visible)
                            if (selection.isEmpty()) selecting = false
                        },
                        modifier = Modifier.matchParentSize().background(Ab.colors.bg),
                    )
                }
            }
            TodayStrip(
                stripText, onOpen = onOpenToday, lit = drag.active, hot = drag.spot == ShelfDropSpot.Strip, selecting = selecting,
                modifier = Modifier.onGloballyPositioned { drag.strip = it.boundsInRoot() },
            )
            // R7.2 review item 3 and R7.3b (owner): the PCs older than the one task list, a line each; × closes it for 3 days.
            notice?.let { TaskUpdateNoticeCard(it, Modifier.padding(horizontal = AbPageHeaderDefaults.PaneEdge, vertical = 4.dp)) }
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
                ShelfPager(
                    pages, index, wrap = wrap, motionScale = motion.scale, onIndex = showPage, modifier = Modifier.weight(1f),
                    dragging = drag.active, hotSide = turnSide, onBounds = { drag.pager = it },
                ) { page ->
                    val rows = rowsOf(page)
                    SideEffect { displayed[page.key] = rows.map { it.id } }
                    ShelfPageCard(
                        page, emptyText, onAdd = { adding = page }, rows = rows, listState = listStates.getOrPut(page.key) { LazyListState() },
                        motionScale = motion.scale, onListBounds = { drag.lists[page.key] = it },
                    ) { task ->
                        val label = labels.firstOrNull { it.id.equals(task.labelId, ignoreCase = true) }
                        val onLabelPage = page.key.startsWith("label:")
                        val kept = finishing[task.id]
                        val isOpen = expanded == task.id
                        val liftable = !isOpen && !task.isDone && kept == null
                        val selected = selection.any { it.equals(task.id, ignoreCase = true) }
                        ShelfRow(
                            task = task,
                            parts = TaskRowText.detail(task, label?.name, now, zone, showBin = page.bin == null, showLabel = !onLabelPage),
                            labelColor = label?.color,
                            showDot = !onLabelPage,
                            compact = config.density == TaskViewConfig.DENSITY_COMPACT,
                            expanded = isOpen,
                            swipe = if (selecting || kept != null) null else swipeOf(task, controller),
                            onTick = {
                                when {
                                    kept != null -> {
                                        // A second tap on a row being finished takes the tick back.
                                        finishing.remove(task.id)
                                        kept.undo()
                                    }

                                    task.isDone -> controller.reopen(task.id)
                                    else -> {
                                        val at = page.tasks.indexOfFirst { it.id == task.id }.coerceAtLeast(0)
                                        val undo = controller.finish(task.id, onGone = { finishing.remove(task.id) })
                                        if (undo != null) finishing[task.id] = FinishingRow(task, page.key, at, undo)
                                    }
                                }
                            },
                            onTap = {
                                if (selecting) {
                                    selection = ShelfDrag.toggled(selection, task.id)
                                    if (selection.isEmpty()) selecting = false
                                } else {
                                    expanded = if (isOpen) null else task.id
                                }
                            },
                            onMenu = { menu = task.id to TaskMenuPage.Main },
                            onTitle = { menu = task.id to TaskMenuPage.Rename },
                            // A row that can't be lifted (an open task, one being finished) opens its menu on a hold instead.
                            onHold = if (liftable) null else ({ menu = task.id to TaskMenuPage.Main }),
                            state = ShelfRowState(
                                lifted = drag.active && drag.ids.any { it.equals(task.id, ignoreCase = true) },
                                selecting = selecting,
                                selected = selected,
                                finishing = kept != null,
                                flash = task.id in flash,
                            ),
                            liftable = liftable,
                            onBounds = { bounds -> if (bounds == null) drag.rows.remove(task.id) else drag.rows[task.id] = bounds },
                            actions = if (!liftable) emptyList() else rowActions(task, page, pages, index, wrap, canReorder && page.key == shown?.key, selected, controller,
                                onToggle = {
                                    selection = ShelfDrag.toggled(selection, task.id)
                                    selecting = selection.isNotEmpty()
                                }),
                        ) {
                            OpenTask(
                                task, self, zone, onTickStep = { step -> controller.tickStep(task.id, step) }, rings = TaskReminderRings.line(lists, task, self),
                                edit = StepEdits(
                                    onAdd = { title -> controller.addStep(task.id, title) },
                                    onStep = { stepId, title, action ->
                                        when (action) {
                                            StepAction.OwnTask -> controller.ownTask(task.id, stepId, title, page.key)
                                            StepAction.Up -> controller.moveStep(task.id, stepId, -1)
                                            StepAction.Down -> controller.moveStep(task.id, stepId, 1)
                                            StepAction.Delete -> controller.removeStep(task.id, stepId)
                                        }
                                    },
                                ),
                            )
                        }
                    }
                }
            }
        }

        // Select mode's bar, in the "+" button's place.
        if (selecting && !drag.active) {
            BulkBar(
                selection.size, onMove = { bulk = BulkPage.Move }, onOptions = { bulk = BulkPage.Options },
                onDelete = {
                    controller.delete(selection)
                    stopSelecting()
                },
                modifier = Modifier.align(Alignment.BottomCenter).padding(horizontal = 16.dp, vertical = 12.dp).blocksDrag(drag, "bulk"),
            )
        } else if (!drag.active && toast == null) {
            Fab(onClick = { addingAnywhere = true }, modifier = Modifier.align(Alignment.BottomEnd).padding(end = 16.dp, bottom = 20.dp).blocksDrag(drag, "fab"))
        }

        toast?.let { current ->
            key(current) {
                val goTo = current.goTo
                val canGo = goTo != null && (goTo == TasksToast.TODAY || goTo in keys)
                TasksToastCard(
                    current, motion.scale,
                    onGo = if (!canGo) null else ({
                        dropToast(current)
                        if (goTo == TasksToast.TODAY) {
                            onOpenToday()
                        } else {
                            showPage(keys.indexOf(goTo))
                            flash = current.flash.toSet()
                        }
                    }),
                    onUndo = current.undo?.let { undo -> { undo(); dropToast(current) } },
                    onGone = { dropToast(current) },
                    modifier = Modifier.align(Alignment.BottomCenter).padding(horizontal = 16.dp, vertical = 12.dp).blocksDrag(drag, "toast"),
                )
            }
        }

        if (drag.active) {
            // With the picker open the page dims under it (board Tasks-Phone-Drag-Pick).
            if (drag.pickerOpen) {
                Box(Modifier.fillMaxSize().background(Ab.colors.bg.copy(alpha = 0.55f)))
                DragPicker(
                    ShelfPicker.title(grouping), ShelfPicker.note(grouping), tiles, overKey = (drag.spot as? ShelfDropSpot.Tile)?.key,
                    onBounds = { drag.picker = it }, onTileBounds = { tileKey, bounds -> drag.tiles[tileKey] = bounds },
                    modifier = Modifier.align(Alignment.BottomCenter),
                )
            }
            if (barHolder == null) {
                Box(Modifier.align(Alignment.BottomCenter).fillMaxWidth().height(76.dp).background(Ab.colors.bg)) { dragBar() }
            }
            // The page under the finger (a zone's or a picker tile's), for the card's "ePT → mPT".
            val over = when (val spot = drag.spot) {
                ShelfDropSpot.Left, ShelfDropSpot.Right -> ShelfDrag.zoneTarget(keys, index, turnSide, wrap)?.let(pageOf)
                is ShelfDropSpot.Tile -> pageOf(spot.key)
                else -> null
            }
            DraggedCard(drag, lists.tasks.map { it.task }, labels, over, grouping, now, zone)
        }
    }

    menu?.let { (id, start) ->
        val task = lists.tasks.firstOrNull { it.id == id && it.task.isLive }?.task
        if (task == null) {
            LaunchedEffect(id) { menu = null }
        } else {
            TaskMenuSheet(task, lists, self, zone, controller, pc, onDismiss = { menu = null }, start = start)
        }
    }

    // Select mode's "Rename one by one": the Rename sheet for each selected task in turn.
    renameQueue.firstOrNull()?.let { id ->
        val task = lists.tasks.firstOrNull { it.id.equals(id, ignoreCase = true) && it.task.isLive }?.task
        if (task == null) {
            LaunchedEffect(id) { renameQueue = renameQueue.drop(1) }
        } else if (menu == null) {
            key(id) { TaskMenuSheet(task, lists, self, zone, controller, pc, onDismiss = { renameQueue = renameQueue.drop(1) }, start = TaskMenuPage.Rename) }
        }
    }

    bulk?.let { start ->
        val chosen = selection.mapNotNull { id -> lists.tasks.firstOrNull { it.id.equals(id, ignoreCase = true) && it.task.isLive }?.task }
        BulkSheet(
            start, chosen, lists, controller, pc,
            onUnselect = { id ->
                selection = ShelfDrag.toggled(selection, id)
                if (selection.isEmpty()) selecting = false
            },
            onMoved = stopSelecting,
            onRenameEach = { renameQueue = selection },
            onDismiss = { bulk = null },
        )
    }

    waiting?.let { ids -> SessionFullSheet(ids, lists, controller, onDismiss = { waiting = null }) }

    options?.let { start ->
        TasksOptionsSheet(state, prefs::update, lists, now, zone, controller, onOpenSettings = onOpenSettings, onDismiss = { options = null }, start = start)
    }

    // "+" on a page adds there; the round "+" adds to the page you are looking at (Next while the Shelf is hidden).
    val target = adding ?: if (addingAnywhere) shown.takeIf { !hidden } else null
    if (adding != null || addingAnywhere) {
        AddTaskSheet(
            startBin = target?.bin ?: TaskBin.NEXT,
            startLabel = target?.labelId ?: chipLabel?.takeIf { byBins && it != ShelfPages.NO_LABEL },
            startTag = target?.tag,
            labels = labels,
            knownTags = TaskLabels.allOf(lists.tasks.filter { it.task.isLive }.map { it.task.tags }),
            another = addAnother,
            onAnother = { addAnother = it },
            onAdd = { draft ->
                controller.add(draft.title, draft.bin, draft.labelId, draft.tags, draft.estimateMinutes, draft.note, draft.steps, draft.dueOn, draft.dueAt, draft.reminders, pc)
            },
            onDismiss = {
                adding = null
                addingAnywhere = false
            },
        )
    }

    if (newLabel) {
        AbSheet(title = "New label", subtitle = "A label is the coloured dot, and each task has one", onDismiss = { newLabel = false }, height = AbSheetHeight.Half) {
            NewLabelForm(labels.map { it.name }, onAdd = { name -> if (controller.createLabel(name, null)) newLabel = false })
        }
    }
}

// The card that follows the finger: the held task in front (its label dot and detail line; over a label's zone or tile, the change
// "ePT → mPT · #Release"), the rest of a stack behind it.
@Composable
private fun DraggedCard(
    drag: ShelfDragState,
    tasks: List<TaskItem>,
    labels: List<com.accountabilibuddy.shared.tasks.TaskLabel>,
    over: ShelfPage?,
    grouping: String?,
    now: OffsetDateTime,
    zone: java.time.ZoneId,
) {
    val density = LocalDensity.current
    val lead = tasks.firstOrNull { it.id.equals(drag.lead, ignoreCase = true) } ?: return
    val label = labels.firstOrNull { it.id.equals(lead.labelId, ignoreCase = true) }
    val spot = drag.spot
    val toLabel = over?.takeIf { grouping == TaskViewConfig.GROUP_LABEL && (it.labelId != null || it.key == TaskViewRules.LABEL_NONE) }?.title
    val parts = TaskRowText.detail(lead, null, now, zone, showBin = false, showLabel = false).map { it.text }
    val tags = lead.tags?.takeIf { it.isNotEmpty() }?.joinToString(" ") { "#$it" }
    val line = ShelfDrag.ghostLine(label?.name, parts, tags, drag.ids.size, toLabel)
    val widthPx = with(density) { ShelfDrag.GHOST_WIDTH_DP.dp.toPx() }
    val abovePx = with(density) { GHOST_ABOVE_DP.dp.toPx() }
    val overTarget = spot != ShelfDropSpot.Elsewhere && spot !is ShelfDropSpot.Card && spot != ShelfDropSpot.Bar
    DragGhost(
        title = lead.title, line = line, dotColor = label?.color, showDot = label != null, count = drag.ids.size, over = overTarget,
        modifier = Modifier.offset {
            IntOffset((drag.pointer.x - drag.host.x - widthPx / 2f).roundToInt(), (drag.pointer.y - drag.host.y - abovePx).roundToInt())
        },
    )
}

// What a drag does, offered by name to a screen reader (and to anyone who can't hold and drag): into Today · Queued, onto the page
// on either side, one row up or down, and selecting.
private fun rowActions(
    task: TaskItem, page: ShelfPage, pages: List<ShelfPage>, shown: Int, wrap: Boolean, canStep: Boolean, selected: Boolean, controller: ShelfController,
    onToggle: () -> Unit,
): List<Pair<String, () -> Unit>> = buildList {
    add("Move to Today · Queued" to { controller.toQueued(listOf(task.id)) })
    val keys = pages.map { it.key }
    for (step in listOf(-1, 1)) {
        val key = ShelfDrag.zoneTarget(keys, shown, step, wrap) ?: continue
        val target = pages.firstOrNull { it.key == key } ?: continue
        if (target.key != page.key) add("Move to ${target.title}" to { controller.dropOnPage(listOf(task.id), target) })
    }
    if (canStep) {
        val order = page.tasks.map { it.id }
        val at = order.indexOf(task.id)
        if (at > 0) add("Move up" to { controller.reorderStep(order, task.id, -1) })
        if (at >= 0 && at < order.lastIndex) add("Move down" to { controller.reorderStep(order, task.id, 1) })
    }
    add((if (selected) "Unselect" else "Select") to onToggle)
}

// Marks a part that lies over the rows and takes its own touches, so a hold on it never lifts the row under it.
@Composable
private fun Modifier.blocksDrag(drag: ShelfDragState, name: String): Modifier {
    DisposableEffect(drag, name) { onDispose { drag.blockers.remove(name) } }
    return onGloballyPositioned { drag.blockers[name] = it.boundsInRoot() }
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
