package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalCounting
import com.accountabilibuddy.shared.goals.GoalDerivedMinutes
import com.accountabilibuddy.shared.goals.GoalDirection
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalFocusSession
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalMenuItemId
import com.accountabilibuddy.shared.goals.GoalMenuPlan
import com.accountabilibuddy.shared.goals.GoalPeriod
import com.accountabilibuddy.shared.goals.GoalPhoneRules
import com.accountabilibuddy.shared.goals.GoalSessionTask
import com.accountabilibuddy.shared.goals.GoalSourceAvailability
import com.accountabilibuddy.shared.goals.GoalSourceData
import com.accountabilibuddy.shared.goals.GoalSourceRule
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.goals.GoalSources
import com.accountabilibuddy.shared.goals.GoalTarget
import com.accountabilibuddy.shared.goals.GoalTaskLink
import com.accountabilibuddy.shared.goals.GoalTaskLinks
import com.accountabilibuddy.shared.goals.GoalTasksCard
import com.accountabilibuddy.shared.goals.GoalUnit
import com.accountabilibuddy.shared.goals.GoalWindow
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.tasks.ShelfEdits
import com.accountabilibuddy.shared.tasks.ShelfToasts
import com.accountabilibuddy.shared.tasks.SyncTaskItem
import com.accountabilibuddy.shared.tasks.SyncTaskLogItem
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskConflict
import com.accountabilibuddy.shared.tasks.TaskConflictActions
import com.accountabilibuddy.shared.tasks.TaskEdits
import com.accountabilibuddy.shared.tasks.TaskField
import com.accountabilibuddy.shared.tasks.TaskFieldMerge
import com.accountabilibuddy.shared.tasks.TaskFieldStamps
import com.accountabilibuddy.shared.tasks.TaskFieldValues
import com.accountabilibuddy.shared.tasks.TaskHistoryEntry
import com.accountabilibuddy.shared.tasks.TaskHistoryKind
import com.accountabilibuddy.shared.tasks.TaskHistoryRules
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskJson
import com.accountabilibuddy.shared.tasks.TaskListRules
import com.accountabilibuddy.shared.tasks.TaskLists
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID

/**
 * GS5b (phone; the PC's twins are the gs5a fixture checks, which run beside these): the task goal link as a real field of the phone's task,
 * its stamping (only when the phone sets or clears it), every writer path keeping it (edits, copies, undo, restore, History, the merge with an
 * older device), the goal sources counting on the phone, and the Q1126 gap closed (a tag + link goal: the phone shows what the PC shows).
 */
object Gs5bSelfCheck {
    private const val PC = "aaaaaaaa-0000-0000-0000-00000000000a"
    private const val PHONE = "bbbbbbbb-0000-0000-0000-00000000000b"
    private val offset: ZoneOffset = ZoneOffset.ofHours(-5)
    private val created: OffsetDateTime = OffsetDateTime.of(2026, 11, 1, 12, 0, 0, 0, offset)
    private val now: OffsetDateTime = OffsetDateTime.of(2026, 11, 11, 18, 0, 0, 0, offset)
    private val today: LocalDate = LocalDate.of(2026, 11, 11)
    private val goalA = "00000000-0000-0000-0000-0000000000a1"
    private val goalB = "00000000-0000-0000-0000-0000000000b2"

    private fun id(n: Int) = "55555555-0000-0000-0000-%012d".format(n)

    private fun task(n: Int, goal: String? = null, bin: TaskBin = TaskBin.QUEUED, change: (TaskItem) -> TaskItem = { it }): SyncTaskItem {
        val item = change(TaskItem(id(n), "Task $n", bin, n.toDouble(), created, created, created, goalId = goal))
        return SyncTaskItem(item, TaskFieldStamps.forNew(item, FieldStamp(created, PC)))
    }

    private fun lists(vararg tasks: SyncTaskItem, log: List<SyncTaskLogItem> = emptyList()) = TaskLists(tasks.toList(), emptyList(), emptyList(), log)

    private fun TaskLists.of(n: Int) = tasks.first { it.id == id(n) }

    fun run(report: CheckReport) {
        field(report)
        writers(report)
        merging(report)
        counting(report)
        sourcesAndCard(report)
        wiring(report)
    }

    // ---------------------------------------------------------------- the field and its stamp

