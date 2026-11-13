package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.tasks.ShelfEdits
import com.accountabilibuddy.shared.tasks.ShelfPages
import com.accountabilibuddy.shared.tasks.ShelfPaging
import com.accountabilibuddy.shared.tasks.SyncTaskItem
import com.accountabilibuddy.shared.tasks.SyncTaskLabel
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskBins
import com.accountabilibuddy.shared.tasks.TaskField
import com.accountabilibuddy.shared.tasks.TaskFieldStamps
import com.accountabilibuddy.shared.tasks.TaskHistoryKind
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskJson
import com.accountabilibuddy.shared.tasks.TaskLabel
import com.accountabilibuddy.shared.tasks.TaskListRules
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskNumber
import com.accountabilibuddy.shared.tasks.TaskOrigins
import com.accountabilibuddy.shared.tasks.TaskPageOrder
import com.accountabilibuddy.shared.tasks.TaskReminder
import com.accountabilibuddy.shared.tasks.TaskRowText
import com.accountabilibuddy.shared.tasks.TaskStep
import com.accountabilibuddy.shared.tasks.TaskView
import com.accountabilibuddy.shared.tasks.TaskViewConfig
import com.accountabilibuddy.shared.tasks.TaskViewFilter
import com.accountabilibuddy.shared.tasks.TaskViewRules
import com.accountabilibuddy.shared.tasks.TodayStripCounts
import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * R7.3a checks with no C# twin (the phone's own rules): the Shelf's paging (start page, neighbours with and without wrap around, dots
 * or a count), the page order kept per grouping, the pages a view gives, the Today strip's words and counts, the Today strip's drop
 * (Queued, never This session, Undo back to the bin), a drop on a label or tag page (label replaced, tag added, Undo restores both),
 * that every change is this phone's stamped edit (ids, numbers and untouched fields kept), a row's words, and that what the page
 * remembers reads back as written.
 */
object TasksPageSelfCheck {
    private const val PC = "aaaaaaaa-0000-0000-0000-00000000000a"
    private const val PHONE = "bbbbbbbb-0000-0000-0000-00000000000b"
    private val zone: ZoneId = ZoneId.of("America/New_York")
    private val created: OffsetDateTime = OffsetDateTime.parse("2026-11-10T13:00:00Z")
    private val now: OffsetDateTime = OffsetDateTime.parse("2026-11-12T17:00:00Z")
    private val ept = TaskLabel(TaskLabel.idFor("ePT"), "ePT", "#2F6BEA", 2.0, created)
    private val ai = TaskLabel(TaskLabel.idFor("AI"), "AI", "#7C4DFF", 1.0, created)

    private fun id(n: Int) = "73737373-0000-0000-0000-%012d".format(n)

    private fun task(n: Int, bin: TaskBin, position: Double, change: (TaskItem) -> TaskItem = { it }): SyncTaskItem =
        SyncTaskItem(change(TaskItem(id(n), "Task $n", bin, position, created, created, created, number = TaskNumber(100 + n))), TaskFieldStamps.allAt(FieldStamp(created, PC)))

    private fun world(): TaskLists = TaskLists(
        listOf(
            task(1, TaskBin.NEXT, 1.0) { it.copy(labelId = ept.id, tags = listOf("Release"), highlight = true, reminders = listOf(TaskReminder(id(90), TaskReminder.TEN_MINUTES_BEFORE)), dueOn = LocalDate.of(2026, 11, 12), dueAt = LocalTime.of(15, 0)) },
            task(2, TaskBin.NEXT, 2.0) { it.copy(labelId = ai.id, estimateMinutes = 50, steps = listOf(TaskStep(id(80), "one", doneAt = created), TaskStep(id(81), "two"), TaskStep(id(82), "three"))) },
            task(3, TaskBin.SORT, 1.0),
            task(4, TaskBin.LATER, 1.0) { it.copy(labelId = ept.id, tags = listOf("launch-prep", "Release")) },
            task(5, TaskBin.QUEUED, 1.0) { it.copy(labelId = ept.id) },
            task(6, TaskBin.SESSION, 1.0),
            task(7, TaskBin.SESSION, 2.0) { it.copy(steps = listOf(TaskStep(id(83), "a", focus = true), TaskStep(id(84), "b", focus = true))) },
            task(8, TaskBin.DONE, 1.0) { it.copy(prevBin = TaskBin.NEXT, doneAt = now.minusHours(1)) },
        ),
        listOf(SyncTaskLabel(ept), SyncTaskLabel(ai)), emptyList(), emptyList(),
    )

