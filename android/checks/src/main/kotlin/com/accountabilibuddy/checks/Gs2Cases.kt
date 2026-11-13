package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalCounting
import com.accountabilibuddy.shared.goals.GoalDeepWorkSync
import com.accountabilibuddy.shared.goals.GoalDeepWorkTargets
import com.accountabilibuddy.shared.goals.GoalDerivedCover
import com.accountabilibuddy.shared.goals.GoalDerivedIds
import com.accountabilibuddy.shared.goals.GoalDerivedReading
import com.accountabilibuddy.shared.goals.GoalDerivedWriter
import com.accountabilibuddy.shared.goals.GoalEdits
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalFocusAttribution
import com.accountabilibuddy.shared.goals.GoalGroupDevice
import com.accountabilibuddy.shared.goals.GoalHistoryDevice
import com.accountabilibuddy.shared.goals.GoalHistoryJson
import com.accountabilibuddy.shared.goals.GoalHistoryListing
import com.accountabilibuddy.shared.goals.GoalHistoryMerged
import com.accountabilibuddy.shared.goals.GoalHistoryRows
import com.accountabilibuddy.shared.goals.GoalHistoryRules
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalRawItem
import com.accountabilibuddy.shared.goals.GoalReviewIds
import com.accountabilibuddy.shared.goals.GoalSourceData
import com.accountabilibuddy.shared.goals.GoalStampJson
import com.accountabilibuddy.shared.goals.GoalSyncMerge
import com.accountabilibuddy.shared.goals.GoalSyncWindow
import com.accountabilibuddy.shared.goals.GoalTombstones
import com.accountabilibuddy.shared.goals.GoalWindow
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.SyncGoalParts
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.double
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put

/** The GS1a / GS1b fixtures (ADR 79 goals sync) run through the phone's twins; each returns null when they agree with the PC. */
object Gs2Cases {
    // ---------------------------------------------------------------- gs1a

    /** The merge both ways, and merging the expected result with either input again (nothing changes). */
    fun merge(case: FixtureCase): String? {
        val first = Gs2Json.lists(case.input.getValue("first"))
        val second = Gs2Json.lists(case.input.getValue("second"))
        val now = Gs2Json.time(case.input.getValue("now"))
        val expected = case.expected
        val expectedLists = Gs2Json.lists(expected)
        return Gs2Json.same(expected, Gs2Json.parts(GoalSyncMerge.merge(first, second, now)), case.wire()).tag("first+second")
            ?: Gs2Json.same(expected, Gs2Json.parts(GoalSyncMerge.merge(second, first, now)), case.wire()).tag("second+first")
            ?: Gs2Json.same(expected, Gs2Json.parts(GoalSyncMerge.merge(expectedLists, first, now)), case.wire()).tag("expected+first")
            ?: Gs2Json.same(expected, Gs2Json.parts(GoalSyncMerge.merge(second, expectedLists, now)), case.wire()).tag("second+expected")
            ?: Gs2Json.same(expected, Gs2Json.parts(GoalSyncMerge.merge(expectedLists, expectedLists, now)), case.wire()).tag("expected+expected")
            ?: roundTrip(expected, case.wire()).tag("PC -> phone -> PC")
    }

    fun reviewId(case: FixtureCase): String? {
        val actual = GoalReviewIds.forWeek(LocalDate.parse(case.text("week"))).toString()
        return GoalCaseRunner.sameText(case.expected.jsonPrimitive.content, actual)
    }

    /** Three devices' records: every order of a, b, c, folded either way round. */
    fun reviewThree(case: FixtureCase): String? {
        val inputs = listOf("a", "b", "c").map { Gs2Json.lists(case.input.getValue(it)) }
        val now = Gs2Json.time(case.input.getValue("now"))
        for (order in permutations(listOf(0, 1, 2))) {
            val (x, y, z) = order.map { inputs[it] }
            Gs2Json.same(case.expected, Gs2Json.parts(GoalSyncMerge.merge(GoalSyncMerge.merge(x, y, now), z, now)), case.wire())?.let { return "order $order (x+y)+z: $it" }
            Gs2Json.same(case.expected, Gs2Json.parts(GoalSyncMerge.merge(x, GoalSyncMerge.merge(y, z, now), now)), case.wire())?.let { return "order $order x+(y+z): $it" }
        }

        return null
    }