    private fun field(report: CheckReport) {
        val name = "gs5b/field"
        val linked = task(1, goalA)
        report.check("$name: the goal id is the field's canonical text, empty for none", TaskFieldValues.canonical(linked.task, TaskField.GOAL) == goalA && TaskFieldValues.canonical(task(2).task, TaskField.GOAL) == "")
        report.check("$name: allAt stamps every field but the goal; forNew stamps the goal only when the task carries one",
            TaskField.entries.filter { it != TaskField.GOAL }.all { TaskFieldStamps.allAt(FieldStamp(now, PHONE)).of(it) != null } &&
                TaskFieldStamps.allAt(FieldStamp(now, PHONE)).of(TaskField.GOAL) == null &&
                TaskFieldStamps.forNew(linked.task, FieldStamp(now, PHONE)).of(TaskField.GOAL)?.by == PHONE && TaskFieldStamps.forNew(task(2).task, FieldStamp(now, PHONE)).of(TaskField.GOAL) == null)
        report.check("$name: a task with no link writes no goalId and no goal stamp (the text older readers see is unchanged)",
            !TaskJson.text(TaskJson.writeSyncTask(task(2))).contains("goal"))
        val text = TaskJson.writeSyncTask(linked)
        report.check("$name: goalId is written after autoSavedFor and before unknown fields, the goal stamp after plan, and both read back",
            TaskJson.readSyncTask(text) == linked && TaskJson.text(text).contains("\"goalId\":\"$goalA\"") && TaskJson.text(text).contains("\"goal\":{"))
        val upper = TaskJson.readTask(kotlinx.serialization.json.buildJsonObject {
            kotlinx.serialization.json.JsonObject(TaskJson.writeTask(linked.task)).forEach { (key, value) -> put(key, if (key == "goalId") kotlinx.serialization.json.JsonPrimitive(goalA.uppercase()) else value) }
        })
        report.check("$name: a goal id written in capitals reads as lower case, as every id", upper.goalId == goalA)
        report.check("$name: the tie-break text (the PC's JSON, known fields) now holds the goal id and a task with another link breaks differently",
            linked.tieBreak.contains(goalA) && linked.tieBreak != task(1, goalB).tieBreak)

        // The phone sets and clears it: only the goal stamp is made, by this phone, newer than before; every other stamp stays.
        val before = lists(task(1))
        val set = ShelfEdits.setGoal(before, listOf(id(1)), goalA.uppercase(), PHONE, now)
        val item = set.lists.of(1)
        report.check("$name: linking stamps only the goal part, by this phone, and moves the task's edit time",
            set.changed && item.task.goalId == goalA && item.stamps.of(TaskField.GOAL)?.by == PHONE && item.stamps.of(TaskField.GOAL)!!.at >= now &&
                TaskField.entries.filter { it != TaskField.GOAL }.all { item.stamps.of(it) == before.of(1).stamps.of(it) } && item.task.editedAt >= now)
        report.check("$name: linking again to the same goal changes nothing", !ShelfEdits.setGoal(set.lists, listOf(id(1)), goalA, PHONE, now.plusMinutes(1)).changed)
        val cleared = ShelfEdits.setGoal(set.lists, listOf(id(1), id(99)), null, PHONE, now.plusMinutes(2))
        report.check("$name: Unlink is a change: the id goes, the goal stamp moves on (a clear is stamped), an unknown id is skipped",
            cleared.ids == listOf(id(1)) && cleared.lists.of(1).task.goalId == null && cleared.lists.of(1).stamps.of(TaskField.GOAL)!!.at > item.stamps.of(TaskField.GOAL)!!.at)
        val undone = ShelfEdits.restore(cleared.lists, cleared.before, listOf(TaskField.GOAL), PHONE, now.plusMinutes(3))
        report.check("$name: Undo of an Unlink puts the link back as a new stamped edit; Undo of a link takes it off",
            undone.lists.of(1).task.goalId == goalA && undone.lists.of(1).stamps.of(TaskField.GOAL)!!.at > cleared.lists.of(1).stamps.of(TaskField.GOAL)!!.at &&
                ShelfEdits.restore(set.lists, set.before, listOf(TaskField.GOAL), PHONE, now.plusMinutes(3)).lists.of(1).task.goalId == null)
        val several = ShelfEdits.setGoal(lists(task(1), task(2, goalB), task(3, goalA)), listOf(id(1), id(2), id(3)), goalA, PHONE, now)
        report.check("$name: several tasks: each is one edit, one already on the goal is left alone", several.ids == listOf(id(1), id(2)) && several.before.size == 2)
        report.check("$name: the toast words are the PC's (Linked to X, Link removed, Linked to a goal)",
            ShelfToasts.linked("Spanish") == "Linked to Spanish" && ShelfToasts.linked(null) == "Link removed" && ShelfToasts.linked("") == "Linked to a goal")

        // Every other phone edit keeps the link and its stamp byte for byte.
        val l = lists(task(1, goalA, TaskBin.NEXT) { it.copy(steps = listOf(com.accountabilibuddy.shared.tasks.TaskStep(id(80), "a"))) })
        val stampText = { r: TaskLists -> TaskJson.text(TaskJson.writeStamp(r.of(1).stamps.of(TaskField.GOAL)!!)) }
        val first = stampText(l)
        val edits = listOf(
            "rename" to ShelfEdits.rename(l, id(1), "Renamed", PHONE, now),
            "label" to ShelfEdits.edit(l, id(1), TaskField.LABEL, PHONE, now) { it.copy(labelId = "00000000-0000-0000-0000-0000000000c3") },
            "tags" to ShelfEdits.edit(l, id(1), TaskField.TAGS, PHONE, now) { it.copy(tags = listOf("x")) },
            "highlight" to ShelfEdits.edit(l, id(1), TaskField.HIGHLIGHT, PHONE, now) { it.copy(highlight = true) },
            "estimate" to ShelfEdits.edit(l, id(1), TaskField.ESTIMATE, PHONE, now) { it.copy(estimateMinutes = 25) },
            "move" to ShelfEdits.move(l, listOf(id(1)), TaskBin.SESSION, PHONE, now),
            "finish" to ShelfEdits.finish(l, id(1), PHONE, now),
        )
        for ((what, change) in edits) {
            report.check("$name: $what keeps the link and its goal stamp", change.changed && change.lists.of(1).task.goalId == goalA && stampText(change.lists) == first)
        }
        val finished = ShelfEdits.finish(l, id(1), PHONE, now).lists
        report.check("$name: reopen keeps the link and its stamp", ShelfEdits.reopen(finished, id(1), PHONE, now.plusMinutes(1)).lists.of(1).let { it.task.goalId == goalA && stampText(lists(it)) == first })
    }

