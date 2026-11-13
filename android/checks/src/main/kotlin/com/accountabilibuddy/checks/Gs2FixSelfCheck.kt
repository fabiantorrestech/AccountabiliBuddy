package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalCounting
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalHistoryFileKey
import com.accountabilibuddy.shared.goals.GoalHistoryFileKind
import com.accountabilibuddy.shared.goals.GoalHistoryJson
import com.accountabilibuddy.shared.goals.GoalHistoryListing
import com.accountabilibuddy.shared.goals.GoalHistoryRows
import com.accountabilibuddy.shared.goals.GoalHistoryRules
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalRawItem
import com.accountabilibuddy.shared.goals.GoalSeeds
import com.accountabilibuddy.shared.goals.GoalSyncJson
import com.accountabilibuddy.shared.goals.GoalSyncMerge
import com.accountabilibuddy.shared.sync.SyncGoalParts
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.put

/**
 * GS2 review fixes (phone only, no device): how a "+" is written (a known time straight, every other string as +), non-finite numbers
 * are damage and never written, Health Connect's highest value (-0 equals 0, a NaN never wins), the small JSON reading rules the PC has
 * (a tombstone with a null target, " 1.5", names in any case in history files), and a timing check on 65 000 entries.
 */
object Gs2FixSelfCheck {
    private val T0 = OffsetDateTime.of(2026, 11, 14, 9, 0, 0, 0, ZoneOffset.ofHoursMinutes(5, 30))
    private const val GOAL = "99999999-0000-0000-0000-000000000001"
    private const val AREA = "aaaaaaaa-0000-0000-0000-000000000001"
    private const val BS = '\\'

    fun run(report: CheckReport) {
        plus(report)
        nonFinite(report)
        highest(report)
        reading(report)
        history(report)
        timing(report)
    }

    private fun goalJson(extra: String = "", id: String = GOAL, edited: String = "2026-11-10T09:00:00+05:30") =
        """{"id":"$id","areaId":"$AREA","name":"Run","icon":"x","color":"#fff","kind":"Habit","target":{"period":"Day","amount":1,"unit":"Count","direction":"AtLeast"},""" +
            """"sources":[],"createdAt":"$edited","position":1,"editedAt":"$edited","builtIn":false,"showOnToday":false$extra}"""

