package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.SyncFileStrip
import com.accountabilibuddy.shared.tasks.ShelfDrag
import com.accountabilibuddy.shared.tasks.ShelfDrop
import com.accountabilibuddy.shared.tasks.ShelfDropSpot
import com.accountabilibuddy.shared.tasks.ShelfEdits
import com.accountabilibuddy.shared.tasks.ShelfPages
import com.accountabilibuddy.shared.tasks.ShelfPicker
import com.accountabilibuddy.shared.tasks.ShelfReorder
import com.accountabilibuddy.shared.tasks.ShelfSession
import com.accountabilibuddy.shared.tasks.ShelfSteps
import com.accountabilibuddy.shared.tasks.ShelfToastLength
import com.accountabilibuddy.shared.tasks.ShelfToasts
import com.accountabilibuddy.shared.tasks.SyncTaskAccountSettings
import com.accountabilibuddy.shared.tasks.SyncTaskItem
import com.accountabilibuddy.shared.tasks.SyncTaskLabel
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskField
import com.accountabilibuddy.shared.tasks.TaskFieldStamps
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskLabel
import com.accountabilibuddy.shared.tasks.TaskListUpdateNotice
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskNumber
import com.accountabilibuddy.shared.tasks.TaskParts
import com.accountabilibuddy.shared.tasks.TaskReminder
import com.accountabilibuddy.shared.tasks.TaskReminderRings
import com.accountabilibuddy.shared.tasks.TaskStep
import com.accountabilibuddy.shared.tasks.TaskTitleHelp
import com.accountabilibuddy.shared.tasks.TaskViewConfig
import com.accountabilibuddy.shared.tasks.TaskViewRules
import java.time.Duration
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId
import java.util.UUID

/**
 * R7.3b checks with no C# twin (the phone's own rules): what a release does where (the strip, the zones, an edge with and without
 * wrap around, the page's own list, another page's list after a page turn, the picker's tiles, Cancel), the selection afterwards,
 * a stack's move, the picker's tiles per grouping, reordering on whole and filtered pages, the session cap's swap and "Add anyway",
 * Undo (bin, place, label and tags back), the toast's words, the Add sheet's extras, "Rings on" for this phone, and the "Update
 * AccountabiliBuddy on" notice (decided from the kept copy's mark; one line per device; closed, back after 3 days or for a new one).
 */
object TasksDragSelfCheck {
    private const val PC = "aaaaaaaa-0000-0000-0000-00000000000a"
    private const val PHONE = "bbbbbbbb-0000-0000-0000-00000000000b"
    private const val TABLET = "cccccccc-0000-0000-0000-00000000000c"
    private val zone: ZoneId = ZoneId.of("America/New_York")
    private val created: OffsetDateTime = OffsetDateTime.parse("2026-11-10T13:00:00Z")
    private val now: OffsetDateTime = OffsetDateTime.parse("2026-11-12T17:00:00Z")
    private val ept = TaskLabel(TaskLabel.idFor("ePT"), "ePT", "#2F6BEA", 2.0, created)
    private val ai = TaskLabel(TaskLabel.idFor("AI"), "AI", "#7C4DFF", 1.0, created)
    private val sda = TaskLabel(TaskLabel.idFor("SDA"), "SDA", "#E0322C", 3.0, created)
    private val bins = listOf("bin:sort", "bin:next", "bin:later")

    private fun id(n: Int) = "7373b73b-0000-0000-0000-%012d".format(n)

    private fun task(n: Int, bin: TaskBin, position: Double, change: (TaskItem) -> TaskItem = { it }): SyncTaskItem =
        SyncTaskItem(change(TaskItem(id(n), "Task $n", bin, position, created, created, created, number = TaskNumber(100 + n))), TaskFieldStamps.allAt(FieldStamp(created, PC)))

    // Next: 1 (ePT, #Release), 2 (AI), 3, 4 (ePT). Later: 5 (ePT, #launch-prep), 6. To sort: 7. Queued: 8. This session: empty.
    private fun world(): TaskLists = TaskLists(
        listOf(
            task(1, TaskBin.NEXT, 1.0) { it.copy(labelId = ept.id, tags = listOf("Release")) },
            task(2, TaskBin.NEXT, 2.0) { it.copy(labelId = ai.id) },
            task(3, TaskBin.NEXT, 3.0),
            task(4, TaskBin.NEXT, 4.0) { it.copy(labelId = ept.id) },
            task(5, TaskBin.LATER, 1.0) { it.copy(labelId = ept.id, tags = listOf("launch-prep")) },
            task(6, TaskBin.LATER, 2.0),
            task(7, TaskBin.SORT, 1.0),
            task(8, TaskBin.QUEUED, 1.0),
        ),
        listOf(SyncTaskLabel(ept), SyncTaskLabel(ai), SyncTaskLabel(sda)), emptyList(), emptyList(),
    )

    private fun TaskLists.of(n: Int): SyncTaskItem = tasks.first { it.id == id(n) }

    private fun TaskLists.order(bin: TaskBin): List<Int> =
        tasks.map { it.task }.filter { it.isLive && it.bin == bin }.sortedBy { it.position }.map { it.id.takeLast(2).toInt() }

    fun run(report: CheckReport) {
        PhoneBug1Check.run(report)
        resolve(report)
        selection(report)
        stack(report)
        picker(report)
        reorder(report)
        session(report)
        undo(report)
        words(report)
        add(report)
        rings(report)
        notice(report)
        steps(report)
    }