    // ---------------------------------------------------------------- every writer path

    private fun writers(report: CheckReport) {
        val name = "gs5b/writers"
        val done = task(1, goalA, TaskBin.DONE) { it.copy(prevBin = TaskBin.NEXT, doneAt = now.minusHours(1), steps = listOf(com.accountabilibuddy.shared.tasks.TaskStep(id(80), "s", doneAt = now))) }
        val entry = SyncTaskLogItem(TaskHistoryEntry(done.id, TaskHistoryKind.FINISHED, now.minusHours(1), done.task, now.minusHours(1)))
        var n = 100
        val again = TaskHistoryRules.doAgain(lists(log = listOf(entry)), entry.id, now, PHONE) { id(n++) }
        val copy = again.lists.tasks.first { it.id == again.ids.single() }
        report.check("$name: Do again copies the goal with the task, stamped as set now (a copy is a new link)",
            copy.task.goalId == goalA && copy.stamps.of(TaskField.GOAL)?.let { it.by == PHONE && it.at == now } == true)
        val plain = TaskHistoryEntry(id(2), TaskHistoryKind.FINISHED, now, task(2).task.copy(doneAt = now), now)
        val againPlain = TaskHistoryRules.doAgain(lists(log = listOf(SyncTaskLogItem(plain))), plain.id, now, PHONE) { id(n++) }
        report.check("$name: Do again of a task with no link makes one with no goal stamp", againPlain.lists.tasks.single().let { it.task.goalId == null && it.stamps.of(TaskField.GOAL) == null })

        val conflictTask = task(3, goalB) { it.copy(conflicts = listOf(TaskConflict(TaskConflict.TITLE, "Other title", PC, created))) }
        val kept = TaskConflictActions.keepBothVersions(lists(conflictTask), id(3), conflictTask.task.conflicts!!.single(), id(120), PHONE, now) { id(130 + it) }
        val other = kept.lists.tasks.first { it.id == id(120) }
        report.check("$name: Keep both: the other copy carries the goal, stamped now; the original keeps its own",
            kept.ids.contains(id(120)) && other.task.goalId == goalB && other.stamps.of(TaskField.GOAL)?.by == PHONE && kept.lists.of(3).task.goalId == goalB)

        // Deleted and brought back (Undo of a delete, Bring back from History): whole, with the link, stamped as set then.
        val live = lists(task(4, goalA))
        val deleted = ShelfEdits.delete(live, listOf(id(4)), PHONE, now)
        report.check("$name: Delete keeps the link in the History snapshot", deleted.lists.log.single().entry.task.goalId == goalA && deleted.before.single().goalId == goalA)
        val back = ShelfEdits.undoDelete(deleted.lists, listOf(id(4)), PHONE, now.plusMinutes(1))
        report.check("$name: Undo of a delete / Bring back puts the task back with its link and a goal stamp",
            back.lists.of(4).task.let { it.deletedAt == null && it.goalId == goalA } && back.lists.of(4).stamps.of(TaskField.GOAL)?.by == PHONE)
        val noLink = ShelfEdits.undoDelete(ShelfEdits.delete(lists(task(5)), listOf(id(5)), PHONE, now).lists, listOf(id(5)), PHONE, now.plusMinutes(1))
        report.check("$name: a task with no link comes back with no goal stamp", noLink.lists.of(5).let { it.task.goalId == null && it.stamps.of(TaskField.GOAL) == null })

        // A restore (a backup): a restored link is stamped now; a backup task with no link never clears a link made since.
        val current = lists(task(6, goalA))
        val backup = lists(task(6, goalB), task(7), task(8, goalA))
        val restored = TaskListRules.restoredIncoming(current, backup, PC, PHONE, now, null)
        report.check("$name: Restore stamps the backup's links now and leaves a task with no link unstamped",
            restored.of(6).task.goalId == goalB && restored.of(6).stamps.of(TaskField.GOAL)?.at == now && restored.of(7).stamps.of(TaskField.GOAL) == null && restored.of(8).task.goalId == goalA)
        val linkedSince = task(7, goalA).let { it.copy(stamps = it.stamps.with(TaskField.GOAL, FieldStamp(now.plusHours(1), PC))) }
        val merged = TaskFieldMerge.merge(listOf(linkedSince), listOf(restored.of(7)), now.plusHours(2), PHONE).single()
        report.check("$name: so a restore's task with no link never takes away a link made since", merged.task.goalId == goalA)

        // The older devices' lists (the old fields the phone still reads and writes): an edit there keeps the link and its stamp, a tick and an untick
        // bring the task back from History with its link, stamped as set then.
        val zone = java.time.ZoneId.of("America/New_York")
        val world = lists(task(11, goalA, TaskBin.LATER), task(12))
        val none = com.accountabilibuddy.shared.tasks.OldTaskFields(emptyList(), emptyList(), emptyList(), emptyList(), emptyList(), emptyList())
        val laterItem = com.accountabilibuddy.shared.tasks.TaskFacadeLists.later(world, now).first { it.id == id(11) }
        val goalStamp = world.of(11).stamps.of(TaskField.GOAL)
        val renamed = com.accountabilibuddy.shared.tasks.TaskLocalEdits.apply(world, none.copy(later = listOf(laterItem.copy(title = "Renamed on an older list"))), PHONE, now, zone).of(11)
        report.check("$name: an edit through an older device's list keeps the link and its goal stamp", renamed.task.title == "Renamed on an older list" && renamed.task.goalId == goalA && renamed.stamps.of(TaskField.GOAL) == goalStamp)
        val ticked = TaskListRules.normalize(com.accountabilibuddy.shared.tasks.TaskLocalEdits.apply(world, none.copy(later = listOf(laterItem.copy(doneAt = now.toInstant()))), PHONE, now, zone), PHONE, PC, now, zone)
        val shownTicked = com.accountabilibuddy.shared.tasks.TaskFacadeLists.later(ticked, now).first { it.id == id(11) }
        val unticked = TaskListRules.normalize(com.accountabilibuddy.shared.tasks.TaskLocalEdits.apply(ticked, none.copy(later = listOf(shownTicked.copy(doneAt = null))), PHONE, now.plusMinutes(1), zone), PHONE, PC, now.plusMinutes(1), zone)
        report.check("$name: tick then untick on an older list: the task comes back from History with its link and a goal stamp by this phone",
            ticked.log.any { it.id == id(11) && it.entry.task.goalId == goalA } && unticked.of(11).task.let { it.isLive && it.goalId == goalA } && unticked.of(11).stamps.of(TaskField.GOAL)?.by == PHONE)

        val parent = task(13, goalA, TaskBin.NEXT) { it.copy(steps = listOf(com.accountabilibuddy.shared.tasks.TaskStep(id(85), "a step"))) }
        val own = com.accountabilibuddy.shared.tasks.ShelfSteps.ownTask(lists(parent), id(13), id(85), id(140), PHONE, now)
        val ownTask = own.lists.of(140)
        report.check("$name: Q1129: a step made its own task keeps the parent's goal link, stamped as set now by this phone; the parent keeps its own",
            own.changed && ownTask.task.goalId == goalA && ownTask.stamps.of(TaskField.GOAL)?.let { it.by == PHONE && it.at == now } == true && own.lists.of(13).task.goalId == goalA)
        val ownPlain = com.accountabilibuddy.shared.tasks.ShelfSteps.ownTask(lists(task(14, null, TaskBin.NEXT) { it.copy(steps = listOf(com.accountabilibuddy.shared.tasks.TaskStep(id(86), "s"))) }), id(14), id(86), id(141), PHONE, now)
        report.check("$name: Q1129: a parent with no link makes a task with no goal stamp", ownPlain.lists.of(141).stamps.of(TaskField.GOAL) == null)

        // The text of a task that never had a link is unchanged by every writer (older readers see the same).
        val untouched = ShelfEdits.rename(lists(task(9)), id(9), "New name", PHONE, now).lists.of(9)
        report.check("$name: a rename of a task with no link writes no goal stamp", untouched.stamps.of(TaskField.GOAL) == null && !TaskJson.text(TaskJson.writeSyncTask(untouched)).contains("goal"))
        val add = ShelfEdits.add(TaskLists.EMPTY, id(10), "New", TaskBin.QUEUED, null, null, PHONE, now)
        report.check("$name: a task made on the phone has no goal stamp until a link is set", add.lists.tasks.single().stamps.of(TaskField.GOAL) == null)
    }