    private fun TaskLists.of(n: Int): SyncTaskItem = tasks.first { it.id == id(n) }

    fun run(report: CheckReport) {
        paging(report)
        pageOrder(report)
        pages(report)
        strip(report)
        moves(report)
        drops(report)
        edits(report)
        words(report)
        remembered(report)
    }

    private fun paging(report: CheckReport) {
        val bins = listOf("bin:sort", "bin:next", "bin:later")
        val nine = (1..9).map { "label:$it" }
        report.check("r73a/paging: bins start on Next", ShelfPaging.startIndex(bins, null) == 1)
        report.check("r73a/paging: bins in another order still start on Next", ShelfPaging.startIndex(listOf("bin:next", "bin:later", "bin:sort"), null) == 0)
        report.check("r73a/paging: nine labels start on the middle one", ShelfPaging.startIndex(nine, null) == 4)
        report.check("r73a/paging: four pages start on the second", ShelfPaging.startIndex(nine.take(4), null) == 1)
        report.check("r73a/paging: the stored start page wins", ShelfPaging.startIndex(nine, "label:7") == 6)
        report.check("r73a/paging: a stored start page that isn't shown falls back to the default", ShelfPaging.startIndex(nine, "label:gone") == 4)
        report.check("r73a/paging: no pages", ShelfPaging.startIndex(emptyList(), "x") == 0)
        report.check("r73a/paging: wrap off, the first page has no left neighbour", ShelfPaging.neighbour(0, 3, -1, wrap = false) == null)
        report.check("r73a/paging: wrap off, the last page has no right neighbour", ShelfPaging.neighbour(2, 3, 1, wrap = false) == null)
        report.check("r73a/paging: the middle page has both", ShelfPaging.neighbour(1, 3, -1, false) == 0 && ShelfPaging.neighbour(1, 3, 1, false) == 2)
        report.check("r73a/paging: wrap on, past the last is the first", ShelfPaging.neighbour(8, 9, 1, wrap = true) == 0)
        report.check("r73a/paging: wrap on, before the first is the last", ShelfPaging.neighbour(0, 9, -1, wrap = true) == 8)
        report.check("r73a/paging: one page has no neighbours even with wrap on", ShelfPaging.neighbour(0, 1, 1, true) == null && ShelfPaging.neighbour(0, 1, -1, true) == null)
        report.check("r73a/paging: five pages keep the dots", !ShelfPaging.usesCount(5))
        report.check("r73a/paging: six pages show a count", ShelfPaging.usesCount(6))
        report.check("r73a/paging: the count reads 4 / 9", ShelfPaging.countText(3, 9) == "4 / 9")
        report.check("r73a/paging: the count wraps 9 to 1", ShelfPaging.countText(ShelfPaging.neighbour(8, 9, 1, true)!!, 9) == "1 / 9")
        report.check("r73a/paging: the Options row's summary", ShelfPaging.summary("Next", false) == "Start on Next · wrap around off" && ShelfPaging.summary("ePT", true) == "Start on ePT · wrap around on")
        report.check("r73a/paging: the page stays when the pages change around it", ShelfPaging.keep(nine, nine.filter { it != "label:2" }, 4) == 3)
        report.check("r73a/paging: a page that went away leaves the nearest one", ShelfPaging.keep(nine, nine.dropLast(1), 8) == 7)
    }

