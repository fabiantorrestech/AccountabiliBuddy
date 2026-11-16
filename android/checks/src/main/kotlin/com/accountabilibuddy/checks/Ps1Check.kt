package com.accountabilibuddy.checks

import com.accountabilibuddy.companion.stats.FocusWindows
import com.accountabilibuddy.shared.goals.GoalFocusAttribution
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalSourceRule
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.session.PhoneAway
import com.accountabilibuddy.shared.session.PhoneAwayKind
import com.accountabilibuddy.shared.session.PhoneSessionChecklist
import com.accountabilibuddy.shared.session.PhoneSessionRecord
import com.accountabilibuddy.shared.sync.GroupSessions
import com.accountabilibuddy.shared.sync.ReceivedSyncFile
import com.accountabilibuddy.shared.sync.SessionPieces
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.SyncMerge
import com.accountabilibuddy.shared.sync.SyncRunning
import com.accountabilibuddy.shared.sync.SyncSession
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.format.DateTimeParseException
import java.util.UUID

/** One PS1a fixture file (area `ps1`): each case goes through the phone's twin of the PC's rule ([compare] gives null when they agree). */
class Ps1Check(override val name: String, private val compare: (FixtureCase) -> String?) : FixtureCheck {
    override val area = "ps1"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") { compare(case) }
        }
    }
}

/**
 * The comparisons. The C# fixtures carry times with the writer's offset and an `isDone` key on every task and step that the Kotlin writer
 * leaves out (Q1155), so parsed JSON is compared with times as instants, `isDone` ignored, an absent key read as null (C# leaves optional keys
 * out, the Kotlin writer writes nulls) and a step's empty `subtasks` ignored (the Kotlin task writer always adds it, the PC ignores it, Q1157).
 */
object Ps1Cases {
    private val deviceA: UUID = UUID.fromString("33333333-0000-0000-0000-00000000000c")

    // ------------------------------------------------------------------------------------------------------ live (R2, R3, B4)

    fun live(case: FixtureCase): String? {
        val now = time(case.text("now"))
        val files = received(case, now)
        val pcLive: (ReceivedSyncFile) -> SyncRunning? = { GroupSessions.pcRuleOfCore(it, now) }
        val expected = case.expected.jsonObject
        val isLive = files.map { GroupSessions.isLive(it, now, pcLive) }
        val expectedLive = expected.getValue("isLive").jsonArray.map { it.jsonPrimitive.boolean }
        if (isLive != expectedLive) return "isLive: C# $expectedLive vs kotlin $isLive"

        val live = GroupSessions.liveElsewhere(files, now, pcLive)
        val liveJson: JsonElement = live?.let {
            buildJsonObject {
                put("deviceId", it.deviceId.toString())
                put("name", it.name)
                put("kind", it.kind)
                put("endsAt", it.endsAt?.toString())
                put("paused", it.paused)
                put("noSetEnd", it.noSetEnd)
            }
        } ?: JsonNull
        differs("live", expected.getValue("live"), liveJson)?.let { return it }

        val stale = GroupSessions.staleElsewhere(files, now)
        val staleJson: JsonElement = stale?.let {
            buildJsonObject {
                put("deviceId", it.deviceId.toString())
                put("name", it.name)
                put("lastHeardAt", it.lastHeardAt.toString())
            }
        } ?: JsonNull
        return differs("stale", expected.getValue("stale"), staleJson)
    }

    // ------------------------------------------------------------------------------------------------------ pieces

    fun pieces(case: FixtureCase): String? {
        val now = time(case.text("now"))
        val zone = ZoneOffset.ofTotalSeconds(case.input.getValue("zoneOffsetMinutes").jsonPrimitive.int * 60)
        val oldest = (case.input["oldestSessionStart"] as? JsonPrimitive)?.takeIf { it !is JsonNull }?.let { time(it.content) }
        val records = case.input.getValue("records").jsonArray.mapIndexed { index, item -> record(index, item.jsonObject) }
        val built = SessionPieces.build(records, now, zone, oldest)
        val file = SyncFile(1, card(now), built.sessions, emptyList(), null, emptyList(), built.coversFrom)
        val written = SyncFileJson.write(file)
        val expected = case.expected.jsonObject
        differs("sessions", expected.getValue("sessions"), written.getValue("sessions"))?.let { return it }
        differs("coversFrom", expected.getValue("coversFrom"), written.getValue("coversFrom"))?.let { return it }
        // The running card's start of the session (PS1a follow-up): the newest recorded session's start while it runs, else none.
        val runningRecord = records.maxByOrNull { it.startedAt }?.takeIf { it.endedAt == null && case.input.getValue("running").jsonPrimitive.boolean }
        val got: JsonElement = SessionPieces.runningOf(runningRecord ?: return differs("runningSessionStartedAt", case.expected.jsonObject["runningSessionStartedAt"] ?: JsonNull, JsonNull))?.sessionStartedAt?.let { JsonPrimitive(it.toString()) } ?: JsonNull
        return differs("runningSessionStartedAt", case.expected.jsonObject["runningSessionStartedAt"] ?: JsonNull, got)
    }