    // ---------------------------------------------------------------- the merge with the PC and an older device

    private fun merging(report: CheckReport) {
        val name = "gs5b/merge"
        val pc = task(1, goalA).let { it.copy(stamps = it.stamps.with(TaskField.GOAL, FieldStamp(now, PC))) }
        val older = task(1).let { SyncTaskItem(it.task.copy(editedAt = now.plusMinutes(5), touchedAt = now.plusMinutes(5), title = "Edited on an older device"), it.stamps.with(TaskField.TITLE, FieldStamp(now.plusMinutes(5), "cccccccc-0000-0000-0000-00000000000c"))) }
        val both = TaskFieldMerge.merge(listOf(pc), listOf(older), now.plusMinutes(10), PHONE).single()
        report.check("$name: an older device's copy (no link, no goal stamp) keeps the PC's link; its newer title wins", both.task.goalId == goalA && both.task.title == "Edited on an older device")
        report.check("$name: in the other order too", TaskFieldMerge.merge(listOf(older), listOf(pc), now.plusMinutes(10), PHONE).single() == both)
        val clear = ShelfEdits.setGoal(lists(pc), listOf(id(1)), null, PHONE, now.plusMinutes(1)).lists.of(1)
        report.check("$name: a clear on the phone newer than the PC's link wins, in both orders and again",
            TaskFieldMerge.merge(listOf(pc), listOf(clear), now.plusMinutes(2), PHONE).single().task.goalId == null &&
                TaskFieldMerge.merge(listOf(clear), listOf(pc), now.plusMinutes(2), PHONE).single().task.goalId == null)
        val again = TaskFieldMerge.merge(listOf(TaskFieldMerge.merge(listOf(pc), listOf(clear), now.plusMinutes(2), PHONE).single()), listOf(clear), now.plusMinutes(3), PHONE).single()
        report.check("$name: merging the result with an input again changes nothing", again.task.goalId == null && again.stamps.of(TaskField.GOAL) == clear.stamps.of(TaskField.GOAL))
        val neither = TaskFieldMerge.merge(listOf(task(2)), listOf(task(2)), now, PHONE).single()
        report.check("$name: neither copy ever stamped it and both are the same: the merged task has no goal stamp", neither.stamps.of(TaskField.GOAL) == null)
        val differ = TaskFieldMerge.merge(listOf(task(3, goalA).let { it.copy(stamps = it.stamps.with(TaskField.GOAL, null)) }), listOf(task(3, goalB).let { it.copy(stamps = it.stamps.with(TaskField.GOAL, null)) }), now, PHONE).single()
        report.check("$name: unstamped but different values keep the picked stamp (or merging again could pick the other value)", differ.stamps.of(TaskField.GOAL) != null)
        // A PC task with a link comes through the phone's list store text and back unchanged (the PC -> phone -> PC round trip).
        val text = TaskJson.writeTasks(listOf(pc, task(2)))
        val read = TaskJson.readTasks(text)!!
        report.check("$name: PC -> phone -> PC: a list with a link reads and writes back to the same text", TaskJson.text(TaskJson.writeTasks(read.items)) == TaskJson.text(text) && read.items.first().task.goalId == goalA)
        val renamed = TaskEdits.edit(read.items.first(), TaskField.TITLE, read.items.first().task.copy(title = "Phone edit"), now.plusMinutes(1), PHONE)
        val out = TaskJson.readSyncTask(TaskJson.writeSyncTask(renamed))
        report.check("$name: a phone edit of another field sends the link and its PC stamp on unchanged",
            out.task.goalId == goalA && out.stamps.of(TaskField.GOAL) == pc.stamps.of(TaskField.GOAL) && out.task.title == "Phone edit")
    }

