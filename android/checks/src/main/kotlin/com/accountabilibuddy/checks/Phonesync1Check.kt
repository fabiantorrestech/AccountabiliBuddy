package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalDefaults
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeekly
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklySync
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalPhoneSync
import com.accountabilibuddy.shared.goals.GoalSeeds
import com.accountabilibuddy.shared.goals.GoalSyncMerge
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncFileCodec
import com.accountabilibuddy.shared.sync.SyncGoalParts
import com.accountabilibuddy.shared.sync.SyncKey
import com.accountabilibuddy.shared.tasks.SyncTaskItem
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskFieldStamps
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskListRules
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskNumber
import java.time.Duration
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId
import java.util.UUID

/**
 * Phone sync bug (owner device test 2026-11-13): the PCs saw none of the phone's goals, areas and tasks, and the phone none of theirs.
 * Root cause: the sealed file's JSON text was made with JsonObject.toString(), which leaves the U+0001 mark of every DateTimeOffset value in
 * the text (GoalJson.timeValue; only GoalJson.text drops it), so the PC could not read a single time and left out every item that had one.
 * Also: what goes out is never an empty list for a store that has items, and each side's goals and tasks reach the other, both orders.
 */
object Phonesync1Check {
    private const val BACKSLASH = "\\"
    private const val PHONE = "aaaaaaaa-0000-0000-0000-000000000001"
    private const val PC = "bbbbbbbb-0000-0000-0000-000000000002"
    private val now: OffsetDateTime = OffsetDateTime.parse("2026-11-13T17:16:04Z")
    private val zone: ZoneId = ZoneId.of("UTC")
    private val isoTime = Regex("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(\\.\\d+)?([+-]\\d{2}:\\d{2}|Z)$")
    private val timeFields = Regex("\"(createdAt|editedAt|touchedAt|at|writtenAt|deletedAt)\":\"([^\"]*)\"")

    private fun task(n: Int, by: String = PHONE, bin: TaskBin = TaskBin.SORT) = SyncTaskItem(
        TaskItem("9b9b9b9b-0000-0000-0000-%012d".format(n), "Task $n", bin, n.toDouble(), now, now, now, number = TaskNumber(100 + n)),
        TaskFieldStamps.allAt(FieldStamp(now, by)),
    )

    private fun card(name: String, kind: String) =
        SyncDeviceCard(UUID.nameUUIDFromBytes(name.toByteArray()), name, kind, "0.1.78", Instant.parse("2026-11-13T17:16:04Z"), emptyList(), false, emptyMap())

    private fun phoneFile(goals: GoalLists?, tasks: TaskLists?) = SyncFile(
        1, card("phone", SyncDeviceCard.KIND_PHONE), emptyList(), emptyList(), null, emptyList(),
        taskParts = tasks?.let { TaskListRules.fileParts(it, now, zone).second },
        goalParts = goals?.let { GoalPhoneSync.outgoing(it, GoalDeepWorkWeekly(null, stamp = FieldStamp(now, PHONE)), true, now) },
    )

    /** Every time in the text must read as plain ISO 8601: what .NET's DateTimeOffset reader accepts. */
    private fun badTimes(text: String): List<String> = timeFields.findAll(text).map { it.groupValues[2].replace(BACKSLASH + "u002B", "+") }.filter { !isoTime.matches(it) }.toList()

    fun run(report: CheckReport) {
        text(report)
        outgoing(report)
        receive(report)
    }

    private fun text(report: CheckReport) {
        val area = GoalDefaults.newWorkArea(now)
        val goals = GoalLists(listOf(area), listOf(GoalDefaults.newDeepWork(area.id, now), Gs4SelfCheck.goal(1)))
        val file = phoneFile(goals, TaskLists((1..5).map { task(it) }, emptyList(), emptyList(), emptyList()))
        val text = SyncFileCodec.sealedText(file)
        report.check("phonesync1/text: the file's text carries no control mark (U+0001)", text.none { it == '\u0001' } && !text.contains("\\u0001"))
        report.check(
            "phonesync1/text: every time in the text is plain ISO 8601, as the PC reads it",
            badTimes(text).isEmpty() && text.contains("\"createdAt\":\"2026-11-13T17:16:04+00:00\""), badTimes(text).take(2).toString(),
        )
        val key = SyncKey.fromSecret(ByteArray(SyncKey.SECRET_BYTES) { it.toByte() })
        val sealed = SyncFileCodec.seal(file, key)
        val wire = SyncFileCodec.openJson(sealed, key, "ABS1".toByteArray(Charsets.US_ASCII))?.toString(Charsets.UTF_8) ?: ""
        report.check("phonesync1/text: the sealed bytes themselves hold the clean text (what the PC opens)", wire.isNotEmpty() && wire.none { it == (1).toChar() } && !wire.contains(BACKSLASH + "u0001") && badTimes(wire).isEmpty())
        val back = SyncFileCodec.open(sealed, key)
        report.check(
            "phonesync1/text: sealed and opened, the phone's own reader takes in every goal, area and task",
            back?.goalParts?.goals?.size == 2 && back.goalParts?.areas?.size == 1 && back.taskParts?.tasks?.size == 5, "${back?.goalParts?.goals?.size}/${back?.taskParts?.tasks?.size}",
        )
    }