    private fun plus(report: CheckReport) {
        val escaped = BS + "u002B"
        val goal = GoalJson.readGoal(Json.parseToJsonElement(goalJson(""","seen":"2026-11-10T09:00:00+05:30","note":"a+b","back":"2026-11-10T09:00:00-08:00"""")))
        val text = GoalJson.text(GoalJson.writeGoal(goal))
        report.check("gs2fix/plus: an unknown field's time has its + written as ${escaped}", text.contains(""""seen":"2026-11-10T09:00:00${escaped}05:30"""") && text.contains(""""note":"a${escaped}b""""))
        report.check("gs2fix/plus: a - offset in an unknown field stays", text.contains(""""back":"2026-11-10T09:00:00-08:00""""))
        report.check("gs2fix/plus: a known time (edited, created) is written straight", text.contains(""""editedAt":"2026-11-10T09:00:00+05:30"""") && text.contains(""""createdAt":"2026-11-10T09:00:00+05:30""""))
        report.check("gs2fix/plus: the text reads back to the same goal and is written the same", GoalJson.text(GoalJson.writeGoal(GoalJson.readGoal(Json.parseToJsonElement(text)))) == text)
        // The mark never leaks: not into the text, not into reading the written tree.
        report.check("gs2fix/plus: no mark in the text", text.none { it.code < 0x20 })
        val tree = GoalJson.writeGoal(goal)
        report.check("gs2fix/plus: the written tree reads back (the mark is dropped by the reader)", GoalJson.readGoal(tree).editedAt == goal.editedAt)
        // A newer app's item kept whole: its own time fields count as unknown text (escaped) until the merge restamps it.
        val newer = Json.parseToJsonElement("""{"id":"$GOAL","editedAt":"2026-11-10T09:00:00+05:30","kind":"Futuristic","seen":"2026-11-10T09:00:00+05:30"}""").jsonObject
        val raw = GoalRawItem.tryRead(newer)!!
        report.check("gs2fix/plus: a raw item's tie-break text escapes every + (as GetRawText of what the PC wrote)", raw.tieBreak.contains(escaped) && !raw.tieBreak.contains("+"))
        val restamped = raw.withStamps(T0.plusMinutes(1), null)
        val restampedText = GoalJson.text(restamped.json)
        report.check(
            "gs2fix/plus: a restamped raw item (the PC reads it back as JSON): every time of it escaped",
            restampedText.contains(""""editedAt":"2026-11-14T09:01:00${escaped}05:30"""") && restampedText.contains(""""seen":"2026-11-10T09:00:00${escaped}05:30""""),
        )
        // The same, through the goal parts of a sync file.
        val parts = buildJsonObject {
            put("goals", JsonArray(listOf(Json.parseToJsonElement(goalJson(""","seen":"2026-11-10T09:00:00+05:30"""")))))
        }
        val read = SyncGoalParts.read(parts)!!
        val written = GoalJson.text(JsonObject(SyncGoalParts.write(read).toMap()))
        report.check("gs2fix/plus: goal parts written as the PC writes them", written.contains(""""seen":"2026-11-10T09:00:00${escaped}05:30"""") && written.contains(""""editedAt":"2026-11-10T09:00:00+05:30""""))
    }

    private fun entryJson(value: String, id: Int = 1) =
        """{"id":"66666666-0000-0000-0000-00000000000$id","goalId":"$GOAL","day":"2026-11-10","value":$value,"source":"Hand","editedAt":"2026-11-10T09:00:00+00:00"}"""

    private fun nonFinite(report: CheckReport) {
        val list = Json.parseToJsonElement(
            "[${entryJson("\"NaN\"", 1)},${entryJson("\"Infinity\"", 2)},${entryJson("\"-Infinity\"", 3)},${entryJson("1e400", 4)},${entryJson("\"1e400\"", 5)},${entryJson("2.5", 6)}]",
        )
        val read = GoalSyncJson.readEntries(list)!!
        report.check("gs2fix/nonfinite: NaN, Infinity, -Infinity and 1e400 (a number or text) are damage: those entries are left out", read.known.map { it.value } == listOf(2.5) && read.newer.isEmpty())
        val position = GoalSyncJson.readGoals(Json.parseToJsonElement("[${goalJson(id = "99999999-0000-0000-0000-000000000002").replace("\"position\":1", "\"position\":\"Infinity\"")}]"))!!
        report.check("gs2fix/nonfinite: a goal with an infinite position is left out", position.known.isEmpty())
        val threw = runCatching { GoalJson.text(buildJsonObject { put("v", JsonPrimitive(Double.NaN)) }) }.isFailure &&
            runCatching { GoalJson.text(buildJsonObject { put("v", JsonPrimitive(Double.POSITIVE_INFINITY)) }) }.isFailure &&
            runCatching { GoalJson.text(buildJsonObject { put("v", GoalJson.number(Double.NEGATIVE_INFINITY)) }) }.isFailure
        report.check("gs2fix/nonfinite: a bare NaN / Infinity is never written", threw)
        report.check("gs2fix/nonfinite: a number the phone did not make (1e400 in an unknown field) goes back as it came", GoalJson.text(JsonObject(mapOf("v" to Json.parseToJsonElement("1e400")))) == """{"v":1e400}""")
    }

    private fun entry(id: Int, device: Int, value: Double) = GoalEntry(
        UUID.fromString("66666666-0000-0000-0000-0000000000$id"), UUID.fromString(GOAL), LocalDate.of(2026, 11, 10), null, value, GoalEntrySource.HealthConnect,
        T0.plusMinutes(id.toLong()), 0.0, null, JsonObject(emptyMap()), UUID.fromString("33333333-0000-0000-0000-0000000000$device"),
    )

    private fun highest(report: CheckReport) {
        val goal = GoalSeeds.deepWork(T0)
        fun pick(vararg values: Double): Pair<Double, GoalEntry>? =
            GoalCounting.combined(goal, GoalEntrySource.HealthConnect, values.mapIndexed { index, v -> entry(10 + index, 10 + index, v) }, false, null)
        report.check("gs2fix/highest: -0 and 0 are equal; the kind's order (newest first) decides, not the sign", pick(-0.0, 0.0)?.second?.id == entry(11, 11, 0.0).id)
        report.check("gs2fix/highest: 0 then -0 gives the same standing entry (order, not sign)", pick(0.0, -0.0)?.second?.id == entry(11, 11, 0.0).id)
        report.check("gs2fix/highest: a NaN never wins over a number", pick(Double.NaN, 3.0)?.first == 3.0 && pick(3.0, Double.NaN)?.first == 3.0 && pick(Double.NaN, -2.0)?.first == -2.0)
        report.check("gs2fix/highest: the highest of several", pick(1.0, 7.5, 3.0)?.first == 7.5)
    }

    private fun reading(report: CheckReport) {
        val tomb = Json.parseToJsonElement(
            """[{"id":"$GOAL","areaId":"$AREA","name":"x","kind":"Habit","target":null,"createdAt":"2026-11-10T09:00:00+00:00","editedAt":"2026-11-10T09:00:00+00:00","deletedAt":"2026-11-11T09:00:00+00:00"}]""",
        )
        val goals = GoalSyncJson.readGoals(tomb)!!
        report.check(
            "gs2fix/reading: a tombstone with \"target\": null is kept with the placeholder target",
            goals.known.size == 1 && goals.known[0].target.period.name == "Day" && goals.known[0].target.unit.name == "Count" && goals.known[0].target.amount == null,
        )
        val live = Json.parseToJsonElement(goalJson().replace(""""target":{"period":"Day","amount":1,"unit":"Count","direction":"AtLeast"}""", """"target":null"""))
        report.check("gs2fix/reading: a live goal with a null target is still left out", GoalSyncJson.readGoals(JsonArray(listOf(live)))!!.known.isEmpty())
        val spaced = GoalSyncJson.readEntries(Json.parseToJsonElement("[${entryJson("\" 1.5\"", 1)},${entryJson("\"1.5 \"", 2)},${entryJson("\"1.5d\"", 3)},${entryJson("\"1.5\"", 4)},${entryJson("\"-2e1\"", 5)}]"))!!
        report.check("gs2fix/reading: a number in text with spaces or a suffix is rejected like .NET; plain text numbers read", spaced.known.map { it.value } == listOf(1.5, -20.0))
    }

    private fun history(report: CheckReport) {
        val month = Json.parseToJsonElement(
            """{"FormatVersion":1,"DeviceId":"33333333-0000-0000-0000-000000000003","Year":"2026","Month":"11","WrittenAt":"2026-11-14T09:00:00+00:00","Entries":[]}""",
        )
        val read = GoalHistoryJson.readMonth(month)
        report.check("gs2fix/history: names in any case and numbers written as text read", read != null && read.year == 2026 && read.month == 11)
        val year = Json.parseToJsonElement(
            """{"formatVersion":1,"year":2026,"rolledBy":"33333333-0000-0000-0000-000000000003","rolledAt":"2027-02-01T00:00:00+00:00","months":[null,{"deviceId":"33333333-0000-0000-0000-000000000003","month":1,"writtenAt":"2026-12-31T00:00:00+00:00"}],"entries":[]}""",
        )
        val readYear = GoalHistoryJson.readYear(year)
        report.check("gs2fix/history: a null month piece in a year file is dropped, the file is read", readYear != null && readYear.months.size == 1)

        // Two listings of one file at the same instant with two offsets are one file read (the PC compares an instant).
        val key = GoalHistoryFileKey(GoalHistoryFileKind.Month, UUID.fromString("33333333-0000-0000-0000-000000000003"), 2026, 11)
        val at = OffsetDateTime.of(2026, 11, 14, 9, 0, 0, 0, ZoneOffset.UTC)
        val rows = listOf(GoalHistoryRows(GoalHistoryListing(key, at), GoalLists()), GoalHistoryRows(GoalHistoryListing(key, at.withOffsetSameInstant(ZoneOffset.ofHours(-5))), GoalLists()))
        val merged = GoalHistoryRules.merge(GoalLists(), null, rows, at.plusHours(1))
        report.check("gs2fix/history: the same file at the same instant in two offsets is listed read once", merged.read.size == 1)

        // Q649: rows of a goal this device has no tombstone for keep their file unread; of a pruned goal they do not.
        val orphan = GoalEntry(
            UUID.fromString("66666666-0000-0000-0000-000000000099"), UUID.fromString(GOAL), LocalDate.of(2026, 11, 10), null, 2.0, GoalEntrySource.Hand, at, 0.0, null,
        )
        val orphanRows = listOf(GoalHistoryRows(GoalHistoryListing(key, at), GoalLists(entries = listOf(orphan))))
        val held = GoalLists(listOf(GoalSeeds.workArea(at)), listOf(GoalSeeds.deepWork(at)))
        val unread = GoalHistoryRules.merge(held, null, orphanRows, at.plusHours(1))
        val pruned = GoalHistoryRules.merge(held, null, orphanRows, at.plusHours(1), setOf(UUID.fromString(GOAL)))
        report.check("gs2fix/q649: rows of an unknown goal leave the file unread; of a goal whose tombstone this device pruned, the file counts as read", unread.read.isEmpty() && pruned.read.size == 1)
    }

    private fun timing(report: CheckReport) {
        val count = 65_000
        val goal = Json.parseToJsonElement(goalJson()).let(GoalJson::readGoal)
        fun entries(shift: Long, valueOf: (Int) -> Double) = (0 until count).map { i ->
            GoalEntry(
                UUID(0x66666666_0000_0000L, i.toLong()), goal.id, LocalDate.of(2026, 1, 1).plusDays((i % 300).toLong()), null, valueOf(i), GoalEntrySource.Hand,
                T0.plusSeconds(shift + i % 50), (i % 7).toDouble(),
            )
        }

        val now = T0.plusDays(1)
        val first = GoalLists(listOf(GoalSeeds.workArea(T0)), listOf(GoalSeeds.deepWork(T0), goal), entries(0) { it.toDouble() })
        val second = GoalLists(listOf(GoalSeeds.workArea(T0)), listOf(GoalSeeds.deepWork(T0), goal), entries(1) { it + 0.5 })
        val start = System.nanoTime()
        val merged = GoalSyncMerge.merge(first, second, now)
        val mergedMs = (System.nanoTime() - start) / 1_000_000
        report.check("gs2fix/timing: merging two lists of $count entries took ${mergedMs} ms (limit 10 s)", merged.entries.size == count && mergedMs < 10_000)
        val startHistory = System.nanoTime()
        val history = GoalHistoryRules.mergeEntries(listOf(GoalLists(entries = first.entries), GoalLists(entries = second.entries)), now)
        val historyMs = (System.nanoTime() - startHistory) / 1_000_000
        report.check("gs2fix/timing: merging two history lists of $count entries took ${historyMs} ms (limit 10 s)", history.entries.size == count && historyMs < 10_000)
        val startText = System.nanoTime()
        val text = GoalHistoryRules.rowsText(merged)
        val textMs = (System.nanoTime() - startText) / 1_000_000
        report.check("gs2fix/timing: writing $count entries as rows took ${textMs} ms (limit 10 s)", text.length > count && textMs < 10_000)
    }
}