    // ---------------------------------------------------------------- counting on the phone

    private fun gid(n: Int) = UUID.fromString("00000000-0000-0000-0000-%012d".format(n))

    private fun goal(n: Int, kind: GoalKind, sources: List<GoalSourceRule>, target: GoalTarget = GoalTarget(GoalPeriod.Day, 15.0, GoalUnit.Minutes, GoalDirection.AtLeast)) =
        Goal(gid(n), gid(1), "Goal $n", "school", "#2E7D6B", kind, target, sources, now.minusDays(30), n.toDouble(), now.minusDays(30))

    private fun session(from: Int, to: Int, vararg tasks: GoalSessionTask) =
        GoalFocusSession(listOf(GoalWindow(LocalDateTime.of(today, java.time.LocalTime.of(10, from)), LocalDateTime.of(today, java.time.LocalTime.of(10, to)))), tasks.toList(), false)

    private fun counting(report: CheckReport) {
        val name = "gs5b/counting"
        val tagged = GoalSessionTask(null, null, listOf("Spanish"), gid(500))
        val linkOnly = GoalSessionTask(null, null, null, gid(501))
        val tagOnly = GoalSessionTask(null, null, listOf("Spanish"), gid(502))
        val tagAndLink = goal(10, GoalKind.Time, listOf(GoalSourceRule(gid(901), GoalSourceType.FocusTag, tag = "Spanish"), GoalSourceRule(gid(902), GoalSourceType.GoalField)))
        val links = listOf(GoalTaskLink(gid(500), tagAndLink.id, "t", null), GoalTaskLink(gid(501), tagAndLink.id, "l", null))
        val ownSessions = listOf(session(0, 30, tagged), session(40, 50, tagOnly))
        // Q1126, the S1 gap closed: what the PC wrote for the phone (its #tag extra: the minutes the link does not already count) plus what the
        // phone counts itself from the links must be what the PC shows.
        val extra = GoalDerivedMinutes.focusLocalExtra(tagAndLink, ownSessions, today, setOf(gid(500), gid(501)))
        val pcData = GoalSourceData(ownSessions, emptyList(), offset, null, true, taskLinks = links)
        val pcShows = GoalCounting.day(tagAndLink, pcData, today, today).minutes
        val entry = GoalEntry(gid(700), tagAndLink.id, today, null, extra, GoalEntrySource.FocusLocal, now, deviceId = gid(800))
        val phoneData = GoalSourceData.forPhone(ownSessions, listOf(entry), offset, links)
        val phoneShows = GoalCounting.day(tagAndLink, phoneData, today, today).minutes
        report.check("$name: Q1126: a tag + link goal: the PC's #tag extra leaves out the minutes the link counts (30 linked, 10 tag only: extra 10)", extra == 10.0, extra.toString())
        report.check("$name: Q1126: the PC shows 40 and the phone, from the PC's extra plus its own link count, shows the same 40 (it showed 10 before GS5b)", pcShows == 40.0 && phoneShows == 40.0, "$pcShows $phoneShows")
        val noLinks = GoalCounting.day(tagAndLink, GoalSourceData.forPhone(ownSessions, listOf(entry), offset), today, today).minutes
        report.check("$name: without the links the phone would show only the extra (the gap the PC check pinned)", noLinks == 10.0, noLinks.toString())

        val linkGoal = goal(11, GoalKind.Time, listOf(GoalSourceRule(gid(903), GoalSourceType.GoalField)))
        val onlyLinks = listOf(GoalTaskLink(gid(501), linkGoal.id, "l", null), GoalTaskLink(gid(502), gid(999), "other goal", null))
        val data = GoalSourceData.forPhone(listOf(session(0, 20, linkOnly), session(30, 45, tagOnly)), emptyList(), offset, onlyLinks)
        val state = GoalCounting.day(linkGoal, data, today, today)
        report.check("$name: the focus of a session with a linked task counts, one with a task linked to another goal does not", state.minutes == 20.0 && state.sources == listOf(GoalSourceType.GoalField))
        val unlinked = GoalCounting.day(linkGoal, GoalSourceData.forPhone(data.focus, emptyList(), offset, emptyList()), today, today)
        report.check("$name: unlinking takes the old sessions out (the link as it is now)", unlinked.minutes == 0.0)
        val deepWork = GoalCounting.day(com.accountabilibuddy.shared.goals.GoalSeeds.deepWork(now.minusDays(30)), GoalSourceData.forPhone(data.focus, emptyList(), offset, onlyLinks), today, today)
        report.check("$name: Deep work counts all focus whatever links say", deepWork.minutes == 35.0, deepWork.minutes.toString())

        val milestone = goal(12, GoalKind.Milestone, listOf(GoalSourceRule(gid(904), GoalSourceType.MilestoneTasks)), GoalTarget(GoalPeriod.Day, null, GoalUnit.Count, GoalDirection.AtLeast, 4.0))
        val doneAt = { h: Int, d: Int -> OffsetDateTime.of(2026, 11, d, h, 0, 0, 0, offset) }
        val mLinks = listOf(
            GoalTaskLink(gid(600), milestone.id, "a", doneAt(9, 10)), GoalTaskLink(gid(601), milestone.id, "b", doneAt(23, 10)), GoalTaskLink(gid(602), milestone.id, "c", doneAt(1, 11)),
            GoalTaskLink(gid(603), milestone.id, "open", null), GoalTaskLink(gid(604), gid(999), "other", doneAt(9, 11)),
        )
        val mData = GoalSourceData.forPhone(emptyList(), emptyList(), offset, mLinks)
        report.check("$name: a milestone counts each finished linked task once, on its local day; open ones and other goals' tasks never",
            GoalCounting.day(milestone, mData, LocalDate.of(2026, 11, 10), today).value == 2.0 && GoalCounting.day(milestone, mData, today, today).value == 1.0)
        val hand = GoalEntry(gid(710), milestone.id, today, null, 1.0, GoalEntrySource.Hand, now)
        report.check("$name: a hand entry adds to the finished tasks, and the Target tile reads the total",
            GoalCounting.day(milestone, GoalSourceData.forPhone(emptyList(), listOf(hand), offset, mLinks), today, today).value == 2.0 &&
                com.accountabilibuddy.shared.goals.GoalStatus.target(milestone, mData, today)?.current == 3.0)
        val noSource = goal(13, GoalKind.Milestone, listOf(GoalSourceRule(gid(905), GoalSourceType.Hand)))
        report.check("$name: a milestone without the source counts none of them", GoalCounting.day(noSource, GoalSourceData.forPhone(emptyList(), emptyList(), offset, mLinks.map { it.copy(goalId = noSource.id) }), today, today).value == 0.0)

        // The links read off the task list: open and finished tasks, the History entry of a finished one, once each; no tombstone, no unlinked task.
        val finishedTask = task(32, goalA, TaskBin.DONE) { it.copy(doneAt = now.minusHours(3)) }
        val old = task(35, goalB, TaskBin.DONE) { it.copy(doneAt = now.minusDays(5)) }
        val l = TaskLists(
            listOf(task(31, goalA), finishedTask, task(33), task(34, goalB).let { it.copy(task = it.task.deleted(now)) }), emptyList(), emptyList(),
            listOf(
                SyncTaskLogItem(TaskHistoryEntry(old.id, TaskHistoryKind.FINISHED, now.minusDays(5), old.task, now.minusDays(5))),
                SyncTaskLogItem(TaskHistoryEntry(finishedTask.id, TaskHistoryKind.FINISHED, now.minusHours(3), finishedTask.task, now)),
                SyncTaskLogItem(TaskHistoryEntry(id(36), TaskHistoryKind.DELETED, now, old.task.copy(id = id(36), doneAt = null), now)),
            ),
        )
        val read = GoalTaskLinks.from(l)
        report.check("$name: links off the task list: open and finished once each, the History entry of a finished one, no tombstone, no unlinked task, no deleted entry",
            read.size == 3 && read.count { it.isDone } == 2 && read.any { it.taskId.toString() == id(35) && it.doneAt == old.task.doneAt })
    }