    private fun pageOrder(report: CheckReport) {
        val shown = listOf("label:a", "label:c")
        val stored = listOf("label:a", "label:b", "label:c")
        report.check("r73a/order: a dragged list keeps a hidden page's slot", TaskPageOrder.reordered(shown, stored, listOf("label:c", "label:a")) == listOf("label:c", "label:b", "label:a"))
        report.check("r73a/order: the sheet's drag is the same move as one step", TaskPageOrder.movedIndex(shown, stored, 1, 0) == TaskPageOrder.movedBy(shown, stored, "label:c", -1))
        report.check("r73a/order: a drag to its own place changes nothing", TaskPageOrder.movedIndex(shown, stored, 1, 1) == null)
        report.check("r73a/order: pages not stored yet are added once", TaskPageOrder.reordered(listOf("x", "y"), listOf("x"), listOf("y", "x")) == listOf("y", "x"))
        report.check("r73a/order: a key missing from the new order keeps a place", TaskPageOrder.reordered(listOf("x", "y", "z"), null, listOf("z", "x")).sorted() == listOf("x", "y", "z"))
    }

    private fun pages(report: CheckReport) {
        val lists = world()
        val bins = ShelfPages.build(lists, TaskViewConfig.EVERYTHING, now, zone)
        report.check("r73a/pages: bins are the three Shelf bins in order", bins.map { it.key } == listOf("bin:sort", "bin:next", "bin:later"))
        report.check("r73a/pages: a bin page never holds Today's tasks", bins.flatMap { it.tasks }.none { !TaskBins.isShelf(it.bin) })
        val filtered = ShelfPages.build(lists, TaskViewConfig.EVERYTHING, now, zone, chipLabel = ai.id)
        report.check("r73a/pages: a label chip narrows the bins and an empty bin is still a page", filtered.map { it.count } == listOf(0, 1, 0))
        report.check("r73a/pages: the No label chip", ShelfPages.build(lists, TaskViewConfig.EVERYTHING, now, zone, chipLabel = ShelfPages.NO_LABEL).map { it.count } == listOf(1, 0, 0))
        report.check("r73a/pages: the stored order sets the pages", ShelfPages.build(lists, TaskViewConfig.EVERYTHING, now, zone, storedOrder = listOf("bin:later", "bin:sort")).map { it.key } == listOf("bin:later", "bin:sort", "bin:next"))
        val byLabel = ShelfPages.build(lists, TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_LABEL), now, zone)
        report.check("r73a/pages: label pages follow the labels' order, No label last", byLabel.map { it.title } == listOf("AI", "ePT", "No label"))
        report.check("r73a/pages: a label page shows its open Today tasks, never a finished one", byLabel.first { it.title == "ePT" }.tasks.map { it.bin } .toSet() == setOf(TaskBin.NEXT, TaskBin.LATER, TaskBin.QUEUED) && byLabel.flatMap { it.tasks }.none { it.bin == TaskBin.DONE })
        val running = ShelfPages.build(lists, TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_LABEL), now, zone, sessionRuns = true)
        report.check("r73a/pages: while a PC's session runs, This session's tasks are on no page (Queued still is)", running.flatMap { it.tasks }.let { tasks -> tasks.none { it.bin == TaskBin.SESSION } && tasks.any { it.bin == TaskBin.QUEUED } })
        report.check("r73a/pages: a label page carries its colour for the dot", byLabel.first { it.title == "ePT" }.labelColor == "#2F6BEA" && byLabel.last().labelColor == null && byLabel.last().isNone)
        report.check("r73a/pages: a bin chip narrows the label pages", ShelfPages.build(lists, TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_LABEL), now, zone, chipBin = TaskBin.LATER).map { it.title to it.count } == listOf("ePT" to 1))
        val byTag = ShelfPages.build(lists, TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_TAG), now, zone)
        report.check("r73a/pages: tag pages, a task with two tags on both", byTag.map { it.title } == listOf("#launch-prep", "#Release", "No tag") && byTag[1].count == 2)
        report.check("r73a/pages: the count takes a task once", ShelfPages.count(byTag) == byTag.flatMap { it.tasks }.map { it.id }.toSet().size && ShelfPages.count(byTag) == 7)
        val one = ShelfPages.build(lists, TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_NONE), now, zone)
        report.check("r73a/pages: Group by None is one list of the Shelf", one.size == 1 && one[0].count == 4 && one[0].key == "all")
        report.check("r73a/pages: the header's count names the highlights", bins[1].countText == "2 · ★1" && bins[0].countText == "1" && bins[1].addText == "Add to Next")
        report.check("r73a/pages: a search narrows the pages", ShelfPages.build(lists, TaskViewConfig.EVERYTHING, now, zone, search = "task 3").map { it.count } == listOf(1, 0, 0))
        report.check("r73a/pages: Show n tasks", ShelfPages.showText(10) == "Show 10 tasks" && ShelfPages.showText(1) == "Show 1 task")
        val pinned = ShelfPages.build(lists.copy(tasks = lists.tasks + task(9, TaskBin.NEXT, 0.5)), TaskViewConfig.EVERYTHING.copy(pinHighlights = true), now, zone)
        report.check("r73a/pages: pinned highlights lead their page", pinned[1].tasks.first().id == id(1))
    }

    private fun strip(report: CheckReport) {
        report.check("r73a/strip: the board's line", TodayStripCounts.phoneText("3 ready", 2, 1) == "This session 3 ready · Queued 2 · Reminders 1")
        report.check("r73a/strip: in a session", TodayStripCounts.phoneText(TodayStripCounts.sessionPart(true, 1, 3, 2), 2, 1) == "This session 1/3 · Queued 2 · Reminders 1")
        report.check("r73a/strip: parts at 0 are left out, This session never", TodayStripCounts.phoneText(TodayStripCounts.sessionPart(false, 0, 0, 0), 0, 0) == "This session empty")
        report.check("r73a/strip: the PC's wider line", TodayStripCounts.pcText("3 ready", 2, 1, 1) == "This session 3 ready · Queued 2 · Reminders due today 1 · Finished 1")
        report.check("r73a/strip: no timer or time in the line", !TodayStripCounts.phoneText("3 ready", 2, 1).contains(Regex("\\d:\\d| left|\\d ?m(in)?\\b")))
        val counts = TodayStripCounts.of(world(), now, zone)
        report.check("r73a/strip: ready counts the session's open actions (focused steps each take one)", counts.ready == 3, "ready ${counts.ready}")
        report.check("r73a/strip: queued, finished", counts.queued == 1 && counts.finished == 1)
        report.check("r73a/strip: a reminder tied to a due date today counts", counts.reminders == 1, "reminders ${counts.reminders}")
    }

    private fun moves(report: CheckReport) {
        val lists = world()
        // This session holds 3 actions (task 6: 1, task 7: 2 focused steps): it is full.
        val drop = ShelfEdits.stripDrop(lists, listOf(id(1), id(5), id(6), id(8), id(1), "unknown"), PHONE, now)
        report.check("r73a/strip-drop: only Shelf tasks move, each once", drop.ids == listOf(id(1)))
        report.check("r73a/strip-drop: into Queued, at its end", drop.lists.of(1).task.let { it.bin == TaskBin.QUEUED && it.position == 2.0 })
        report.check("r73a/strip-drop: never into This session", drop.lists.tasks.count { it.task.bin == TaskBin.SESSION } == 2 && drop.queuedInstead == 0)
        report.check("r73a/strip-drop: Today's tasks stay where they are", drop.lists.of(5) === lists.of(5) && drop.lists.of(6) === lists.of(6) && drop.lists.of(8) === lists.of(8))
        val moved = drop.lists.of(1)
        report.check("r73a/strip-drop: the move is this phone's bin edit", moved.stamps.of(TaskField.BIN)?.by == PHONE && DotNetTime.compare(moved.stamps.of(TaskField.BIN)!!.at, created) > 0)
        report.check("r73a/strip-drop: no other field is stamped anew", TaskField.entries.filter { it != TaskField.BIN }.all { moved.stamps.of(it)?.by == PC })
        report.check("r73a/strip-drop: id, number, label, tags and reminders kept", moved.task.let { it.id == id(1) && it.number == TaskNumber(101) && it.labelId == ept.id && it.tags == listOf("Release") && it.reminders?.size == 1 })
        val undone = ShelfEdits.restore(drop.lists, drop.before, ShelfEdits.MOVE_FIELDS, PHONE, now.plusSeconds(3))
        report.check("r73a/strip-drop: Undo puts it back in its bin at its old place", undone.lists.of(1).task.let { it.bin == TaskBin.NEXT && it.position == 1.0 })
        report.check("r73a/strip-drop: Undo is a newer edit, so every device takes it", DotNetTime.compare(undone.lists.of(1).stamps.of(TaskField.BIN)!!.at, moved.stamps.of(TaskField.BIN)!!.at) > 0)
        report.check("r73a/strip-drop: nothing to move changes nothing", ShelfEdits.stripDrop(lists, listOf(id(5)), PHONE, now).let { !it.changed && it.lists === lists })

        val full = ShelfEdits.move(lists, listOf(id(3)), TaskBin.SESSION, PHONE, now)
        report.check("r73a/move: a full This session sends the task to Queued", full.lists.of(3).task.bin == TaskBin.QUEUED && full.queuedInstead == 1)
        report.check("r73a/move: This session's actions never pass the cap", full.lists.tasks.filter { it.task.bin == TaskBin.SESSION }.sumOf { it.task.actionCount } == 3)
        val room = lists.copy(tasks = lists.tasks.filter { it.id != id(7) })
        val into = ShelfEdits.move(room, listOf(id(2), id(3), id(4)), TaskBin.SESSION, PHONE, now)
        report.check("r73a/move: with room a task goes into This session and its first open steps take the places", into.lists.of(2).task.let { it.bin == TaskBin.SESSION && it.steps!!.map { step -> step.focus } == listOf(false, true, true) })
        report.check("r73a/move: the rest of a group goes to Queued once the session is full", into.lists.of(3).task.bin == TaskBin.QUEUED && into.lists.of(4).task.bin == TaskBin.QUEUED && into.queuedInstead == 2)
        val out = ShelfEdits.move(lists, listOf(id(7)), TaskBin.LATER, PHONE, now)
        report.check("r73a/move: out of This session the steps lose their focus", out.lists.of(7).task.let { it.bin == TaskBin.LATER && it.steps!!.none { step -> step.focus } })
        report.check("r73a/move: a task already there is left alone", !ShelfEdits.move(lists, listOf(id(3)), TaskBin.SORT, PHONE, now).changed)
        val bins = ShelfEdits.move(lists, listOf(id(1), id(2)), TaskBin.LATER, PHONE, now)
        report.check("r73a/move: tasks land at the end of the bin in the order given", bins.lists.of(1).task.position == 2.0 && bins.lists.of(2).task.position == 3.0)
        val normalized = TaskListRules.normalize(bins.lists, PHONE, PC, now, zone)
        report.check("r73a/move: the store's normalize keeps the move", normalized.of(1).task.bin == TaskBin.LATER && normalized.of(1).task.number == TaskNumber(101))
    }

    private fun drops(report: CheckReport) {
        val lists = world()
        val labelPages = ShelfPages.build(lists, TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_LABEL), now, zone)
        val onAi = ShelfEdits.dropOnPage(lists, listOf(id(1), id(3)), labelPages.first { it.title == "AI" }, PHONE, now)
        report.check("r73a/drop: a label page replaces the label", onAi.lists.of(1).task.labelId == ai.id && onAi.lists.of(3).task.labelId == ai.id && onAi.ids.size == 2)
        report.check("r73a/drop: a label drop keeps the bin and the tags", onAi.lists.of(1).task.let { it.bin == TaskBin.NEXT && it.tags == listOf("Release") })
        report.check("r73a/drop: a label drop is this phone's label edit only", onAi.lists.of(1).stamps.let { it.of(TaskField.LABEL)?.by == PHONE && it.of(TaskField.BIN)?.by == PC })
        val back = ShelfEdits.restore(onAi.lists, onAi.before, ShelfEdits.MOVE_FIELDS, PHONE, now.plusSeconds(2))
        report.check("r73a/drop: Undo restores the labels (also back to none)", back.lists.of(1).task.labelId == ept.id && back.lists.of(3).task.labelId == null)
        val none = ShelfEdits.dropOnPage(lists, listOf(id(1)), labelPages.last(), PHONE, now)
        report.check("r73a/drop: the No label page clears the label", none.lists.of(1).task.labelId == null)
        val tagPages = ShelfPages.build(lists, TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_TAG), now, zone)
        val tagged = ShelfEdits.dropOnPage(lists, listOf(id(1), id(4)), tagPages.first { it.title == "#launch-prep" }, PHONE, now)
        report.check("r73a/drop: a tag page adds the tag and keeps the others", tagged.lists.of(1).task.tags == listOf("Release", "launch-prep"))
        report.check("r73a/drop: a task that has the tag already is left alone", tagged.ids == listOf(id(1)) && tagged.lists.of(4) === lists.of(4))
        val untagged = ShelfEdits.restore(tagged.lists, tagged.before, ShelfEdits.MOVE_FIELDS, PHONE, now.plusSeconds(2))
        report.check("r73a/drop: Undo restores the tags", untagged.lists.of(1).task.tags == listOf("Release") && untagged.lists.of(1).task.labelId == ept.id)
        report.check("r73a/drop: the No tag page changes nothing", !ShelfEdits.dropOnPage(lists, listOf(id(1)), tagPages.last(), PHONE, now).changed)
        val binPages = ShelfPages.build(lists, TaskViewConfig.EVERYTHING, now, zone)
        val toLater = ShelfEdits.dropOnPage(lists, listOf(id(1), id(2)), binPages[2], PHONE, now)
        report.check("r73a/drop: a bin page moves the tasks (the wrap-around edge drops the same way)", toLater.lists.of(1).task.bin == TaskBin.LATER && toLater.lists.of(2).task.bin == TaskBin.LATER)
        report.check("r73a/drop: a bin drop keeps label and tags", toLater.lists.of(1).task.let { it.labelId == ept.id && it.tags == listOf("Release") })
    }

    private fun edits(report: CheckReport) {
        val lists = world()
        val done = ShelfEdits.finish(lists, id(1), PHONE, now)
        report.check("r73a/edit: a tick finishes the task and remembers its bin", done.lists.of(1).task.let { it.bin == TaskBin.DONE && it.prevBin == TaskBin.NEXT && it.doneAt != null })
        report.check("r73a/edit: Undo of a tick opens it again in place", ShelfEdits.restore(done.lists, done.before, listOf(TaskField.BIN), PHONE, now.plusSeconds(1)).lists.of(1).task.let { it.bin == TaskBin.NEXT && it.doneAt == null && it.position == 1.0 })
        val reopened = ShelfEdits.reopen(lists, id(8), PHONE, now)
        report.check("r73a/edit: unticking a finished task goes back to its bin", reopened.lists.of(8).task.let { it.bin == TaskBin.NEXT && it.doneAt == null && it.prevBin == null })
        val renamed = ShelfEdits.rename(lists, id(3), "  New name ", PHONE, now)
        report.check("r73a/edit: a rename is trimmed and stamped with its base", renamed.lists.of(3).let { it.task.title == "New name" && it.stamps.of(TaskField.TITLE)?.by == PHONE && it.stamps.of(TaskField.TITLE)?.bases?.size == 1 })
        report.check("r73a/edit: a blank rename changes nothing", !ShelfEdits.rename(lists, id(3), "   ", PHONE, now).changed)
        val starred = ShelfEdits.edit(lists, id(3), TaskField.HIGHLIGHT, PHONE, now) { it.copy(highlight = true) }
        report.check("r73a/edit: a highlight is its own stamped field", starred.lists.of(3).let { it.task.highlight && it.stamps.of(TaskField.HIGHLIGHT)?.by == PHONE && it.stamps.of(TaskField.TITLE)?.by == PC })
        report.check("r73a/edit: the same value changes nothing", !ShelfEdits.edit(lists, id(1), TaskField.HIGHLIGHT, PHONE, now) { it.copy(highlight = true) }.changed)
        val added = ShelfEdits.add(lists, id(50), " Buy stamps ", TaskBin.NEXT, ept.id, listOf("errand", "Errand"), PHONE, now)
        val fresh = added.lists.of(50)
        report.check("r73a/add: a new task lands at the end of the bin", fresh.task.let { it.title == "Buy stamps" && it.bin == TaskBin.NEXT && it.position == 3.0 && it.labelId == ept.id && it.tags == listOf("errand") })
        report.check("r73a/add: made on the phone, every field stamped by it", fresh.task.origin == TaskOrigins.FROM_PHONE && TaskField.entries.all { fresh.stamps.of(it)?.by == PHONE })
        report.check("r73a/add: the store numbers it", TaskListRules.normalize(added.lists, PHONE, PHONE, now, zone).of(50).task.number != null)
        report.check("r73a/add: into a full This session goes to Queued", ShelfEdits.add(lists, id(51), "x", TaskBin.SESSION, null, null, PHONE, now).let { it.lists.of(51).task.bin == TaskBin.QUEUED && it.queuedInstead == 1 })
        report.check("r73a/add: a blank title adds nothing", !ShelfEdits.add(lists, id(52), "  ", TaskBin.NEXT, null, null, PHONE, now).changed)
        val deleted = ShelfEdits.delete(lists, listOf(id(1)), PHONE, now)
        report.check("r73a/delete: the task goes to History as deleted, with everything it had", deleted.lists.of(1).task.let { !it.isLive && it.title.isEmpty() } && deleted.lists.log.single().entry.let { it.kind == TaskHistoryKind.DELETED && it.task.title == "Task 1" && it.task.reminders?.size == 1 })
        val back = ShelfEdits.undoDelete(deleted.lists, deleted.ids, PHONE, now.plusSeconds(2))
        report.check("r73a/delete: Undo brings it back under its id and number, in its bin", back.lists.of(1).task.let { it.isLive && it.number == TaskNumber(101) && it.bin == TaskBin.NEXT && it.title == "Task 1" })
    }

    private fun words(report: CheckReport) {
        val lists = world()
        val late = lists.of(1).task
        val parts = TaskRowText.detail(late, "ePT", now, zone, showBin = false)
        // 2026-11-12 17:00 UTC is 12:00 PM in New York: a task due today at 3:00 PM is not late yet.
        report.check("r73a/row: label, due date with a 12-hour time, reminder, tags last", parts.map { it.text } == listOf("ePT", "Thu 11/12 3:00 PM", "10 min before", "#Release"), parts.map { it.text }.toString())
        val overdue = TaskRowText.detail(late.copy(dueOn = LocalDate.of(2026, 11, 9), dueAt = null, reminders = null), "SDA", now, zone, showBin = false)
        report.check("r73a/row: Overdue 3 d is the late part", overdue.map { it.text } == listOf("SDA", "Overdue 3 d", "#Release") && overdue[1].late && !overdue[0].late)
        report.check("r73a/row: due earlier today reads Overdue", TaskRowText.detail(late.copy(dueAt = LocalTime.of(9, 0), reminders = null), null, now, zone, false).any { it.text == "Overdue" && it.late })
        val onLabelPage = TaskRowText.detail(lists.of(2).task, "AI", now, zone, showBin = true, showLabel = false)
        report.check("r73a/row: on a label page the bin leads and the label is left out", onLabelPage.map { it.text } == listOf("Next", "50 min", "1/3"))
        report.check("r73a/row: a Today task on a label page says where it is", TaskRowText.detail(lists.of(5).task, "ePT", now, zone, showBin = true, showLabel = false).first().text == "Today · Queued")
        val old = lists.of(3).task.copy(touchedAt = now.minusDays(29), origin = TaskOrigins.COME_BACK_TO)
        report.check("r73a/row: untouched weeks and where it came from", TaskRowText.detail(old, null, now, zone, false).map { it.text } == listOf("untouched 4 wk", "Come back to"))
        report.check("r73a/row: a task with nothing has no detail line", TaskRowText.detail(lists.of(3).task, null, now, zone, false).isEmpty())
        report.check("r73a/row: next year's date carries the year", TaskRowText.due(LocalDate.of(2027, 1, 31), null, LocalDate.of(2026, 11, 12)) == "Sun 1/31/2027")
        report.check("r73a/row: times are never 24-hour", TaskRowText.due(LocalDate.of(2026, 11, 13), LocalTime.of(20, 0), LocalDate.of(2026, 11, 12)) == "Fri 11/13 8:00 PM")
        report.check("r73a/row: sheet values", TaskRowText.remindersSet(late) == "1 set" && TaskRowText.remindersSet(old) == "None" && TaskRowText.id(late) == "101")
        report.check("r73a/row: devices", TaskRowText.devices(late, PHONE) == "All devices" && TaskRowText.devices(late.copy(devices = listOf(PHONE)), PHONE) == "This phone only" && TaskRowText.devices(late.copy(devices = listOf(PHONE, PC)), PHONE) == "2 devices")
        report.check("r73a/bins: where a task is", TaskBins.place(TaskBin.NEXT) == "Shelf · Next" && TaskBins.place(TaskBin.QUEUED) == "Today · Queued" && TaskBins.place(TaskBin.SESSION) == "Today · This session")
    }

    private fun remembered(report: CheckReport) {
        // What the page remembers travels in a view's own JSON shape (TasksUiPrefs): it must read back exactly, with an empty name.
        val config = TaskViewConfig(
            TaskViewFilter(due = listOf("overdue", "today"), time = listOf("le25"), old = listOf("3w"), highlightedOnly = true),
            labels = listOf(ept.id), noLabel = true, tags = listOf("Release"), match = TaskViewConfig.MATCH_ALL, group = TaskViewConfig.GROUP_TAG,
            sort = TaskViewRules.SORT_SHUFFLE, pinHighlights = true, density = TaskViewConfig.DENSITY_COMPACT, seed = 42,
        )
        val written = TaskJson.writeView(TaskView(TaskView.DEFAULT_ID, "", null, config, 0.0, OffsetDateTime.parse("2000-01-01T00:00:00Z")))
        val read = runCatching { TaskJson.readView(written).config }.getOrNull()
        report.check("r73a/remembered: the view in use reads back as written", read == config, "read $read")
        val plain = TaskJson.writeView(TaskView(TaskView.DEFAULT_ID, "", null, TaskViewConfig.EVERYTHING, 0.0, OffsetDateTime.parse("2000-01-01T00:00:00Z")))
        report.check("r73a/remembered: the default view too", runCatching { TaskJson.readView(plain).config }.getOrNull() == TaskViewConfig.EVERYTHING)
        report.check("r73a/remembered: no page order, start page or wrap in a view's settings (device-local)", listOf("order", "start", "wrap", "page").none { key -> written.toString().contains("\"$key") })
    }
}