    // ------------------------------------------------------------------------------------------------------ tasks (labels, B11)

    fun tasks(case: FixtureCase): String? {
        val items = SessionTaskItem.read(case.input.getValue("json").jsonArray)
        val expected = case.expected.jsonObject
        val readable = SessionPieces.readableByPc(items)
        if (readable != expected.getValue("readable").jsonPrimitive.boolean) return "readable: C# ${expected["readable"]} vs kotlin $readable"
        if (!readable) {
            // What the PC would refuse never goes into a file: the task is left out, not written.
            val written = SessionPieces.forSync(items)
            return if (written.isEmpty()) null else "a task the PC can't read would be written: $written"
        }

        val written = SessionTaskItem.write(SessionPieces.forSync(items)) { it.toString() }
        return differs("json", expected.getValue("json"), written)
    }

    // ------------------------------------------------------------------------------------------------------ end-at-once (R9, B6)

    fun endAtOnce(case: FixtureCase): String? {
        val input = case.input
        val tasks = input.getValue("tasks").jsonArray.map { item ->
            val task = item.jsonObject
            SessionTaskItem(
                task.getValue("id").jsonPrimitive.content,
                "task",
                doneAt(task["doneAt"]),
                task.getValue("subtasks").jsonArray.map { step ->
                    SessionTaskItem(step.jsonObject.getValue("id").jsonPrimitive.content, "step", doneAt(step.jsonObject["doneAt"]), emptyList())
                },
            )
        }
        val checklist = PhoneSessionChecklist(
            tasks,
            doneAt(input["sessionStart"]),
            input.getValue("madeBeforeStart").jsonPrimitive.boolean,
            input.getValue("droppedOpen").jsonPrimitive.int,
        )
        val now = time(case.text("now"))
        val expected = case.expected.jsonObject
        val canEnd = checklist.canEndAtOnce(now)
        val allDone = checklist.allDone
        if (canEnd != expected.getValue("canEndAtOnce").jsonPrimitive.boolean) return "canEndAtOnce: C# ${expected["canEndAtOnce"]} vs kotlin $canEnd"
        if (allDone != expected.getValue("allDone").jsonPrimitive.boolean) return "allDone: C# ${expected["allDone"]} vs kotlin $allDone"
        return null
    }

    // ------------------------------------------------------------------------------------------------------ checklist-ops (B6, S3)

    /** The `droppedOpen` sequence: tick toggles; removing or parking an open task adds 1 (a ticked one adds 0); undo puts the last one back and takes 1 off if it was open. */
    fun checklistOps(case: FixtureCase): String? {
        val input = case.input
        val start = time(case.text("sessionStart"))
        val now = time(case.text("now"))
        var tasks = input.getValue("taskIds").jsonArray.map { SessionTaskItem(it.jsonPrimitive.content, "task", null, emptyList()) }
        var checklist = PhoneSessionChecklist(tasks).started(start)
        val removed = ArrayDeque<Triple<SessionTaskItem, Int, Boolean>>()
        fun step(name: String) = buildJsonObject {
            put("step", name)
            put("droppedOpen", checklist.droppedOpen)
            put("openTaskIds", JsonArray(checklist.tasks.filter { !it.isDone }.map { JsonPrimitive(it.id) }))
            put("allDone", checklist.allDone)
            put("canEndAtOnce", checklist.canEndAtOnce(now))
        }
        val got = mutableListOf<JsonElement>(step("start"))
        for (op in input.getValue("ops").jsonArray.map { it.jsonObject }) {
            val kind = op.getValue("op").jsonPrimitive.content
            val id = op.getValue("id").jsonPrimitive.content
            val at = op["at"]?.let { it as? JsonPrimitive }?.takeIf { it !is JsonNull }?.let { time(it.content) } ?: start
            when (kind) {
                "tick" -> checklist = if (checklist.tasks.first { it.id == id }.isDone) checklist.untickTask(id) else checklist.tickTask(id, at)
                "remove", "park" -> {
                    val index = checklist.tasks.indexOfFirst { it.id == id }
                    val task = checklist.tasks[index]
                    removed.addLast(Triple(task, index, !task.isDone))
                    checklist = checklist.withTaskList(checklist.tasks.filterIndexed { i, _ -> i != index })
                }
                "undo" -> {
                    val (task, index, wasOpen) = removed.removeLast()
                    val list = checklist.tasks.toMutableList().also { it.add(index.coerceAtMost(it.size), task) }
                    checklist = checklist.withUndone(list)
                }
                else -> return "unknown op $kind"
            }
            got += step(kind)
        }

        return differs("steps", case.expected.jsonObject.getValue("steps"), JsonArray(got))
    }