    // ---------------------------------------------------------------- the sources, the goal menu and the goal page's Tasks card

    private fun sourcesAndCard(report: CheckReport) {
        val name = "gs5b/ui-rules"
        report.check("$name: both task sources count on the phone now (not Measured on your PC, not Coming soon)",
            GoalSources.availabilityOf(GoalSourceType.GoalField) == GoalSourceAvailability.Everywhere && GoalSources.availabilityOf(GoalSourceType.MilestoneTasks) == GoalSourceAvailability.Everywhere &&
                GoalPhoneRules.countsOnPhone(GoalSourceType.GoalField) && GoalPhoneRules.countsOnPhone(GoalSourceType.MilestoneTasks) && !GoalPhoneRules.measuredOnPc(GoalSourceType.GoalField))
        val milestone = goal(12, GoalKind.Milestone, listOf(GoalSourceRule(gid(904), GoalSourceType.MilestoneTasks)), GoalTarget(GoalPeriod.Day, null, GoalUnit.Count, GoalDirection.AtLeast, 4.0))
        val day = GoalCounting.day(milestone, GoalSourceData.forPhone(emptyList(), emptyList(), offset), today, today)
        val none = GoalMenuPlan.of(milestone, day, false, 0, "Area").first { it.id == GoalMenuItemId.OpenTasks }
        val some = GoalMenuPlan.of(milestone, day, false, 0, "Area", linkedTasks = 3).first { it.id == GoalMenuItemId.OpenTasks }
        report.check("$name: a milestone's Open its tasks works now and says how many are linked (the PC's words)", none.enabled && none.note == "None linked yet" && some.enabled && some.note == "3 linked")

        val noCard = GoalMenuPlan.of(milestone, day, false, 0, "Area", hasTasksCard = false).first { it.id == GoalMenuItemId.OpenTasks }
        report.check("$name: N4: a milestone whose page has no Tasks card greys Open its tasks and says why", !noCard.enabled && noCard.note == "Add Milestone tasks as a source first")

        val link = { n: Int, title: String, done: OffsetDateTime?, goalId: UUID -> GoalTaskLink(gid(n), goalId, title, done) }
        val g = milestone
        val links = (1..12).map { link(it, "task $it", if (it % 3 == 0) now.minusDays(it.toLong()) else null, g.id) } + link(50, "other", null, gid(998))
        val card = GoalTasksCard.of(g, links)!!
        report.check("$name: the Tasks card lists open tasks first, then the finished ones latest first, at most 8, with a foot",
            card.rows.size == 8 && card.rows.take(8).let { rows -> rows.takeWhile { !it.done }.size == 8 } && card.foot == "4 of 12 finished · 4 more not shown" && card.linked == 12)
        val few = GoalTasksCard.of(g, listOf(link(1, "b task", now, g.id), link(2, "Zed", null, g.id), link(3, "alpha", null, g.id), link(4, "c task", now.minusDays(2), g.id)))!!
        report.check("$name: open ones by title ignoring case, finished ones newest first, \"2 of 4 finished\"", few.rows.map { it.title } == listOf("alpha", "Zed", "b task", "c task") && few.foot == "2 of 4 finished")
        report.check("$name: a goal with its source and no task says how to link one; no source and no link: no card; Deep work: no card",
            GoalTasksCard.of(g, emptyList())?.foot == GoalTasksCard.NONE_LINKED && GoalTasksCard.of(goal(14, GoalKind.Time, listOf(GoalSourceRule(gid(906), GoalSourceType.Hand))), emptyList()) == null &&
                GoalTasksCard.of(com.accountabilibuddy.shared.goals.GoalSeeds.deepWork(now), links) == null)
        report.check("$name: a linked task shows the card whatever the goal's sources say", GoalTasksCard.of(goal(14, GoalKind.Time, listOf(GoalSourceRule(gid(906), GoalSourceType.Hand))), listOf(link(1, "t", null, gid(14))))?.rows?.size == 1)

        val unsourced = GoalTasksCard.of(goal(15, GoalKind.Time, listOf(GoalSourceRule(gid(907), GoalSourceType.Hand))), listOf(link(1, "t", null, gid(15))))!!
        report.check("$name: Q1132: linked tasks but no task source: the card says they don't count until the source is added; with the source there is no note",
            unsourced.note == "They don't count toward this goal until you add the source \"A task carries this goal\"." && GoalTasksCard.of(g, listOf(link(1, "t", null, g.id)))!!.note == null &&
                GoalTasksCard.of(goal(16, GoalKind.Milestone, listOf(GoalSourceRule(gid(908), GoalSourceType.Hand))), listOf(link(1, "t", null, gid(16))))!!.note!!.contains("\"Milestone tasks\""))

        val g1 = goal(21, GoalKind.Habit, emptyList()); val g2 = goal(22, GoalKind.Time, emptyList())
        val lists = GoalLists(goals = listOf(g2, g1, com.accountabilibuddy.shared.goals.GoalSeeds.deepWork(now), g1.copy(id = gid(23), deletedAt = now, position = 3.0)))
        val linkable = GoalTaskLinks.linkable(lists)
        report.check("$name: Link to goal lists the live goals in their order but never Deep work or a deleted goal", linkable.map { it.id } == listOf(g1.id, g2.id))
        report.check("$name: a link to a goal that no longer exists shows as none", GoalTaskLinks.linkedGoal(linkable, g2.id.toString())?.id == g2.id && GoalTaskLinks.linkedGoal(linkable, gid(23).toString()) == null && GoalTaskLinks.linkedGoal(linkable, null) == null)
    }

