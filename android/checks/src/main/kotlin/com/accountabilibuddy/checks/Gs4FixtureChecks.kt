package com.accountabilibuddy.checks

import com.accountabilibuddy.companion.goals.PhoneGoalHistoryCodec
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalHistoryFileKey
import com.accountabilibuddy.shared.goals.GoalHistoryJson
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalHistoryNames
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.sync.SyncKey
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.util.UUID

/**
 * GS4 review fixtures (area `gs4`, written by the C# checks' `Gs4FixFixtures`): the goal history file names against the PC's
 * `GoalHistoryNames` (and the relay reader's `isHistoryName` over every name a group can have), and the PC's own sealed ABM1 / ABY1 files
 * opened by the phone's codec ([PhoneGoalHistoryCodec]) under the group key made from the link-code secret (the key path the sync files
 * use; no DPAPI is involved), JSON byte for byte after the phone writes what it opened.
 */
class Gs4Check(override val name: String) : FixtureCheck {
    override val area = "gs4"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                when (name) {
                    "names" -> names(case)
                    else -> sealed(case)
                }
            }
        }
    }

    private fun uuid(text: String) = UUID.fromString(text)

    private fun names(case: FixtureCase): String? {
        val groupId = case.text("groupId")
        val extension = case.text("extension")
        return when (case.text("kind")) {
            "name" -> {
                val device = uuid(case.text("device"))
                val year = case.input.getValue("year").jsonPrimitive.intOrNull!!
                val key = if (case.text("history") == "month") GoalHistoryFileKey.forMonth(device, year, case.input.getValue("month").jsonPrimitive.intOrNull!!)
                else GoalHistoryFileKey.forYear(year, device)
                val actual = GoalHistoryNames.nameFor(groupId, key, extension)
                val expected = case.expected.jsonPrimitive.content
                if (actual == expected) null else "kotlin $actual vs C# $expected"
            }
            else -> {
                val devices = case.input.getValue("devices").jsonArray.map { uuid(it.jsonPrimitive.content) }
                val lastYear = case.input.getValue("lastYear").jsonPrimitive.intOrNull!!
                val expected = case.expected.jsonArray.map { it.jsonPrimitive.content }
                val actual = GoalHistoryNames.candidates(groupId, extension, devices, lastYear).keys.sorted()
                if (actual != expected) return "candidates differ: kotlin ${actual.size} names vs C# ${expected.size}"
                // The relay reader leaves exactly these alone: every name a group can have is a history name, a sync file's name is not.
                val sync = Gs4SelfCheck.syncFor(groupId, devices)
                val missed = expected.filterNot { sync.isHistoryName(it, extension) }
                val strayName = "0123456789ab$extension"
                if (missed.isNotEmpty()) "isHistoryName misses ${missed.size} of ${expected.size} names"
                else if (sync.isHistoryName(strayName, extension)) "isHistoryName accepts a name that is not a history file's"
                else null
            }
        }
    }

    private fun hex(text: String) = ByteArray(text.length / 2) { text.substring(it * 2, it * 2 + 2).toInt(16).toByte() }

    private fun hexOf(bytes: ByteArray) = bytes.joinToString("") { "%02x".format(it) }

    private fun entryLines(entries: GoalLists) = entries.entries.map { entry -> entryLine(entry) }

    private fun entryLine(e: GoalEntry) = "${e.id}|${e.day}|${e.value}|${e.deletedAt != null}"

    private fun expectedLines(expected: JsonObject) = expected.getValue("entries").jsonArray.map {
        val o = it.jsonObject
        "${o.getValue("id").jsonPrimitive.content}|${o.getValue("day").jsonPrimitive.content}|${o.getValue("value").jsonPrimitive.doubleOrNull}|${o.getValue("deleted").jsonPrimitive.content}"
    }

    private fun sealed(case: FixtureCase): String? {
        val secret = hex(case.text("openSecret"))
        val key = SyncKey.fromSecret(secret)
        val bytes = java.util.Base64.getDecoder().decode(case.text("bytes"))
        val codec = PhoneGoalHistoryCodec { key }
        val kind = case.text("kind")
        val expected = case.expected
        if (expected is JsonPrimitive) {
            // "none": another group's file, or the other kind of file.
            val opened = if (kind == "month") codec.openMonth(bytes) else codec.openYear(bytes)
            return if (opened == null) null else "opened a file it must refuse"
        }

        // The key path: the group's id and key from the secret are the PC's.
        if (key.groupId != case.text("groupId") || hexOf(key.key) != case.text("keyHex")) return "the group key from the secret differs from the PC's"
        val want = expected.jsonObject
        if (kind == "month") {
            val month = codec.openMonth(bytes) ?: return "the phone's codec could not open the PC's month file"
            if (month.deviceId != uuid(want.getValue("device").jsonPrimitive.content) || month.year != want.getValue("year").jsonPrimitive.intOrNull || month.month != want.getValue("month").jsonPrimitive.intOrNull) {
                return "month header differs: ${month.deviceId} ${month.year}-${month.month}"
            }

            if (entryLines(month.entries) != expectedLines(want)) return "entries differ: ${entryLines(month.entries)} vs ${expectedLines(want)}"
            // The phone writes the same JSON text the PC wrote (nothing is changed by being read).
            val wire = case.wire() ?: return "the fixture has no wire text"
            return GoalCaseRunner.sameText(wire, GoalJson.text(GoalHistoryJson.writeMonth(month)))
        }

        val year = codec.openYear(bytes) ?: return "the phone's codec could not open the PC's year file"
        if (year.rolledBy != uuid(want.getValue("rolledBy").jsonPrimitive.content) || year.year != want.getValue("year").jsonPrimitive.intOrNull) return "year header differs"
        val pieces = year.months.map { "${it.deviceId}|${it.month}" }
        if (pieces != want.getValue("pieces").jsonArray.map { it.jsonPrimitive.content }) return "the months taken differ: $pieces"
        if (entryLines(year.entries) != expectedLines(want)) return "entries differ"
        val wire = case.wire() ?: return "the fixture has no wire text"
        return GoalCaseRunner.sameText(wire, GoalJson.text(GoalHistoryJson.writeYear(year)))
    }
}