    // ------------------------------------------------------------------------------------------------------ merge (B5)

    fun merge(case: FixtureCase): String? {
        val now = time(case.text("now"))
        fun sender(writtenAt: Instant, coversFrom: Instant?, starts: List<Instant>): SyncFile = SyncFile(
            1,
            card(writtenAt),
            starts.sorted().map { SyncSession(it, it.plusSeconds(30 * 60), "Deep work", false) },
            emptyList(), null, emptyList(), coversFrom,
        )
        fun starts(key: String) = case.input.getValue(key).jsonArray.map { time(it.jsonPrimitive.content) }
        val stored = sender(time(case.text("storedWrittenAt")), null, starts("storedStarts"))
        val storedReceivedAt = (case.input["storedReceivedAt"] as? JsonPrimitive)?.takeIf { it !is JsonNull }?.let { time(it.content) }
        val coversFrom = (case.input["incomingCoversFrom"] as? JsonPrimitive)?.takeIf { it !is JsonNull }?.let { time(it.content) }
        val incoming = sender(time(case.text("incomingWrittenAt")), coversFrom, starts("incomingStarts"))
        val kept = SyncMerge.merge(stored, incoming, now, storedReceivedAt).sessions.map { it.start }
        val expected = case.expected.jsonObject.getValue("keptStarts").jsonArray.map { time(it.jsonPrimitive.content) }
        return if (kept == expected) null else "kept starts: C# $expected vs kotlin $kept"
    }

    // ------------------------------------------------------------------------------------------------------ overlap (R4, B4)

    fun overlap(case: FixtureCase): String? {
        val input = case.input
        val now = time(case.text("now"))
        val zone = ZoneOffset.ofTotalSeconds(input.getValue("zoneOffsetMinutes").jsonPrimitive.int * 60)
        // This device's own sessions are one more file here (a phone's own sessions are counted as a file written now).
        val own = input["own"]?.jsonArray.orEmpty().map {
            val session = it.jsonObject
            SyncSession(time(session.getValue("start").jsonPrimitive.content), time(session.getValue("end").jsonPrimitive.content), "Deep work", session.getValue("added").jsonPrimitive.boolean)
        }
        val files = received(case, now) + ReceivedSyncFile(SyncFile(1, card(now, UUID(0, 99)), own, emptyList(), null, emptyList()), now)
        val expected = case.expected.jsonObject

        expected["days"]?.jsonArray?.let { days ->
            val windows = FocusWindows.of(files, zone, now)
            val dates = days.map { java.time.LocalDate.parse(it.jsonObject.getValue("date").jsonPrimitive.content) }
            val counted = FocusWindows.daysIn(windows, dates.first(), dates.last())
            for ((index, day) in days.withIndex()) {
                val want = day.jsonObject.getValue("focusedSeconds").jsonPrimitive.content.toDouble().toLong()
                val got = counted[index].focused.seconds
                if (want != got) return "${dates[index]}: focused seconds C# $want vs kotlin $got"
            }
        }

        expected["goalFocusMinutes"]?.jsonObject?.let { goal ->
            val labelId = UUID.fromString(input.getValue("labelId").jsonPrimitive.content)
            val taskIds = input.getValue("goalTaskIds").jsonArray.map { UUID.fromString(it.jsonPrimitive.content) }.toSet()
            val sessions = files.flatMap { file ->
                GoalFocusAttribution.fromSynced(
                    SyncFileJson.write(file.file).getValue("sessions").jsonArray.map(GoalJson::readPiece), now, zone,
                )
            }
            fun minutes(rule: GoalSourceRule, ids: Set<UUID>? = null): Long =
                GoalFocusAttribution.focusWindows(sessions, listOf(rule), tagsAvailable = false, goalTaskIds = ids)
                    .sumOf { java.time.Duration.between(it.start, it.end).toMinutes() }
            val got = mapOf(
                "all" to minutes(GoalSourceRule(UUID(0, 1), GoalSourceType.FocusAll)),
                "label" to minutes(GoalSourceRule(UUID(0, 2), GoalSourceType.FocusLabel, labelId = labelId)),
                "goalTask" to minutes(GoalSourceRule(UUID(0, 3), GoalSourceType.GoalField), taskIds),
            )
            for ((key, value) in got) {
                val want = goal.getValue(key).jsonPrimitive.content.toDouble().toLong()
                if (want != value) return "goal minutes $key: C# $want vs kotlin $value"
            }
        }

        return null
    }