    private fun resolve(report: CheckReport) {
        fun at(spot: ShelfDropSpot, shown: Int = 1, origin: String? = "bin:next", wrap: Boolean = false, travelled: Boolean = true, canReorder: Boolean = true, keys: List<String> = bins) =
            ShelfDrag.resolve(spot, keys, shown, origin, wrap, travelled, canReorder)
        report.check("r73b/drop: the strip is Today · Queued", at(ShelfDropSpot.Strip) == ShelfDrop.ToQueued)
        report.check("r73b/drop: the left zone is the page on the left", at(ShelfDropSpot.Left) == ShelfDrop.ToPage("bin:sort"))
        report.check("r73b/drop: the right zone is the page on the right", at(ShelfDropSpot.Right) == ShelfDrop.ToPage("bin:later"))
        report.check("r73b/drop: wrap off, an edge offers no drop target", at(ShelfDropSpot.Left, shown = 0, origin = "bin:sort") == ShelfDrop.Nothing && at(ShelfDropSpot.Right, shown = 2, origin = "bin:later") == ShelfDrop.Nothing)
        report.check("r73b/drop: wrap on, the edge drops on the page at the other end", at(ShelfDropSpot.Left, shown = 0, origin = "bin:sort", wrap = true) == ShelfDrop.ToPage("bin:later") && at(ShelfDropSpot.Right, shown = 2, origin = "bin:later", wrap = true) == ShelfDrop.ToPage("bin:sort"))
        report.check("r73b/drop: one page has no zones even with wrap on", at(ShelfDropSpot.Left, shown = 0, origin = "all", wrap = true, keys = listOf("all")) == ShelfDrop.Nothing)
        report.check("r73b/drop: on its own list the row takes a new place", at(ShelfDropSpot.Card(2)) == ShelfDrop.Reorder(2))
        report.check("r73b/drop: no new place while another sort is on", at(ShelfDropSpot.Card(2), canReorder = false) == ShelfDrop.Nothing)
        report.check("r73b/drop: after the page turned, the list of the page showing takes the drop", at(ShelfDropSpot.Card(0), shown = 2) == ShelfDrop.ToPage("bin:later") && at(ShelfDropSpot.Card(0), shown = 2, canReorder = false) == ShelfDrop.ToPage("bin:later"))
        report.check("r73b/drop: a picker tile is its page", at(ShelfDropSpot.Tile("bin:later")) == ShelfDrop.ToPage("bin:later"))
        report.check("r73b/drop: the tile of the page showing (You are here) takes nothing", at(ShelfDropSpot.Tile("bin:next")) == ShelfDrop.Nothing)
        report.check("r73b/drop: Cancel cancels", at(ShelfDropSpot.Cancel) == ShelfDrop.Cancel)
        report.check("r73b/drop: the bar itself and anywhere else put the row back", at(ShelfDropSpot.Bar) == ShelfDrop.Nothing && at(ShelfDropSpot.Elsewhere) == ShelfDrop.Nothing)
        report.check("r73b/drop: let go without moving selects, wherever the finger is", listOf(ShelfDropSpot.Strip, ShelfDropSpot.Cancel, ShelfDropSpot.Card(0), ShelfDropSpot.Elsewhere).all { at(it, travelled = false) == ShelfDrop.Select })
        report.check("r73b/drop: the zone's page", ShelfDrag.zoneTarget(bins, 1, -1, false) == "bin:sort" && ShelfDrag.zoneTarget(bins, 2, 1, false) == null && ShelfDrag.zoneTarget(bins, 2, 1, true) == "bin:sort")
        report.check("r73b/drag: the timings", ShelfDrag.LIFT_MS == 450L && ShelfDrag.PICKER_HOLD_MS == 300L && ShelfDrag.PAGE_TURN_MS == 600L && ShelfDrag.ZONE_DP == 58)
    }

    private fun selection(report: CheckReport) {
        val picked = listOf(id(1), id(4))
        report.check("r73b/select: a hold on a selected row lifts the whole selection", ShelfDrag.lifted(id(4), true, picked) == picked)
        report.check("r73b/select: a hold on another row lifts just that row", ShelfDrag.lifted(id(2), true, picked) == listOf(id(2)) && ShelfDrag.lifted(id(1), false, picked) == listOf(id(1)))
        report.check("r73b/select: let go without moving starts selecting with that row", ShelfDrag.selectionAfter(ShelfDrop.Select, listOf(id(2)), false, emptyList()) == (true to listOf(id(2))))
        report.check("r73b/select: in select mode it joins the selection, once", ShelfDrag.selectionAfter(ShelfDrop.Select, listOf(id(2)), true, picked) == (true to picked + id(2)) && ShelfDrag.selectionAfter(ShelfDrop.Select, picked, true, picked) == (true to picked))
        report.check("r73b/select: Cancel keeps the selection", ShelfDrag.selectionAfter(ShelfDrop.Cancel, picked, true, picked) == (true to picked))
        report.check("r73b/select: a release that moves nothing keeps it too", ShelfDrag.selectionAfter(ShelfDrop.Nothing, picked, true, picked) == (true to picked))
        report.check("r73b/select: Cancel of a single lifted row doesn't start selecting", ShelfDrag.selectionAfter(ShelfDrop.Cancel, listOf(id(2)), false, emptyList()) == (false to emptyList<String>()))
        report.check("r73b/select: a drop ends select mode", listOf(ShelfDrop.ToQueued, ShelfDrop.ToPage("bin:later"), ShelfDrop.Reorder(0)).all { ShelfDrag.selectionAfter(it, picked, true, picked) == (false to emptyList<String>()) })
        report.check("r73b/select: a tap toggles a row", ShelfDrag.toggled(picked, id(2)) == picked + id(2) && ShelfDrag.toggled(picked, id(1).uppercase()) == listOf(id(4)))
        report.check("r73b/select: Select all takes the rows of the page showing, keeping the others", ShelfDrag.selectAll(listOf(id(5)), listOf(id(1), id(2))) == listOf(id(5), id(1), id(2)))
        report.check("r73b/select: with all of them selected it unselects them", ShelfDrag.selectAll(listOf(id(5), id(1), id(2)), listOf(id(1), id(2))) == listOf(id(5)))
        report.check("r73b/select: tasks that are gone leave the selection", ShelfDrag.pruned(picked, listOf(id(4), id(9))) == listOf(id(4)))
        report.check("r73b/select: the header's words", ShelfDrag.selectedText(2) == "2 selected" && ShelfDrag.selectAllText(picked, listOf(id(1), id(2), id(3), id(4))) == "Select all 4" && ShelfDrag.selectAllText(picked, picked) == "Unselect all")
    }