    fun deepWork(case: FixtureCase): String? {
        val stored = GoalJson.readGoal(case.input.getValue("stored"))
        val targets = targets(case.input.getValue("targets"))
        val composed = GoalDeepWorkSync.compose(stored, targets)
        val (split, received) = GoalDeepWorkSync.split(composed)
        val expected = case.expected.jsonObject
        return Gs2Json.same(expected.getValue("composed"), GoalJson.writeGoal(composed), case.wire("composed")).tag("composed")
            ?: Gs2Json.same(expected.getValue("split"), GoalJson.writeGoal(split), case.wire("split")).tag("split")
            ?: Gs2Json.same(expected.getValue("received"), targetsJson(received)).tag("received")
    }

    fun deepWorkSplit(case: FixtureCase): String? {
        val (split, received) = GoalDeepWorkSync.split(GoalJson.readGoal(case.input.getValue("record")))
        val expected = case.expected.jsonObject
        return Gs2Json.same(expected.getValue("split"), GoalJson.writeGoal(split), case.wire("split")).tag("split")
            ?: Gs2Json.same(expected.getValue("received"), targetsJson(received)).tag("received")
    }

    fun edits(case: FixtureCase): String? {
        val before = GoalJson.readGoal(case.input.getValue("before"))
        val after = GoalJson.readGoal(case.input.getValue("after"))
        val actual = GoalEdits.restamp(before, after, Gs2Json.time(case.input.getValue("now")), case.text("by"))
        return Gs2Json.same(case.expected, GoalJson.writeGoal(actual), case.wire())
    }

    fun tombstones(case: FixtureCase): String? {
        val goal = GoalJson.readGoal(case.input.getValue("goal"))
        return Gs2Json.same(case.expected, GoalJson.writeGoal(GoalTombstones.delete(goal, Gs2Json.time(case.input.getValue("at")))), case.wire())
    }

    fun parts(case: FixtureCase): String? {
        val lists = Gs2Json.lists(case.input.getValue("parts"))
        val file = SyncGoalParts.into(lists)
        val back = SyncGoalParts.from(file) ?: return "no parts came back"
        return Gs2Json.same(case.expected, Gs2Json.parts(back), case.wire())
    }

    // ---------------------------------------------------------------- gs1b: entries, rows, derived values

    fun derivedId(case: FixtureCase): String? {
        val input = case.input
        val actual = GoalDerivedIds.forEntry(
            Gs2Json.id(input.getValue("goalId")), GoalEntrySource.valueOf(case.text("source")), Gs2Json.id(input.getValue("deviceId")), LocalDate.parse(case.text("day")),
        )
        return GoalCaseRunner.sameText(case.expected.jsonPrimitive.content, actual.toString())
    }

    /** Object form -> rows text exactly; rows -> back in object form. */
    fun rows(case: FixtureCase): String? {
        val entries = Gs2Json.entriesList(case.input.getValue("entries"))
        val expected = case.expected.jsonObject
        val text = Gs2Json.rowsText(entries)
        GoalCaseRunner.sameText(expected.getValue("rows").jsonPrimitive.content, text)?.let { return "rows: $it" }
        val back = Gs2Json.rows(text) ?: return "rows did not read back"
        return Gs2Json.same(expected.getValue("back"), Gs2Json.objectForm(back), case.wire("back")).tag("back")
    }

    fun rowsRead(case: FixtureCase): String? {
        val text = case.input.getValue("value").jsonPrimitive.content
        val read = Gs2Json.rows(text)
        if (case.expected is JsonNull) return if (read == null) null else "expected not a list"
        if (read == null) return "read as not a list"
        val expected = case.expected.jsonObject
        if (expected.getValue("known").jsonPrimitive.int != read.entries.size) return "known ${read.entries.size} vs ${expected.getValue("known")}"
        if (expected.getValue("newer").jsonPrimitive.int != read.newer.entries.size) return "newer ${read.newer.entries.size} vs ${expected.getValue("newer")}"
        return GoalCaseRunner.sameText(expected.getValue("again").jsonPrimitive.content, Gs2Json.rowsText(read))
    }

    fun window(case: FixtureCase): String? {
        val lists = Gs2Json.rows(case.text("rows")) ?: return "rows did not read"
        val recent = GoalSyncWindow.recent(lists, Gs2Json.time(case.input.getValue("now")))
        return GoalCaseRunner.sameText(case.expected.jsonPrimitive.content, Gs2Json.rowsText(recent))
    }

