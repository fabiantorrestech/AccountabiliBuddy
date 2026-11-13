package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.tasks.ShelfDrag
import com.accountabilibuddy.shared.tasks.ShelfEdits
import com.accountabilibuddy.shared.tasks.ShelfHold
import com.accountabilibuddy.shared.tasks.ShelfHoldOutcome
import com.accountabilibuddy.shared.tasks.ShelfHoldState
import com.accountabilibuddy.shared.tasks.ShelfPages
import com.accountabilibuddy.shared.tasks.ShelfTitleTap
import com.accountabilibuddy.shared.tasks.SyncTaskItem
import com.accountabilibuddy.shared.tasks.SyncTaskLabel
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskFieldStamps
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskLabel
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskNumber
import com.accountabilibuddy.shared.tasks.TaskStep
import com.accountabilibuddy.shared.tasks.TaskViewConfig
import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * Owner device test, round 1 (phone Tasks): Undo of a tick made from a label page (the page, the place and the label, tags, steps and
 * note come back), the hold's arbitration ([ShelfHold]: hold, hold then move, quick swipe, tap) and a closed row's title being part
 * of the row.
 */
object PhoneBug1Check {
    private const val PHONE = "bbbbbbbb-0000-0000-0000-00000000000b"
    private val zone: ZoneId = ZoneId.of("America/New_York")
    private val created: OffsetDateTime = OffsetDateTime.parse("2026-11-10T13:00:00Z")
    private val now: OffsetDateTime = OffsetDateTime.parse("2026-11-12T17:00:00Z")
    private val ept = TaskLabel(TaskLabel.idFor("ePT"), "ePT", "#2F6BEA", 2.0, created)
    private val ai = TaskLabel(TaskLabel.idFor("AI"), "AI", "#7C4DFF", 1.0, created)
    private val config = TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_LABEL)

    private fun id(n: Int) = "9b9b9b9b-0000-0000-0000-%012d".format(n)

    private fun task(n: Int, bin: TaskBin, position: Double, change: (TaskItem) -> TaskItem = { it }) =
        SyncTaskItem(change(TaskItem(id(n), "Task $n", bin, position, created, created, created, number = TaskNumber(100 + n))), TaskFieldStamps.allAt(FieldStamp(created, PHONE)))

    fun run(report: CheckReport) {
        val lists = TaskLists(
            listOf(
                task(1, TaskBin.NEXT, 1.0) { it.copy(labelId = ept.id, tags = listOf("Release"), note = "keep me", steps = listOf(TaskStep(id(81), "a"), TaskStep(id(82), "b"))) },
                task(2, TaskBin.NEXT, 2.0) { it.copy(labelId = ept.id) },
                task(3, TaskBin.NEXT, 3.0) { it.copy(labelId = ept.id) },
                task(4, TaskBin.LATER, 1.0) { it.copy(labelId = ai.id) },
            ),
            listOf(SyncTaskLabel(ept), SyncTaskLabel(ai)), emptyList(), emptyList(),
        )
        val before = ShelfPages.build(lists, config, now, zone)
        val eptPage = before.first { it.labelId.equals(ept.id, true) }
        val at = before.indexOf(eptPage)
        val place = eptPage.tasks.indexOfFirst { it.id == id(2) }

        // Tick task 2 on the ePT page, then Undo with the fields the page's tick uses.
        val ticked = ShelfEdits.finish(lists, id(2), PHONE, now)
        val mid = ShelfPages.build(ticked.lists, config, now, zone)
        report.check("phonebug1/undo: a tick takes the task off its label page", mid.first { it.key == eptPage.key }.tasks.none { it.id == id(2) })
        val undone = ShelfEdits.restore(ticked.lists, ticked.before, ShelfEdits.MOVE_FIELDS, PHONE, now.plusSeconds(3))
        val after = ShelfPages.build(undone.lists, config, now, zone)
        val back = after.first { it.key == eptPage.key }
        val item = undone.lists.tasks.first { it.id == id(2) }.task
        report.check("phonebug1/undo: back on the same label page, bin and place", after.indexOf(back) == at && back.tasks.indexOfFirst { it.id == id(2) } == place && item.bin == TaskBin.NEXT && item.position == 2.0)
        report.check("phonebug1/undo: the label comes back", item.labelId == ept.id)

        // The task with labels, tags, steps and a note: the tick leaves them alone, Undo keeps them.
        val rich = ShelfEdits.finish(lists, id(1), PHONE, now)
        val richBack = ShelfEdits.restore(rich.lists, rich.before, ShelfEdits.MOVE_FIELDS, PHONE, now.plusSeconds(3)).lists.tasks.first { it.id == id(1) }.task
        report.check("phonebug1/undo: labels, tags, steps and note intact", richBack.labelId == ept.id && richBack.tags == listOf("Release") && richBack.note == "keep me" && richBack.steps?.size == 2 && richBack.bin == TaskBin.NEXT && richBack.doneAt == null)

        // The bug: a label with one task loses its page when that task is ticked; the page showing and the pages beside it shifted.
        val solo = ShelfPages.build(ShelfEdits.finish(lists, id(4), PHONE, now).lists, config, now, zone)
        val aiPage = before.first { it.labelId.equals(ai.id, true) }
        report.check("phonebug1/undo: (the cause) a label's only task ticked drops its page", solo.none { it.key == aiPage.key })
        val kept = ShelfPages.keepHeld(solo, listOf(aiPage to before.indexOf(aiPage)))
        report.check("phonebug1/undo: the page is held empty at its old place while the toast shows", kept.map { it.key } == before.map { it.key } && kept[before.indexOf(aiPage)].tasks.isEmpty())
        report.check("phonebug1/undo: a page still there is not doubled", ShelfPages.keepHeld(before, listOf(aiPage to 0)).map { it.key } == before.map { it.key })

        // The hold.
        val slop = 8f
        fun gesture(vararg steps: Pair<Float, Long>, up: Long?): ShelfHold = ShelfHold(slop).also { hold ->
            for ((distance, ms) in steps) hold.onMove(distance, ms)
            if (up != null) hold.onUp(up) else hold.onTimeout()
        }
        val still = gesture(2f to 100L, 3f to 300L, up = null)
        report.check("phonebug1/hold: held still 450 ms lifts the row", still.state == ShelfHoldState.LIFTED)
        still.onUp(600)
        report.check("phonebug1/hold: let go without moving selects", still.state == ShelfHoldState.DONE && still.outcome == ShelfHoldOutcome.SELECT)
        val dragged = gesture(up = null).also { it.onMove(40f, 700) }
        report.check("phonebug1/hold: lifted then moved is a drag, and a release drops", dragged.state == ShelfHoldState.DRAGGING && dragged.also { it.onUp(900) }.outcome == ShelfHoldOutcome.DROP)
        val swipe = gesture(30f to 120L, up = 200L)
        report.check("phonebug1/hold: a quick sideways move is the row's swipe, not a lift", swipe.state == ShelfHoldState.CALLED_OFF && swipe.outcome == ShelfHoldOutcome.MOVED_ON)
        val tap = gesture(up = 90L)
        report.check("phonebug1/hold: a tap opens the task", tap.state == ShelfHoldState.CALLED_OFF && tap.outcome == ShelfHoldOutcome.TAP)
        report.check("phonebug1/hold: the time is the lift time", ShelfDrag.LIFT_MS == 450L)

        // The title.
        report.check("phonebug1/title: a tap on a closed row's title is the row's tap", ShelfTitleTap.of(expanded = false, selecting = false) == ShelfTitleTap.Action.ROW)
        report.check("phonebug1/title: in select mode it still is", ShelfTitleTap.of(expanded = true, selecting = true) == ShelfTitleTap.Action.ROW)
        report.check("phonebug1/title: an open task's title renames", ShelfTitleTap.of(expanded = true, selecting = false) == ShelfTitleTap.Action.RENAME)
    }
}