    private fun stack(report: CheckReport) {
        val lists = world()
        // This session is empty here: a drop on the strip still goes to Queued, never This session (the PC's rule).
        val strip = ShelfEdits.stripDrop(lists, listOf(id(1), id(4), id(5)), PHONE, now)
        report.check("r73b/stack: a stack on the strip lands in Queued, also with This session empty", listOf(1, 4, 5).all { strip.lists.of(it).task.bin == TaskBin.QUEUED } && strip.ids.size == 3)
        report.check("r73b/stack: nothing reaches This session by the strip", strip.lists.tasks.none { it.task.bin == TaskBin.SESSION } && strip.queuedInstead == 0)
        report.check("r73b/stack: the stack lands at the end in its order", strip.lists.order(TaskBin.QUEUED) == listOf(8, 1, 4, 5))
        val pages = ShelfPages.build(lists, TaskViewConfig.EVERYTHING, now, zone)
        val later = ShelfEdits.dropOnPage(lists, listOf(id(1), id(2), id(5)), pages[2], PHONE, now)
        report.check("r73b/stack: a stack on a bin page moves the ones not there yet", later.ids == listOf(id(1), id(2)) && later.lists.order(TaskBin.LATER) == listOf(5, 6, 1, 2))
        report.check("r73b/stack: the toast counts what moved", ShelfToasts.dropped(later.ids.size, pages[2]) == "2 tasks moved to Later")
        val labelPages = ShelfPages.build(lists, TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_LABEL), now, zone)
        val toAi = ShelfEdits.dropOnPage(lists, listOf(id(1), id(3), id(2)), labelPages.first { it.title == "AI" }, PHONE, now)
        report.check("r73b/stack: a stack on a label page re-labels each, keeping bins and tags", toAi.ids == listOf(id(1), id(3)) && toAi.lists.of(1).task.let { it.labelId == ai.id && it.bin == TaskBin.NEXT && it.tags == listOf("Release") })
        report.check("r73b/stack: a stack's name", ShelfDrag.stackText(3) == "3 tasks" && ShelfDrag.stackText(1) == "1 task" && ShelfDrag.MAX_BACK_CARDS == 2)
    }

    private fun picker(report: CheckReport) {
        val lists = world()
        val labels = lists.labels.map { it.label }
        val binPages = ShelfPages.build(lists, TaskViewConfig.EVERYTHING, now, zone)
        val binTiles = ShelfPicker.tiles(TaskViewConfig.GROUP_BINS, binPages, labels, "bin:next")
        report.check("r73b/picker: bins are To sort, Next and Later", binTiles.map { it.page.title } == listOf("To sort", "Next", "Later"))
        report.check("r73b/picker: the page showing is You are here, the others count their tasks", binTiles.map { it.detail } == listOf("1 task", "You are here", "2 tasks") && binTiles.map { it.here } == listOf(false, true, false))
        val labelPages = ShelfPages.build(lists, TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_LABEL), now, zone)
        val labelTiles = ShelfPicker.tiles(TaskViewConfig.GROUP_LABEL, labelPages, labels, labelPages.first { it.title == "ePT" }.key)
        report.check("r73b/picker: labels in page order, then the ones with no task, then No label", labelTiles.map { it.page.title } == listOf("AI", "ePT", "SDA", "No label"), labelTiles.map { it.page.title }.toString())
        report.check("r73b/picker: a label with no task is still a target", labelTiles[2].let { it.detail == "No tasks" && it.page.labelId == sda.id && it.page.key == "label:" + sda.id.lowercase() })
        report.check("r73b/picker: No label is always there", ShelfPicker.tiles(TaskViewConfig.GROUP_LABEL, labelPages.filter { !it.isNone }, labels, null).last().page.key == TaskViewRules.LABEL_NONE)
        val dropped = ShelfEdits.dropOnPage(lists, listOf(id(1)), labelTiles[2].page, PHONE, now)
        report.check("r73b/picker: a drop on a label's tile gives that label", dropped.lists.of(1).task.labelId == sda.id)
        val tagPages = ShelfPages.build(lists, TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_TAG), now, zone)
        val tagTiles = ShelfPicker.tiles(TaskViewConfig.GROUP_TAG, tagPages, labels, null)
        report.check("r73b/picker: tags have a tile each and no No tag", tagTiles.map { it.page.title } == listOf("#launch-prep", "#Release"))
        val one = ShelfPages.build(lists, TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_NONE), now, zone)
        val noneTiles = ShelfPicker.tiles(null, one, labels, "all")
        report.check("r73b/picker: one list picks among the bins, none of them here", noneTiles.map { it.page.bin } == listOf(TaskBin.SORT, TaskBin.NEXT, TaskBin.LATER) && noneTiles.none { it.here } && noneTiles.map { it.detail } == listOf("1 task", "4 tasks", "2 tasks"))
        report.check("r73b/picker: the bar's words", ShelfPicker.barText(TaskViewConfig.GROUP_BINS, false) == "Hold to pick a bin" && ShelfPicker.barText(TaskViewConfig.GROUP_LABEL, false) == "Hold to pick a label" && ShelfPicker.barText(TaskViewConfig.GROUP_LABEL, true) == "Release on a label above")
        report.check("r73b/picker: the title and the note", ShelfPicker.title(TaskViewConfig.GROUP_LABEL) == "Drop on a label" && ShelfPicker.title(null) == "Drop on a bin" && ShelfPicker.note(TaskViewConfig.GROUP_LABEL) == "In bin view this shows To sort, Next and Later. Changed your mind? Drop on Cancel.")
    }

    private fun reorder(report: CheckReport) {
        val lists = world()
        val next = listOf(id(1), id(2), id(3), id(4))
        val down = ShelfReorder.place(lists, next, listOf(id(1)), 2, PHONE, now)
        report.check("r73b/reorder: a row dragged down takes its place", down.lists.order(TaskBin.NEXT) == listOf(2, 3, 1, 4) && down.ids == listOf(id(1)))
        report.check("r73b/reorder: only its place changes, as this phone's bin edit", down.lists.of(1).let { it.task.bin == TaskBin.NEXT && it.task.labelId == ept.id && it.stamps.of(TaskField.BIN)?.by == PHONE && it.stamps.of(TaskField.LABEL)?.by == PC })
        report.check("r73b/reorder: to the end", ShelfReorder.place(lists, next, listOf(id(2)), 3, PHONE, now).lists.order(TaskBin.NEXT) == listOf(1, 3, 4, 2))
        report.check("r73b/reorder: to the top", ShelfReorder.place(lists, next, listOf(id(4)), 0, PHONE, now).lists.order(TaskBin.NEXT) == listOf(4, 1, 2, 3))
        report.check("r73b/reorder: let go at its own place changes nothing", ShelfReorder.place(lists, next, listOf(id(2)), 1, PHONE, now).let { !it.changed && it.lists === lists })
        val stack = ShelfReorder.place(lists, next, listOf(id(4), id(1)), 1, PHONE, now)
        report.check("r73b/reorder: a stack gathers at the place, in the rows' order", stack.lists.order(TaskBin.NEXT) == listOf(2, 1, 4, 3), stack.lists.order(TaskBin.NEXT).toString())
        report.check("r73b/reorder: a stack to the end", ShelfReorder.place(lists, next, listOf(id(1), id(2)), 2, PHONE, now).lists.order(TaskBin.NEXT) == listOf(3, 4, 1, 2))
        // A filtered page: only 1, 3 and 4 show; 2 is hidden between them and keeps its place.
        val filtered = ShelfReorder.place(lists, listOf(id(1), id(3), id(4)), listOf(id(4)), 1, PHONE, now)
        report.check("r73b/reorder: on a filtered page the row goes right before the row below it", filtered.lists.order(TaskBin.NEXT) == listOf(1, 2, 4, 3), filtered.lists.order(TaskBin.NEXT).toString())
        val filteredEnd = ShelfReorder.place(lists, listOf(id(1), id(3)), listOf(id(1)), 1, PHONE, now)
        report.check("r73b/reorder: with no row below, right after the row above (before what follows it)", filteredEnd.lists.order(TaskBin.NEXT) == listOf(2, 3, 1, 4), filteredEnd.lists.order(TaskBin.NEXT).toString())
        // A label's page shows two bins: Next 1, 4 and Later 5.
        val mixed = listOf(id(1), id(4), id(5))
        val across = ShelfReorder.place(lists, mixed, listOf(id(1)), 2, PHONE, now)
        report.check("r73b/reorder: on a page with two bins a row keeps its bin and goes after its own bin's row", across.lists.of(1).task.bin == TaskBin.NEXT && across.lists.order(TaskBin.NEXT) == listOf(2, 3, 4, 1) && across.lists.order(TaskBin.LATER) == listOf(5, 6))
        report.check("r73b/reorder: alone in its bin on the page it stays", !ShelfReorder.place(lists, mixed, listOf(id(5)), 0, PHONE, now).changed)
        val undone = ShelfEdits.restore(down.lists, down.before, listOf(TaskField.BIN), PHONE, now.plusSeconds(2))
        report.check("r73b/reorder: Undo puts it back at its old place", undone.lists.order(TaskBin.NEXT) == listOf(1, 2, 3, 4) && undone.lists.of(1).task.position == 1.0)
        report.check("r73b/reorder: one row up or down without dragging", ShelfReorder.step(lists, next, id(2), 1, PHONE, now).lists.order(TaskBin.NEXT) == listOf(1, 3, 2, 4) && ShelfReorder.step(lists, next, id(2), -1, PHONE, now).lists.order(TaskBin.NEXT) == listOf(2, 1, 3, 4))
        report.check("r73b/reorder: the top row can't go up, the last one can't go down", !ShelfReorder.step(lists, next, id(1), -1, PHONE, now).changed && !ShelfReorder.step(lists, next, id(4), 1, PHONE, now).changed)
        report.check("r73b/reorder: what is drawn while dragging", ShelfReorder.displayed(listOf("a", "b", "c", "d"), listOf("D", "a"), 1) { it } == listOf("b", "a", "d", "c") && ShelfReorder.displayed(listOf("a", "b"), listOf("a"), 9) { it } == listOf("b", "a"))
        report.check("r73b/reorder: only in Your order", ShelfReorder.canReorder(TaskViewConfig.EVERYTHING) && !ShelfReorder.canReorder(TaskViewConfig.EVERYTHING.copy(sort = TaskViewRules.SORT_NEWEST)) && !ShelfReorder.canReorder(TaskViewConfig.EVERYTHING.copy(sort = TaskViewRules.SORT_SHUFFLE)))
    }

    private fun session(report: CheckReport) {
        // This session: 11 (one action), 12 (two focused steps): full. 13 waits in Queued, 14 in Next with two open steps.
        val steps = listOf(TaskStep(id(80), "a", focus = true), TaskStep(id(81), "b", focus = true))
        val lists = TaskLists(
            listOf(
                task(11, TaskBin.SESSION, 1.0),
                task(12, TaskBin.SESSION, 2.0) { it.copy(steps = steps) },
                task(13, TaskBin.QUEUED, 1.0),
                task(14, TaskBin.NEXT, 1.0) { it.copy(steps = listOf(TaskStep(id(82), "c"), TaskStep(id(83), "d"))) },
            ),
            emptyList(), emptyList(), emptyList(),
        )
        report.check("r73b/session: a full This session has no room", ShelfSession.needsRoom(lists, listOf(id(13), id(11))) == listOf(id(13)))
        val room = lists.copy(tasks = lists.tasks.filter { it.id != id(12) })
        report.check("r73b/session: room is taken one after the other", ShelfSession.needsRoom(room, listOf(id(13), id(14), id(99))) == emptyList<String>() && ShelfSession.needsRoom(room.copy(tasks = room.tasks + task(15, TaskBin.NEXT, 2.0) + task(16, TaskBin.NEXT, 3.0)), listOf(id(13), id(15), id(16))) == listOf(id(16)))
        report.check("r73b/session: every task of This session can be swapped for a one-action newcomer", ShelfSession.slots(lists, id(13)).map { it.id } == listOf(id(11), id(12)))
        val swap = ShelfSession.swap(lists, id(13), id(11), PHONE, now)
        report.check("r73b/session: the swap sends the other one to Queued", swap.lists.of(11).task.bin == TaskBin.QUEUED && swap.lists.order(TaskBin.QUEUED).last() == 11)
        report.check("r73b/session: the newcomer takes its place in This session", swap.lists.of(13).task.let { it.bin == TaskBin.SESSION && it.position == 1.0 } && swap.lists.order(TaskBin.SESSION) == listOf(13, 12))
        report.check("r73b/session: after a swap the cap still holds", swap.lists.tasks.filter { it.task.bin == TaskBin.SESSION }.sumOf { it.task.actionCount } == 3)
        val back = ShelfEdits.restore(swap.lists, swap.before, ShelfEdits.MOVE_FIELDS, PHONE, now.plusSeconds(2))
        report.check("r73b/session: Undo after a swap restores both", back.lists.of(11).task.let { it.bin == TaskBin.SESSION && it.position == 1.0 } && back.lists.of(13).task.let { it.bin == TaskBin.QUEUED && it.position == 1.0 })
        report.check("r73b/session: a task that isn't in This session can't be swapped out", !ShelfSession.swap(lists, id(13), id(14), PHONE, now).changed)
        report.check("r73b/session: a swap is refused once the newcomer is in already", !ShelfSession.swap(swap.lists, id(13), id(12), PHONE, now).changed)
        val over = ShelfSession.addAnyway(lists, listOf(id(13)), PHONE, now)
        report.check("r73b/session: Add anyway goes over the cap", over.lists.of(13).task.bin == TaskBin.SESSION && over.queuedInstead == 0 && over.lists.tasks.filter { it.task.bin == TaskBin.SESSION }.sumOf { it.task.actionCount } == 4)
        report.check("r73b/session: without Add anyway the cap sends it to Queued", ShelfEdits.move(lists, listOf(id(14)), TaskBin.SESSION, PHONE, now).let { it.lists.of(14).task.bin == TaskBin.QUEUED && it.queuedInstead == 1 })
        report.check("r73b/session: the swap's toast", ShelfSession.swappedText("Fix the tray icon") == "Swapped · “Fix the tray icon” → Queued" && ShelfSession.swappedText("A".repeat(40)).length < 40 + 20)
    }

    private fun undo(report: CheckReport) {
        val lists = world()
        val labelPages = ShelfPages.build(lists, TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_LABEL), now, zone)
        val toAi = ShelfEdits.dropOnPage(lists, listOf(id(1), id(3), id(5)), labelPages.first { it.title == "AI" }, PHONE, now)
        val labelsBack = ShelfEdits.restore(toAi.lists, toAi.before, ShelfEdits.MOVE_FIELDS, PHONE, now.plusSeconds(2))
        report.check("r73b/undo: a stack's labels come back, each its own (also none)", labelsBack.lists.of(1).task.labelId == ept.id && labelsBack.lists.of(3).task.labelId == null && labelsBack.lists.of(5).task.labelId == ept.id)
        report.check("r73b/undo: Undo's fields name the label and the tags", TaskField.LABEL in ShelfEdits.MOVE_FIELDS && TaskField.TAGS in ShelfEdits.MOVE_FIELDS && TaskField.BIN in ShelfEdits.MOVE_FIELDS)
        val tagPages = ShelfPages.build(lists, TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_TAG), now, zone)
        val tagged = ShelfEdits.dropOnPage(lists, listOf(id(1), id(2)), tagPages.first { it.title == "#launch-prep" }, PHONE, now)
        val tagsBack = ShelfEdits.restore(tagged.lists, tagged.before, ShelfEdits.MOVE_FIELDS, PHONE, now.plusSeconds(2))
        report.check("r73b/undo: the tags come back (also to none)", tagsBack.lists.of(1).task.tags == listOf("Release") && tagsBack.lists.of(2).task.tags == null)
        val binPages = ShelfPages.build(lists, TaskViewConfig.EVERYTHING, now, zone)
        val moved = ShelfEdits.dropOnPage(lists, listOf(id(2), id(3)), binPages[2], PHONE, now)
        val binsBack = ShelfEdits.restore(moved.lists, moved.before, ShelfEdits.MOVE_FIELDS, PHONE, now.plusSeconds(2))
        report.check("r73b/undo: the bin and the place come back", binsBack.lists.order(TaskBin.NEXT) == listOf(1, 2, 3, 4) && binsBack.lists.order(TaskBin.LATER) == listOf(5, 6) && binsBack.lists.of(2).task.position == 2.0)
        report.check("r73b/undo: label and tags untouched by a bin move and its Undo", binsBack.lists.of(2).task.labelId == ai.id && binsBack.lists.of(2).stamps.of(TaskField.LABEL)?.by == PC)
        val strip = ShelfEdits.stripDrop(lists, listOf(id(1), id(5)), PHONE, now)
        val stripBack = ShelfEdits.restore(strip.lists, strip.before, ShelfEdits.MOVE_FIELDS, PHONE, now.plusSeconds(2))
        report.check("r73b/undo: off the strip, back in their bins at their places with label and tags", stripBack.lists.of(1).task.let { it.bin == TaskBin.NEXT && it.position == 1.0 && it.labelId == ept.id && it.tags == listOf("Release") } && stripBack.lists.of(5).task.let { it.bin == TaskBin.LATER && it.position == 1.0 })
        // A task changed on both ends meanwhile keeps the other field's newer edit: Undo only puts back what the drop changed.
        val renamed = ShelfEdits.rename(toAi.lists, id(1), "Renamed meanwhile", PHONE, now.plusSeconds(1))
        report.check("r73b/undo: Undo leaves a rename made meanwhile", ShelfEdits.restore(renamed.lists, toAi.before, ShelfEdits.MOVE_FIELDS, PHONE, now.plusSeconds(2)).lists.of(1).task.let { it.title == "Renamed meanwhile" && it.labelId == ept.id })
    }

    private fun words(report: CheckReport) {
        report.check("r73b/toast: the board's lines", ShelfToasts.moved(2, TaskBin.LATER) == "2 tasks moved to Later" && ShelfToasts.see(2, inToday = false) == "Tap to see them there")
        report.check("r73b/toast: into Today", ShelfToasts.moved(3, TaskBin.QUEUED) == "3 tasks added to Today · Queued" && ShelfToasts.see(3, inToday = true) == "Tap to see them in Today")
        report.check("r73b/toast: one task", ShelfToasts.moved(1, TaskBin.NEXT) == "1 task moved to Next" && ShelfToasts.see(1, inToday = false) == "Tap to see it there" && ShelfToasts.show(1, "Later") == "Show it in Later")
        report.check("r73b/toast: 4 s plain, 6 s with Undo, 7 s for an explanation, 8 s waiting", ShelfToasts.lengthOf(false).millis == 4_000L && ShelfToasts.lengthOf(true).millis == 6_000L && ShelfToasts.lengthOf(true, explains = true).millis == 7_000L && ShelfToastLength.WAITING.millis == 8_000L)
        report.check("r73b/toast: a long title is cut to quote it", ShelfToasts.short("Short one") == "Short one" && ShelfToasts.short("A very long task title that goes on and on").let { it.length <= ShelfToasts.SHORT_TITLE && it.endsWith("…") })
        report.check("r73b/ghost: one task keeps its label and detail line", ShelfDrag.ghostLine("ePT", listOf("#Release #launch-prep"), "#Release #launch-prep", 1) == "ePT · #Release #launch-prep")
        report.check("r73b/ghost: over a label it says the change", ShelfDrag.ghostLine("ePT", listOf("50 min", "#Release"), "#Release", 1, toLabel = "mPT") == "ePT → mPT · #Release")
        report.check("r73b/ghost: a stack names the rest", ShelfDrag.ghostLine("AI", listOf("50 min"), null, 2) == "AI · + 1 more" && ShelfDrag.ghostLine("ePT", emptyList(), null, 2, toLabel = "SDA") == "ePT → SDA · + 1 more")
        report.check("r73b/ghost: no label", ShelfDrag.ghostLine(null, emptyList(), null, 3) == "+ 2 more" && ShelfDrag.ghostLine(null, emptyList(), null, 1, toLabel = "AI") == "No label → AI")
        report.check("r73b/strip: the lit strip's words", ShelfDrag.stripLead(false) == "Drop to add to " && ShelfDrag.stripLead(true) == "Or drop to add to ")
    }

    private fun add(report: CheckReport) {
        val lists = world()
        val labels = lists.labels.map { it.label }
        val typed = TaskTitleHelp.parse("Email Sam about the staging keys @ePT #Release", labels)
        report.check("r73b/add: the board's line", typed.title == "Email Sam about the staging keys" && typed.labelId == ept.id && typed.tags == listOf("Release"))
        val text = "Email Sam @ePT #Release"
        report.check("r73b/add: the words the field tints", TaskTitleHelp.marks(text, labels).map { text.substring(it.first, it.last + 1) } == listOf("@ePT", "#Release"))
        report.check("r73b/add: an unknown @word isn't tinted", TaskTitleHelp.marks("ask @nobody #x", labels).map { "ask @nobody #x".substring(it.first, it.last + 1) } == listOf("#x"))
        report.check("r73b/add: a chip's × takes its word out", TaskTitleHelp.without(text, "@ept") == "Email Sam #Release" && TaskTitleHelp.without(text, "#Release") == "Email Sam @ePT" && TaskTitleHelp.without("#a #ab", "#a") == "#ab")
        report.check("r73b/add: the counter shows from 50 characters", TaskTitleHelp.counter("x".repeat(49)) == null && TaskTitleHelp.counter("x".repeat(52)) == "52 / 60" && !TaskTitleHelp.isLong("x".repeat(60)) && TaskTitleHelp.isLong("x".repeat(61)))
        val added = ShelfEdits.add(
            lists, id(50), typed.title, TaskBin.NEXT, typed.labelId, typed.tags, PHONE, now,
            estimateMinutes = 25, note = "  call first ", steps = listOf("find the keys", "  ", "send them"), stepId = { id(60 + it) },
        )
        val fresh = added.lists.of(50).task
        report.check("r73b/add: label, tags, time limit and note", fresh.labelId == ept.id && fresh.tags == listOf("Release") && fresh.estimateMinutes == 25 && fresh.note == "call first")
        report.check("r73b/add: steps, blank ones dropped, none in focus", fresh.steps?.map { it.title } == listOf("find the keys", "send them") && fresh.steps!!.none { it.focus || it.isDone } && fresh.steps!!.map { it.id }.distinct().size == 2)
        report.check("r73b/add: no time limit for 0, a long one is held", ShelfEdits.add(lists, id(51), "x", TaskBin.NEXT, null, null, PHONE, now, estimateMinutes = 0).lists.of(51).task.estimateMinutes == null && ShelfEdits.add(lists, id(52), "x", TaskBin.NEXT, null, null, PHONE, now, estimateMinutes = 99_999).lists.of(52).task.estimateMinutes == ShelfEdits.MAX_ESTIMATE_MINUTES)
        val several = ShelfEdits.editAll(lists, listOf(id(1), id(2), id(3)), TaskField.ESTIMATE, PHONE, now) { it.copy(estimateMinutes = 50) }
        report.check("r73b/bulk: one field of several tasks, each stamped", several.ids.size == 3 && listOf(1, 2, 3).all { several.lists.of(it).let { item -> item.task.estimateMinutes == 50 && item.stamps.of(TaskField.ESTIMATE)?.by == PHONE } })
    }

    private fun rings(report: CheckReport) {
        val reminded = { item: TaskItem -> item.copy(reminders = listOf(TaskReminder(id(90), TaskReminder.TEN_MINUTES_BEFORE))) }
        val base = world()
        val lists = base.copy(tasks = base.tasks.map { if (it.id == id(1) || it.id == id(3)) it.copy(task = reminded(it.task)) else it })
        report.check("r73b/rings: no lists: every device rings", TaskReminderRings.ringsHere(lists, lists.of(1).task, PHONE))
        val account = lists.copy(account = SyncTaskAccountSettings(ringOn = listOf(TABLET), ringOnStamp = FieldStamp(created, PC)))
        report.check("r73b/rings: the account's list leaves this phone out", !TaskReminderRings.ringsHere(account, account.of(1).task, PHONE) && TaskReminderRings.ringsHere(account, account.of(1).task, TABLET))
        val quiet = lists.copy(labels = lists.labels.map { if (it.label.id == ept.id) SyncTaskLabel(it.label.copy(ringOn = listOf(TABLET))) else it })
        report.check("r73b/rings: a label's list leaves this phone out for its tasks only", !TaskReminderRings.ringsHere(quiet, quiet.of(1).task, PHONE) && TaskReminderRings.ringsHere(quiet, quiet.of(3).task, PHONE))
        val both = quiet.copy(account = SyncTaskAccountSettings(ringOn = listOf(PHONE), ringOnStamp = FieldStamp(created, PC)))
        report.check("r73b/rings: both lists must name the phone", !TaskReminderRings.ringsHere(both, both.of(1).task, PHONE) && TaskReminderRings.ringsHere(both, both.of(3).task, PHONE) && !TaskReminderRings.ringsHere(both, both.of(3).task, TABLET))
        val deleted = quiet.copy(labels = quiet.labels.map { if (it.label.id == ept.id) SyncTaskLabel(it.label.deleted(now)) else it })
        report.check("r73b/rings: a deleted label's list no longer counts", TaskReminderRings.ringsHere(deleted, deleted.of(1).task, PHONE))
        report.check("r73b/rings: the alarms' list holds only tasks that ring here", TaskReminderRings.ringing(quiet, PHONE).map { it.id } == listOf(id(3)) && TaskReminderRings.ringing(lists, PHONE).map { it.id } == listOf(id(1), id(3)))
        report.check("r73b/rings: the line under a task's reminders", TaskReminderRings.line(quiet, quiet.of(1).task, PHONE) == "Doesn't ring on this phone (Rings on)" && TaskReminderRings.line(quiet, quiet.of(3).task, PHONE) == "Rings on this phone" && TaskReminderRings.line(quiet, quiet.of(2).task, PHONE) == null)
    }

    private fun file(name: String, kind: String, withTasks: Boolean, oldLists: Boolean): SyncFile {
        val card = SyncDeviceCard(UUID.nameUUIDFromBytes(name.toByteArray()), name, kind, "0.1.133", Instant.parse("2026-11-12T16:00:00Z"), emptyList(), false, emptyMap())
        return SyncFile(
            1, card, emptyList(), emptyList(), null, emptyList(),
            nextTasks = if (oldLists) emptyList() else null,
            parked = if (oldLists) emptyList() else null,
            later = if (oldLists) emptyList() else null,
            taskParts = if (withTasks) TaskParts(world().tasks, world().labels, emptyList(), emptyList(), null) else null,
        )
    }

    // The copy the phone keeps of a received file, as its sync does it, written to its store and read back.
    private fun kept(file: SyncFile, held: SyncFile? = null): SyncFile {
        val mark = if (file.parked != null || held?.parked != null) emptyList<com.accountabilibuddy.shared.sync.SyncParked>() else null
        val copy = SyncFileStrip.keptCopy(file, mark, null, SyncFileStrip.tasksMark(file, held))
        return SyncFileJson.read(SyncFileJson.write(copy)) ?: copy
    }

    private fun notice(report: CheckReport) {
        val updated = file("DESKTOP-A", SyncDeviceCard.KIND_PC, withTasks = true, oldLists = true)
        val old = file("Den", SyncDeviceCard.KIND_PC, withTasks = false, oldLists = true)
        val keptUpdated = kept(updated)
        report.check("r73b/notice: the kept copy of a PC that writes `tasks` holds no task, only the mark", keptUpdated.taskParts?.tasks?.isEmpty() == true && keptUpdated.taskParts?.labels == null && keptUpdated.nextTasks == null && keptUpdated.later == null)
        report.check("r73b/notice: a PC with `tasks` and lists: after the kept copy, no notice", TaskListUpdateNotice.pcsBehind(listOf(keptUpdated)).isEmpty())
        report.check("r73b/notice: a PC with lists but no `tasks`: after the kept copy, the notice", TaskListUpdateNotice.pcsWithoutTasks(listOf(kept(old))) == listOf("Den"))
        report.check("r73b/notice: the whole file reads the same way", TaskListUpdateNotice.pcsBehind(listOf(updated)).isEmpty() && TaskListUpdateNotice.pcsWithoutTasks(listOf(old)) == listOf("Den"))
        report.check("r73b/notice: without the mark the kept copy would name every PC (the bug)", TaskListUpdateNotice.pcsWithoutTasks(listOf(SyncFileStrip.keptCopy(updated, emptyList(), null))) == listOf("DESKTOP-A"))
        report.check("r73b/notice: a QR file after a full one keeps the mark", TaskListUpdateNotice.pcsBehind(listOf(kept(file("DESKTOP-A", SyncDeviceCard.KIND_PC, withTasks = false, oldLists = false), held = keptUpdated))).isEmpty())
        report.check("r73b/notice: a full file without `tasks` after one with it drops the mark (the PC went back)", TaskListUpdateNotice.pcsWithoutTasks(listOf(kept(file("DESKTOP-A", SyncDeviceCard.KIND_PC, withTasks = false, oldLists = true), held = keptUpdated))) == listOf("DESKTOP-A"))
        report.check("r73b/notice: a phone or a PC that shares no lists is never named", TaskListUpdateNotice.pcsBehind(listOf(kept(file("Pixel", SyncDeviceCard.KIND_PHONE, false, true)), kept(file("Studio", SyncDeviceCard.KIND_PC, false, false)))).isEmpty())
        val names = listOf("DESKTOP-0GUCMA2", "WTN72WFT6", "FABTZEPHYRUSG15")
        report.check("r73b/notice: one line per device under the heading", TaskListUpdateNotice.lines(names) == listOf("Update AccountabiliBuddy on:") + names + "Their task edits don't reach this phone until they are updated.")
        report.check("r73b/notice: one device", TaskListUpdateNotice.lines(listOf("Den")) == listOf("Update AccountabiliBuddy on:", "Den", "Its task edits don't reach this phone until it is updated."))
        report.check("r73b/notice: no device, no lines", TaskListUpdateNotice.lines(emptyList()).isEmpty() && TaskListUpdateNotice.text(emptyList()) == null)
        val at = Instant.parse("2026-11-12T17:00:00Z")
        val behind = listOf("pc-a", "pc-b")
        report.check("r73b/notice: never closed: it shows", TaskListUpdateNotice.shows(behind, emptyList(), null, at))
        report.check("r73b/notice: × hides it", !TaskListUpdateNotice.shows(behind, behind, at, at) && !TaskListUpdateNotice.shows(behind, behind, at, at.plus(Duration.ofDays(3)).minusSeconds(1)))
        report.check("r73b/notice: back after 3 days while still behind", TaskListUpdateNotice.shows(behind, behind, at, at.plus(Duration.ofDays(3))) && TaskListUpdateNotice.SNOOZE == Duration.ofDays(3))
        report.check("r73b/notice: a new device behind shows it at once", TaskListUpdateNotice.shows(behind + "pc-c", behind, at, at.plusSeconds(60)))
        report.check("r73b/notice: fewer devices behind stay closed", !TaskListUpdateNotice.shows(listOf("PC-A"), behind, at, at.plusSeconds(60)))
        report.check("r73b/notice: gone when none is behind", !TaskListUpdateNotice.shows(emptyList(), behind, at, at.plus(Duration.ofDays(30))) && !TaskListUpdateNotice.shows(emptyList(), emptyList(), null, at))
        report.check("r73b/notice: a close stamped in the future is pulled back to now", TaskListUpdateNotice.dismissedAt(at.plus(Duration.ofDays(9)), at) == at && TaskListUpdateNotice.dismissedAt(at, at.plusSeconds(5)) == at && TaskListUpdateNotice.dismissedAt(null, at) == null)
    }

    private fun steps(report: CheckReport) {
        val step = { n: Int, title: String -> TaskStep("5737e900-0000-0000-0000-%012d".format(n), title) }
        val base = world()
        // Next: 1 (steps a, b, c; 50 min), 2, 3, 4.
        val lists = base.copy(tasks = base.tasks.map { if (it.id == id(1)) it.copy(task = it.task.copy(estimateMinutes = 50, steps = listOf(step(1, "a"), step(2, "b").copy(estimateMinutes = 20), step(3, "c").copy(estimateMinutes = 15)))) else it })
        report.check("r73b/steps: the heading counts and adds up the steps' times", ShelfSteps.heading(lists.of(1).task) == "Steps · 0/3 · add up to 35 min of 50" && ShelfSteps.heading(lists.of(2).task) == null && ShelfSteps.heading(lists.of(1).task.copy(estimateMinutes = null)) == "Steps · 0/3")
        val added = ShelfSteps.add(lists, id(1), step(4, "").id, "  d  ", PHONE, now)
        report.check("r73b/steps: a step is added at the end, stamped by this phone", added.lists.of(1).let { it.task.steps!!.map { s -> s.title } == listOf("a", "b", "c", "d") && it.stamps.of(TaskField.STEPS)?.by == PHONE })
        report.check("r73b/steps: a blank step adds nothing", !ShelfSteps.add(lists, id(1), step(5, "").id, "   ", PHONE, now).changed)
        val up = ShelfSteps.move(lists, id(1), step(3, "").id, -1, PHONE, now)
        report.check("r73b/steps: a step moves up; at the top it can't", up.lists.of(1).task.steps!!.map { it.title } == listOf("a", "c", "b") && !ShelfSteps.move(lists, id(1), step(1, "").id, -1, PHONE, now).changed)
        val removed = ShelfSteps.remove(lists, id(1), step(2, "").id, PHONE, now)
        report.check("r73b/steps: a step is deleted, and Undo brings it back in its place", removed.lists.of(1).task.steps!!.map { it.title } == listOf("a", "c") &&
            ShelfEdits.restore(removed.lists, removed.before, listOf(TaskField.STEPS), PHONE, now.plusSeconds(1)).lists.of(1).task.steps!!.map { it.title } == listOf("a", "b", "c"))
        val own = ShelfSteps.ownTask(lists, id(1), step(2, "").id, id(60), PHONE, now)
        val made = own.lists.of(60).task
        report.check("r73b/steps: Make it its own task: the step leaves, the new task comes right after it in its bin", own.lists.of(1).task.steps!!.map { it.title } == listOf("a", "c") && own.lists.order(TaskBin.NEXT) == listOf(1, 60, 2, 3, 4), own.lists.order(TaskBin.NEXT).toString())
        report.check("r73b/steps: the new task keeps the label, the tags and the step's time", made.title == "b" && made.labelId == ept.id && made.tags == listOf("Release") && made.estimateMinutes == 20 && own.ids == listOf(id(60), id(1)))
        report.check("r73b/steps: a ticked step, or a task in This session, stays as it is / goes to Queued", !ShelfSteps.ownTask(lists.copy(tasks = lists.tasks.map { if (it.id == id(1)) it.copy(task = it.task.copy(steps = listOf(step(1, "a").copy(doneAt = now)))) else it }), id(1), step(1, "").id, id(61), PHONE, now).changed &&
            ShelfSteps.ownTask(lists.copy(tasks = lists.tasks.map { if (it.id == id(1)) it.copy(task = it.task.copy(bin = TaskBin.SESSION)) else it }), id(1), step(1, "").id, id(62), PHONE, now).lists.of(62).task.bin == TaskBin.QUEUED)
        report.check("r73b/steps: the toast", ShelfSteps.ownTaskText("Call the bank") == "Made “Call the bank” its own task")
    }

}