    fun writer(case: FixtureCase): String? {
        val input = case.input
        val held = Gs2Json.entriesList(input.getValue("held")).entries
        val readings = input.getValue("readings").jsonArray.map { item ->
            val obj = item.jsonObject
            GoalDerivedReading(
                Gs2Json.id(obj.getValue("goalId")), GoalEntrySource.valueOf(obj.getValue("source").jsonPrimitive.content),
                LocalDate.parse(obj.getValue("day").jsonPrimitive.content), number(obj.getValue("value")),
            )
        }
        val covered = input.getValue("covered").jsonArray.map { item ->
            val obj = item.jsonObject
            GoalDerivedCover(
                Gs2Json.id(obj.getValue("goalId")), GoalEntrySource.valueOf(obj.getValue("source").jsonPrimitive.content),
                LocalDate.parse(obj.getValue("from").jsonPrimitive.content), LocalDate.parse(obj.getValue("to").jsonPrimitive.content),
            )
        }
        val changes = GoalDerivedWriter.changes(held, Gs2Json.id(input.getValue("deviceId")), readings, covered, Gs2Json.time(input.getValue("now")))
        return Gs2Json.same(case.expected, GoalJson.writeEntries(changes), case.wire())
    }

    fun counting(case: FixtureCase): String? {
        val input = case.input
        val zone = ZoneOffset.ofTotalSeconds(input.getValue("zoneOffsetMinutes").jsonPrimitive.int * 60)
        val now = Gs2Json.time(input.getValue("now")).toInstant()
        val focus = input.getValue("devices").jsonArray.flatMap { device -> GoalFocusAttribution.fromSynced(device.jsonArray.map(GoalJson::readPiece), now, zone) }
        val calendar = Gs2Json.nullable(input["calendar"])?.jsonObject?.entries?.associate { (category, windows) ->
            UUID.fromString(category) to windows.jsonArray.map { GoalWindow(LocalDateTime.parse(it.jsonObject.getValue("start").jsonPrimitive.content), LocalDateTime.parse(it.jsonObject.getValue("end").jsonPrimitive.content)) }
        }
        val data = GoalSourceData(
            focus, Gs2Json.entriesList(input.getValue("entries")).entries, zone, calendar, input.getValue("tagsAvailable").jsonPrimitive.boolean,
            SyncFileJson.readSteps(Gs2Json.nullable(input["steps"])), Gs2Json.nullable(input["deviceId"])?.let(Gs2Json::id),
        )
        val goal = GoalJson.readGoal(input.getValue("goal"))
        val state = GoalCounting.day(goal, data, LocalDate.parse(case.text("day")), LocalDate.parse(case.text("today")))
        val expected = case.expected.jsonObject
        val actual = buildJsonObject {
            put("value", GoalJson.number(state.value))
            put("minutes", GoalJson.number(state.minutes))
            put("hasValue", state.hasValue)
            put("hit", state.hit)
            put("status", state.status.name)
            put("short", GoalJson.number(state.short))
            put("sources", JsonArray(state.sources.map { JsonPrimitive(it.name) }))
            put("byHand", state.byHand)
        }
        return JsonParity.difference(expected, actual)
    }

    // ---------------------------------------------------------------- gs1b: history

    fun monthWrite(case: FixtureCase): String? {
        val input = case.input
        val last = Gs2Json.nullable(input["last"])?.let { Gs2Json.month(it) }
        val held = Gs2Json.rows(case.text("held")) ?: return "held rows"
        val file = GoalHistoryRules.monthToWrite(
            last, held, Gs2Json.id(input.getValue("deviceId")), input.getValue("year").jsonPrimitive.int, input.getValue("month").jsonPrimitive.int,
            Gs2Json.time(input.getValue("now")),
        )
        if (case.expected is JsonNull) return if (file == null) null else "wrote a file, expected none"
        if (file == null) return "wrote nothing, expected a file"
        return Gs2Json.same(case.expected, GoalHistoryJson.writeMonth(file)) ?: Gs2Json.same(case.expected, GoalHistoryJson.writeMonth(Gs2Json.month(case.expected))).tag("PC -> phone -> PC")
    }