    // ---------------------------------------------------------------- the wiring (the Compose code is not run here)

    private val app = "../app/src/main/java/com/accountabilibuddy/companion"

    private fun source(path: String): String = java.io.File(path).takeIf { it.exists() }?.readText()?.replace("\r\n", "\n") ?: ""

    private fun wiring(report: CheckReport) {
        val name = "gs5b/wiring"
        val menu = source("$app/ui/tasks/page/TaskMenuSheet.kt")
        val bulk = source("$app/ui/tasks/page/BulkSheet.kt")
        val controller = source("$app/ui/tasks/page/ShelfController.kt")
        report.check("$name: the task menu and select mode's Options have Link to goal, built on the one set of rows and the one controller edit",
            menu.contains("\"Link to goal\"") && menu.contains("TaskGoalRows(") && bulk.contains("\"Link to goal\"") && bulk.contains("TaskGoalRows(") && controller.contains("ShelfEdits.setGoal("))
        val uiDir = java.io.File("$app").walkTopDown().filter { it.isFile && it.extension == "kt" }.map { it.name to it.readText().replace("\r\n", "\n") }.toList()
        val goalRows = source("$app/ui/tasks/page/TaskGoalPage.kt")
        report.check("$name: N1: Unlink is reachable while a task still carries a link to a deleted goal (the rows take hasLink, the menus stay enabled for it)",
            goalRows.contains("goals.isEmpty() && !hasLink") && menu.contains("task.goalId != null") && bulk.contains("tasks.any { it.goalId != null }"))
        val builders = uiDir.filter { (file, text) -> file != "PhoneGoalData.kt" && text.contains("PhoneGoalData.build(") }
        report.check("$name: every place that builds goal numbers passes the task links, so no screen shows less than another (${builders.size} places)",
            builders.size >= 4 && builders.all { (_, text) -> Regex("PhoneGoalData\\.build\\([^\\n]*").findAll(text).all { it.value.contains("taskLinks") } })
        val overviewCallers = uiDir.filter { (file, text) -> file != "PhoneGoalsOverview.kt" && text.contains("PhoneGoalsOverview.build(") }
        report.check("$name: Goals, Today's goals card and Stats pass the links to the overview too", overviewCallers.isNotEmpty() && overviewCallers.all { (_, text) -> text.contains("taskLinks") })
        report.check("$name: the goal page has the Tasks card and scrolls to it for Open its tasks, the goals list opens the page at it",
            source("$app/ui/goals/GoalPageScreen.kt").let { it.contains("TasksCard(") && it.contains("bringIntoView") } && source("$app/ui/goals/GoalsScreen.kt").contains("onOpenGoalTasks") && source("$app/ui/MainScreen.kt").contains("\"tasks\""))
        report.check("$name: the sources sheet lists both task sources as working rows and greys Milestone tasks off a milestone",
            source("$app/ui/goals/GoalSourcesSheet.kt").let { it.contains("add(GoalSourceType.GoalField)") && it.contains("add(GoalSourceType.MilestoneTasks)") && it.contains("kind != GoalKind.Milestone") && !it.contains("Coming soon · Measured") })
    }
}