    // ------------------------------------------------------------------------------------------------------ helpers

    /** The case's files with the time each arrived (the parallel `receivedAt` array); a case without it reads each stamp as its arrival. */
    internal fun received(case: FixtureCase, now: Instant): List<ReceivedSyncFile> {
        val files = case.input.getValue("files").jsonArray.map { readFile(it.jsonObject) }
        val arrivals = case.input["receivedAt"]?.jsonArray?.map { time(it.jsonPrimitive.content) }
        check(arrivals == null || arrivals.size == files.size) { "receivedAt has ${arrivals?.size} entries for ${files.size} files" }
        return files.mapIndexed { index, file -> ReceivedSyncFile(file, arrivals?.get(index) ?: file.device.writtenAt) }
    }

    private fun readFile(json: JsonObject): SyncFile = SyncFileJson.read(json) ?: error("a fixture file the phone cannot read")

    internal fun card(writtenAt: Instant, id: UUID = deviceA) =
        SyncDeviceCard(id, "Pixel", SyncDeviceCard.KIND_PHONE, "0.1.78", writtenAt, listOf(SyncDeviceCard.METHOD_RELAY), false, emptyMap())

    internal fun record(index: Int, json: JsonObject): PhoneSessionRecord {
        val kinds = mapOf("StepAway" to PhoneAwayKind.STEP_AWAY, "Paused" to PhoneAwayKind.PAUSED, "Lunch" to PhoneAwayKind.LUNCH, "DeviceOff" to PhoneAwayKind.DEVICE_OFF)
        val start = time(json.getValue("startedAt").jsonPrimitive.content)
        val tasks = (json["tasks"] as? JsonArray)?.let { SessionTaskItem.read(it) }.orEmpty()
        return PhoneSessionRecord(
            id = "r$index",
            profileId = "p",
            profileName = json.getValue("profile").jsonPrimitive.content,
            startedAt = start,
            phaseStartedAt = start,
            phaseEndsAt = null,
            away = json.getValue("away").jsonArray.map {
                val away = it.jsonObject
                PhoneAway(
                    kinds.getValue(away.getValue("kind").jsonPrimitive.content),
                    time(away.getValue("from").jsonPrimitive.content),
                    (away["to"] as? JsonPrimitive)?.takeIf { value -> value !is JsonNull }?.let { value -> time(value.content) },
                )
            },
            checklist = PhoneSessionChecklist(tasks),
            endedAt = (json["endedAt"] as? JsonPrimitive)?.takeIf { it !is JsonNull }?.let { time(it.content) },
        )
    }

    private fun doneAt(element: JsonElement?): Instant? = (element as? JsonPrimitive)?.takeIf { it !is JsonNull }?.let { time(it.content) }

    internal fun time(text: String): Instant = OffsetDateTime.parse(text).toInstant()

    /** Null when [expected] and [actual] are the same JSON once times are instants and the ignored keys are gone, else a short description. */
    internal fun differs(what: String, expected: JsonElement, actual: JsonElement): String? {
        val want = normal(expected)
        val got = normal(actual)
        return if (want == got) null else "$what differs: C# $want vs kotlin $got"
    }

    private val timeForm = Regex("""\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})""")

    private fun normal(element: JsonElement, inSteps: Boolean = false): JsonElement = when (element) {
        is JsonObject -> JsonObject(
            element.filter { (key, value) -> key != "isDone" && value !is JsonNull && !(inSteps && key == "subtasks" && (value as? JsonArray)?.isEmpty() == true) }
                .mapValues { (key, value) -> normal(value, inSteps = key == "subtasks") },
        )
        is JsonArray -> JsonArray(element.map { normal(it, inSteps) })
        is JsonPrimitive ->
            if (element.isString && timeForm.matches(element.content)) {
                try {
                    JsonPrimitive(OffsetDateTime.parse(element.content).toInstant().toString())
                } catch (e: DateTimeParseException) {
                    element
                }
            } else {
                element
            }
        else -> element
    }
}