    fun toRead(case: FixtureCase): String? {
        val input = case.input
        val listing = input.getValue("listing").jsonArray.map(Gs2Json::listing)
        val lastRead = input.getValue("lastRead").jsonArray.associate { Gs2Json.key(it.jsonObject.getValue("key")) to Gs2Json.time(it.jsonObject.getValue("at")) }
        val lastMerge = input.getValue("lastMergeFrom").jsonObject.entries.associate { UUID.fromString(it.key) to Gs2Json.time(it.value) }
        val removed = input.getValue("removed").jsonArray.map(Gs2Json::id).toSet()
        val actual = GoalHistoryRules.toRead(
            listing, lastRead, lastMerge, removed, Gs2Json.id(input.getValue("self")), input.getValue("newToGroup").jsonPrimitive.boolean,
            Gs2Json.time(input.getValue("now")),
        )
        return Gs2Json.same(case.expected, JsonArray(actual.map { Gs2Json.keyJson(it.key) }))
    }

    fun rollUp(case: FixtureCase): String? {
        val input = case.input
        val existing = Gs2Json.nullable(input["existing"])?.let { Gs2Json.year(it) }
        val months = input.getValue("months").jsonArray.map { Gs2Json.month(it) }
        val actual = GoalHistoryRules.rollUp(
            existing, months, input.getValue("year").jsonPrimitive.int, Gs2Json.id(input.getValue("rolledBy")),
            input.getValue("listing").jsonArray.map(Gs2Json::listing), Gs2Json.time(input.getValue("now")),
        )
        if (case.expected is JsonNull) return if (actual == null) null else "rolled a year, expected none"
        if (actual == null) return "rolled nothing, expected a year"
        return Gs2Json.same(case.expected, GoalHistoryJson.writeYear(actual)) ?: Gs2Json.same(case.expected, GoalHistoryJson.writeYear(Gs2Json.year(case.expected))).tag("PC -> phone -> PC")
    }

    fun mayRemove(case: FixtureCase): String? {
        val input = case.input
        val year = Gs2Json.nullable(input["year"])?.let { Gs2Json.year(it) }
        val actual = GoalHistoryRules.mayRemoveMonth(
            Gs2Json.key(input.getValue("key")), Gs2Json.time(input.getValue("writtenAt")), year,
            input.getValue("devices").jsonArray.map(::device), Gs2Json.time(input.getValue("now")),
        )
        return if (actual == case.expected.jsonPrimitive.boolean) null else "may remove $actual vs ${case.expected}"
    }

    fun roller(case: FixtureCase): String? {
        val devices = case.input.getValue("value").jsonArray.map(::device)
        val actual = GoalHistoryRules.roller(devices)?.toString()
        val expected = (case.expected as? JsonPrimitive)?.takeIf { it !is JsonNull }?.content
        return if (actual == expected) null else "roller $actual vs $expected"
    }

    fun active(case: FixtureCase): String? {
        val input = case.input
        val group = input.getValue("group").jsonArray.map { item ->
            val obj = item.jsonObject
            GoalGroupDevice(
                Gs2Json.id(obj.getValue("id")), obj.getValue("isPc").jsonPrimitive.boolean, obj.getValue("removed").jsonPrimitive.boolean,
                Gs2Json.nullable(obj["lastRegularMerge"])?.let(Gs2Json::time), Gs2Json.yearsMerged(obj["yearsMerged"]),
            )
        }
        val actual = GoalHistoryRules.activeDevices(group, Gs2Json.id(input.getValue("self")), Gs2Json.time(input.getValue("now")))
        return Gs2Json.same(case.expected, JsonArray(actual.map(::deviceJson)))
    }

    /** The regular file and every history file in every order, one call at a time, then the next sync reading the rest; and all at once. */
    fun converge(case: FixtureCase): String? {
        val input = case.input
        val held = Gs2Json.lists(input.getValue("held"))
        val regular = Gs2Json.lists(input.getValue("regular"))
        val now = Gs2Json.time(input.getValue("now"))
        val months = input.getValue("months").jsonArray.map(::historyRows)
        val year = historyRows(input.getValue("year"))
        val inputs: List<Pair<GoalLists?, GoalHistoryRows?>> = listOf<Pair<GoalLists?, GoalHistoryRows?>>(regular to null) +
            months.map { null to it } + listOf(null to year)
        var orders = 0
        for (order in permutations(inputs.indices.toList())) {
            val (lists, _) = convergeOne(held, now, order.map { inputs[it] })
            Gs2Json.same(case.expected, Gs2Json.parts(lists), case.wire())?.let { return "order $order: $it" }
            orders++
        }

        Gs2Json.same(case.expected, Gs2Json.parts(GoalHistoryRules.merge(held, regular, months + year, now).lists), case.wire())?.let { return "all at once: $it" }
        // Merging an input again changes nothing.
        val expectedLists = Gs2Json.lists(case.expected)
        Gs2Json.same(case.expected, Gs2Json.parts(GoalHistoryRules.merge(expectedLists, regular, months + year, now).lists), case.wire())?.let { return "again: $it" }
        return if (orders == 120) null else "orders $orders"
    }