    private fun outgoing(report: CheckReport) {
        val weekly = GoalDeepWorkWeekly(null, stamp = FieldStamp(now, PHONE))
        val goals = GoalLists(listOf(GoalSeeds.workArea(now)), listOf(GoalSeeds.deepWork(now), Gs4SelfCheck.goal(1), Gs4SelfCheck.goal(2)))
        val out = GoalPhoneSync.outgoing(goals, weekly, true, now)
        report.check("phonesync1/out: a phone with 3 goals and an area writes 3 and 1 (never an empty list)", out?.goals?.size == 3 && out.areas?.size == 1)
        report.check(
            "phonesync1/out: goals not ready write no goal part at all (null, never [])",
            GoalPhoneSync.outgoing(goals, weekly, false, now) == null && GoalPhoneSync.outgoing(GoalLists.EMPTY, weekly, true, now) == null,
        )
        val tasks = TaskListRules.fileParts(TaskLists((1..7).map { task(it) }, emptyList(), emptyList(), emptyList()), now, zone).second
        report.check("phonesync1/out: a phone with 7 tasks writes 7", tasks.tasks?.size == 7)
    }

    private fun receive(report: CheckReport) {
        val env = GoalDeepWorkWeeklySync.Env(false, false, now, Duration.ofMinutes(10))
        val weekly = GoalDeepWorkWeekly(null, stamp = FieldStamp(now, PHONE))
        val pcArea = GoalSeeds.workArea(now.minusDays(2))
        val pcGoals = GoalLists(listOf(pcArea), listOf(GoalSeeds.deepWork(now.minusDays(2)), Gs4SelfCheck.goal(1), Gs4SelfCheck.goal(2)))
        val pcParts = SyncGoalParts.into(pcGoals)
        val phoneArea = GoalDefaults.newWorkArea(now)
        val phone = GoalLists(listOf(phoneArea), listOf(GoalDefaults.newDeepWork(phoneArea.id, now)))
        val taken = GoalPhoneSync.applied(phone, weekly, pcParts, true, now.plusMinutes(1), env, 120.0)
        val live = taken?.lists?.goals?.count { it.isLive }
        report.check("phonesync1/in: a PC file with 3 goals gives a first-run phone 3 goals (Deep work once)", live == 3, "$live")
        val phoneWith = GoalLists(listOf(phoneArea), listOf(GoalDefaults.newDeepWork(phoneArea.id, now), Gs4SelfCheck.goal(3)))
        val other = GoalPhoneSync.applied(phoneWith, weekly, pcParts, true, now.plusMinutes(1), env, 120.0)
        report.check("phonesync1/in: a phone with a goal of its own keeps it and gains the PC's", other?.lists?.goals?.count { it.isLive } == 4, "${other?.lists?.goals?.count { it.isLive }}")
        val phoneOut = SyncGoalParts.from(GoalPhoneSync.outgoing(phoneWith, weekly, true, now))!!
        val onPcGoals = GoalSyncMerge.mergeReported(pcGoals, phoneOut, now.plusMinutes(1)).lists
        report.check("phonesync1/in: the other order, the PC takes the phone's goal in too", onPcGoals.goals.count { it.isLive } == 4, "${onPcGoals.goals.count { it.isLive }}")

        val pcTasks = TaskLists((1..42).map { task(it, PC, TaskBin.NEXT) }, emptyList(), emptyList(), emptyList())
        val pcFileParts = TaskListRules.fileParts(pcTasks, now, zone).second
        val phoneTasks = TaskLists((101..102).map { task(it, PHONE) }, emptyList(), emptyList(), emptyList())
        val merged = TaskListRules.mergeFile(phoneTasks, pcFileParts, PHONE, now.plusMinutes(1))
        report.check("phonesync1/in: a PC file with 42 tasks gives a phone with 2 tasks 44", merged?.tasks?.count { it.task.isLive } == 44, "${merged?.tasks?.count { it.task.isLive }}")
        val phoneParts = TaskListRules.fileParts(phoneTasks, now, zone).second
        val onPc = TaskListRules.mergeFile(pcTasks, phoneParts, PC, now.plusMinutes(1))
        report.check("phonesync1/in: the other order, the PC takes the phone's 2 tasks in too", onPc?.tasks?.count { it.task.isLive } == 44, "${onPc?.tasks?.count { it.task.isLive }}")
    }
}
