package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalHistoryDevice
import com.accountabilibuddy.shared.goals.GoalHistoryFileKey
import com.accountabilibuddy.shared.goals.GoalHistoryLatePrune
import com.accountabilibuddy.shared.goals.GoalHistoryListing
import com.accountabilibuddy.shared.goals.GoalHistoryRows
import com.accountabilibuddy.shared.goals.GoalHistoryRules
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalLists
import java.time.LocalDate
import java.time.OffsetDateTime
import java.util.UUID
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/**
 * GS3b Kotlin relay mirror: the phone's twins of the PC's relay history rules (quota, keep, version gate, late joiner, "all accounted for")
 * against the C# fixtures. Area `gs3bfix` (the single rules, written by `Gs3bFixCoreChecks.WriteAll`) and area `gs3bk` (the plan over many
 * devices and years, the merge with the late joiner's rule, more version texts; `Gs3bkFixtures`).
 */
class Gs3bkFixtureCheck(override val area: String, override val name: String) : FixtureCheck {
    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                when ("$area/$name") {
                    "gs3bfix/relay-quota" -> quota(case)
                    "gs3bfix/relay-keep" -> keep(case)
                    "gs3bfix/knows-history", "gs3bk/version-gate" -> gate(case)
                    "gs3bfix/late-prunable" -> latePrunable(case)
                    "gs3bfix/all-accounted" -> accounted(case)
                    "gs3bk/relay-plan" -> plan(case)
                    "gs3bk/late-merge" -> lateMerge(case)
                    else -> "no check for $area/$name"
                }
            }
        }
    }

    private fun time(text: String): OffsetDateTime = GoalJson.parseTime(text)

    private fun JsonObject.int(key: String): Int = getValue(key).jsonPrimitive.intOrNull!!

    private fun JsonObject.flag(key: String): Boolean = getValue(key).jsonPrimitive.booleanOrNull!!

    private fun JsonObject.text(key: String): String = getValue(key).jsonPrimitive.content

    private fun JsonObject.optText(key: String): String? = (get(key) as? JsonPrimitive)?.takeIf { it !is JsonNull }?.contentOrNull

    private fun JsonObject.texts(key: String): List<String> = getValue(key).jsonArray.map { it.jsonPrimitive.content }

    private fun JsonObject.ids(key: String): List<UUID> = texts(key).map(UUID::fromString)

    private fun JsonObject.times(key: String): Map<UUID, OffsetDateTime> = getValue(key).jsonObject.entries.associate { UUID.fromString(it.key) to time(it.value.jsonPrimitive.content) }

    // "yyyy-MM" as the fixtures write a month.
    private fun month(text: String): Pair<Int, Int> = text.substring(0, 4).toInt() to text.substring(5).toInt()

    private fun monthText(month: Pair<Int, Int>) = "%04d-%02d".format(month.first, month.second)

    // "Kind|deviceId|year|month" as the C# writer's key text.
    private fun fileKey(text: String): GoalHistoryFileKey {
        val parts = text.split('|')
        val device = UUID.fromString(parts[1])
        return if (parts[0] == "Month") GoalHistoryFileKey.forMonth(device, parts[2].toInt(), parts[3].toInt()) else GoalHistoryFileKey.forYear(parts[2].toInt(), device)
    }

    private fun keyText(key: GoalHistoryFileKey) =
        "${if (key.kind == com.accountabilibuddy.shared.goals.GoalHistoryFileKind.Month) "Month" else "Year"}|${key.deviceId}|${key.year}|${key.month ?: ""}"

    private fun compare(expected: List<String>, actual: List<String>): String? = if (expected == actual) null else "C# $expected vs kotlin $actual"

    private fun quota(case: FixtureCase): String? {
        val actual = GoalHistoryRules.relayQuota(case.input.int("yearFiles"), case.input.int("knownDevices"))
        val expected = case.expected.jsonPrimitive.intOrNull!!
        return if (actual == expected) null else "C# $expected vs kotlin $actual"
    }

    private fun keep(case: FixtureCase): String? {
        val months = case.input.texts("months").map(::month)
        val actual = GoalHistoryRules.relayKeep(months, case.input.flag("isRoller"), case.input.int("quota"), time(case.text("now"))).map(::monthText)
        return compare(case.expected.jsonArray.map { it.jsonPrimitive.content }, actual)
    }

    private fun gate(case: FixtureCase): String? {
        val actual = GoalHistoryRules.knowsHistory(case.input.optText("kind"), case.input.optText("appVersion"))
        val expected = case.expected.jsonPrimitive.booleanOrNull!!
        return if (actual == expected) null else "C# $expected vs kotlin $actual"
    }

    private fun entry(row: JsonObject): GoalEntry {
        val stamp = time(row.text("stamp"))
        return GoalEntry(UUID.randomUUID(), UUID.fromString(row.text("goalId")), LocalDate.of(stamp.year, stamp.monthValue, stamp.dayOfMonth), null, 1.0, GoalEntrySource.Hand, stamp)
    }

    private fun latePrunable(case: FixtureCase): String? {
        val input = case.input
        val dropped = input.getValue("dropped").jsonArray.map { entry(it.jsonObject) }
        val active = input.ids("active").map { GoalHistoryDevice(it, true, null) }
        val actual = GoalHistoryRules.latePrunable(
            dropped, input.optText("firstDropAt")?.let(::time), active, UUID.fromString(input.text("self")), input.times("lastRegularMerge"),
            input.ids("alreadyPruned").toSet(), time(input.text("now")),
        )
        return compare(case.expected.jsonArray.map { it.jsonPrimitive.content }, actual.map { it.toString() })
    }

    private fun accounted(case: FixtureCase): String? {
        val listing = case.input.getValue("listing").jsonArray.map { GoalHistoryListing(fileKey(it.jsonObject.text("key")), time(it.jsonObject.text("modifiedAt"))) }
        val lastRead = case.input.getValue("lastRead").jsonObject.entries.associate { fileKey(it.key) to time(it.value.jsonPrimitive.content) }
        val skipped = case.input.texts("skipped").map(::fileKey).toSet()
        val actual = GoalHistoryRules.allAccountedFor(listing, lastRead, skipped)
        val expected = case.expected.jsonPrimitive.booleanOrNull!!
        return if (actual == expected) null else "C# $expected vs kotlin $actual"
    }

    private fun plan(case: FixtureCase): String? {
        val input = case.input
        val quota = GoalHistoryRules.relayQuota(input.int("yearFiles"), input.int("knownDevices"))
        val keep = GoalHistoryRules.relayKeep(input.texts("months").map(::month), input.flag("isRoller"), quota, time(input.text("now"))).map(::monthText)
        val expected = case.expected.jsonObject
        val expectedQuota = expected.int("quota")
        return when {
            quota != expectedQuota -> "quota C# $expectedQuota vs kotlin $quota"
            else -> compare(expected.texts("keep"), keep)
        }
    }

    private fun lateMerge(case: FixtureCase): String? {
        val input = case.input
        val files = input.getValue("files").jsonArray.map { element ->
            val file = element.jsonObject
            val rows = file.getValue("rows").jsonArray.map { row ->
                val obj = row.jsonObject
                entry(obj).copy(id = UUID.fromString(obj.text("id")))
            }
            GoalHistoryRows(GoalHistoryListing(fileKey(file.text("key")), time(file.text("modifiedAt"))), GoalLists(entries = rows))
        }
        val self = UUID.fromString(input.text("self"))
        val late = if (input.flag("useRule")) {
            GoalHistoryLatePrune(input.optText("firstDropAt")?.let(::time), input.ids("active").map { GoalHistoryDevice(it, true, null) }, self, input.times("lastRegularMerge"))
        } else {
            null
        }

        val merged = GoalHistoryRules.merge(GoalLists.EMPTY, null, files, time(input.text("now")), input.ids("pruned").toSet(), late)
        val expected = case.expected.jsonObject
        val read = merged.read.map { keyText(it.key) }
        val pruned = merged.newlyPruned.map { it.toString() }
        return compare(expected.texts("read"), read) ?: compare(expected.texts("newlyPruned"), pruned)
            ?: if (merged.dropped.size == expected.int("dropped")) null else "dropped C# ${expected.int("dropped")} vs kotlin ${merged.dropped.size}"
    }
}