    private fun convergeOne(held: GoalLists, now: OffsetDateTime, inputs: List<Pair<GoalLists?, GoalHistoryRows?>>): Pair<GoalLists, List<GoalHistoryListing>> {
        var state = held
        val unread = ArrayList<GoalHistoryRows>()
        for ((file, rows) in inputs) {
            val merged = GoalHistoryRules.merge(state, file, if (rows == null) emptyList() else listOf(rows), now)
            state = merged.lists
            if (rows != null && rows.file !in merged.read) unread.add(rows)
        }

        if (unread.isEmpty()) return state to emptyList()
        val again = GoalHistoryRules.merge(state, null, unread, now)
        return again.lists to unread.map { it.file }.filter { it !in again.read }
    }

    fun historyBeforeGoal(case: FixtureCase): String? {
        val input = case.input
        val held = Gs2Json.lists(input.getValue("held"))
        val regular = Gs2Json.lists(input.getValue("regular"))
        val now = Gs2Json.time(input.getValue("now"))
        val rows = GoalHistoryRows(Gs2Json.listing(input.getValue("file")), Gs2Json.rows(case.text("rows")) ?: return "rows")
        val rowsFirst = GoalHistoryRules.merge(held, null, listOf(rows), now)
        val goalsAfter = GoalHistoryRules.merge(rowsFirst.lists, regular, emptyList(), now)
        val reread = GoalHistoryRules.merge(goalsAfter.lists, null, listOf(rows), now)
        val goalsFirst = GoalHistoryRules.merge(GoalHistoryRules.merge(held, regular, emptyList(), now).lists, null, listOf(rows), now)
        val expected = case.expected.jsonObject
        fun mergedJson(merged: GoalHistoryMerged, withDropped: Boolean, withGoalsPart: Boolean, withLists: Boolean): JsonObject = buildJsonObject {
            put("read", JsonArray(merged.read.map(::listingJson)))
            if (withDropped) put("dropped", Gs2Json.rowsText(GoalLists(entries = merged.dropped)))
            if (withGoalsPart) put("goalsPartMerged", merged.goalsPartMerged)
            if (withLists) put("lists", Gs2Json.parts(merged.lists))
        }
        return Gs2Json.same(expected.getValue("rowsFirst"), mergedJson(rowsFirst, true, true, false)).tag("rowsFirst")
            ?: Gs2Json.same(expected.getValue("thenRegular").jsonObject, buildJsonObject { put("goalsPartMerged", goalsAfter.goalsPartMerged) }).tag("thenRegular")
            ?: Gs2Json.same(expected.getValue("reread"), mergedJson(reread, true, false, true)).tag("reread")
            ?: Gs2Json.same(expected.getValue("regularFirst"), mergedJson(goalsFirst, false, false, true)).tag("regularFirst")
    }

    fun newerDerivedRow(case: FixtureCase): String? {
        val input = case.input
        val v1 = Gs2Json.rows(case.text("v1")) ?: return "v1"
        val v2 = Gs2Json.rows(case.text("v2")) ?: return "v2"
        val now = Gs2Json.time(input.getValue("now"))
        val lists = Gs2Json.lists(input.getValue("lists"))
        val expected = case.expected.jsonObject
        val id = GoalRawItem.tryRead(v1.newer.entries[0].json)?.id?.toString() ?: return "id did not read"
        GoalCaseRunner.sameText(expected.getValue("id").jsonPrimitive.content, id)?.let { return "id: $it" }
        GoalCaseRunner.sameText(expected.getValue("recentV1").jsonPrimitive.content, Gs2Json.rowsText(GoalSyncWindow.recent(v1, now)))?.let { return "recent: $it" }
        val months = GoalHistoryRules.monthsOf(v1)
        val monthsJson = JsonArray(months.map { buildJsonObject { put("year", it.first); put("month", it.second) } })
        Gs2Json.same(expected.getValue("monthsOfV1"), monthsJson)?.let { return "monthsOf: $it" }
        val month = months[0]
        GoalCaseRunner.sameText(expected.getValue("monthContentV1").jsonPrimitive.content, Gs2Json.rowsText(GoalHistoryRules.monthContent(v1, month.first, month.second)))?.let { return "monthContent: $it" }
        GoalCaseRunner.sameText(expected.getValue("mergeEntries").jsonPrimitive.content, Gs2Json.rowsText(GoalHistoryRules.mergeEntries(listOf(v1, v2), now)))?.let { return "mergeEntries: $it" }
        GoalCaseRunner.sameText(expected.getValue("mergeEntries").jsonPrimitive.content, Gs2Json.rowsText(GoalHistoryRules.mergeEntries(listOf(v2, v1), now)))?.let { return "mergeEntries reversed: $it" }
        val merged = GoalSyncMerge.merge(lists.copy(entries = v1.entries, newer = lists.newer.copy(entries = v1.newer.entries)), lists.copy(entries = v2.entries, newer = lists.newer.copy(entries = v2.newer.entries)), now)
        return Gs2Json.same(expected.getValue("merge"), Gs2Json.parts(merged)).tag("merge")
    }

    // ---------------------------------------------------------------- helpers

    // The PC's parts read as a sync file opens (read, cleaned) and written again: the same bytes. Also through the file-level reader.
    private fun roundTrip(expected: JsonElement, wire: String?): String? {
        val parts = SyncGoalParts.cleanOpen(SyncGoalParts.read(expected.jsonObject)) ?: return "no parts read"
        return Gs2Json.same(expected, JsonObject(SyncGoalParts.write(parts).toMap()), wire)
    }

    private fun historyRows(json: JsonElement): GoalHistoryRows {
        val obj = json.jsonObject
        return GoalHistoryRows(Gs2Json.listing(obj.getValue("file")), Gs2Json.rows(obj.getValue("rows").jsonPrimitive.content) ?: error("rows"))
    }

    private fun device(json: JsonElement): GoalHistoryDevice {
        val obj = json.jsonObject
        return GoalHistoryDevice(Gs2Json.id(obj.getValue("id")), obj.getValue("isPc").jsonPrimitive.boolean, Gs2Json.yearsMerged(obj["yearsMerged"]))
    }

    private fun deviceJson(device: GoalHistoryDevice): JsonObject = buildJsonObject {
        put("id", device.id.toString())
        put("isPc", device.isPc)
        put("yearsMerged", Gs2Json.yearsMergedJson(device.yearsMerged))
    }

    private fun listingJson(listing: GoalHistoryListing): JsonObject = buildJsonObject {
        put("key", Gs2Json.keyJson(listing.key))
        put("modifiedAt", GoalJson.time(listing.modifiedAt))
    }

    private fun targets(json: JsonElement): GoalDeepWorkTargets {
        val obj = json.jsonObject
        val stamp = obj.getValue("stamp").jsonObject
        return GoalDeepWorkTargets(
            Gs2Json.nullable(obj["weeklyMinutes"])?.jsonPrimitive?.double, obj.getValue("trackTimeOnly").jsonPrimitive.boolean,
            FieldStamp(Gs2Json.time(stamp.getValue("at")), stamp.getValue("by").jsonPrimitive.content),
        )
    }

    private fun targetsJson(targets: GoalDeepWorkTargets): JsonObject = buildJsonObject {
        put("weeklyMinutes", targets.weeklyMinutes?.let { GoalJson.number(it) } ?: JsonNull)
        put("trackTimeOnly", targets.trackTimeOnly)
        put("stamp", GoalStampJson.write(com.accountabilibuddy.shared.goals.GoalFieldStamps(mapOf(com.accountabilibuddy.shared.goals.GoalField.Name to targets.stamp)))
            .getValue("name"))
    }

    // A reading's value: a number, or "NaN" / "Infinity" / "-Infinity" as text.
    private fun number(json: JsonElement): Double {
        val primitive = json.jsonPrimitive
        return if (primitive.isString) {
            when (primitive.content) {
                "NaN" -> Double.NaN
                "Infinity" -> Double.POSITIVE_INFINITY
                "-Infinity" -> Double.NEGATIVE_INFINITY
                else -> error("reading ${primitive.content}")
            }
        } else {
            primitive.double
        }
    }

    private fun <T> permutations(items: List<T>): List<List<T>> {
        if (items.size <= 1) return listOf(items)
        return items.flatMap { head -> permutations(items - head).map { listOf(head) + it } }
    }

    private fun String?.tag(label: String): String? = this?.let { "$label: $it" }
}
