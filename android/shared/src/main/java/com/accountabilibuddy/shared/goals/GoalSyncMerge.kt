package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import java.time.OffsetDateTime
import java.util.UUID
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

private typealias Part<T> = GoalSyncJson.Part<T>

/**
 * Merges two devices' goal lists (ADR 79; the PC's `GoalSyncMerge`, rule for rule): pure, symmetric and idempotent.
 * - **Goals** part by part: for each of the ten parts the newer stamp wins (an untouched seed value loses to any other, then the later
 *   time, the device id, the value text, the smaller offset), tombstones and the future clamp as every shared list.
 * - **Areas, entries, reminders** whole item; **reviews** one record per week (decisions per goal, the newest by its own time).
 * - A newer app's items merge by id and stamps ([GoalRawItem]); unreadable ones are carried along.
 * - Deep work is kept whole (kind, placeholder target, its one source, never paused, built in); the first run's untouched Work and Deep
 *   work never win; a live goal in an area that is not live goes to Work, stamped [GoalAreaTidy.STAMP_ID].
 * Entry tombstones are kept for good. Nothing here reads a clock: [now] is the receiver's.
 */
object GoalSyncMerge {
    private val EMPTY_EXTRA = JsonObject(emptyMap())

    fun merge(first: GoalLists, second: GoalLists, now: OffsetDateTime): GoalLists = mergeReported(first, second, now).lists

    fun mergeReported(firstGiven: GoalLists, secondGiven: GoalLists, now: OffsetDateTime): GoalSyncMergeResult {
        val limit = now.plus(GoalListMerge.FUTURE_LIMIT)
        val first = GoalSyncClean.lists(firstGiven)
        val second = GoalSyncClean.lists(secondGiven)
        val areaMerge = mergeAreas(
            Part(first.areas, first.newer.areas), Part(second.areas, second.newer.areas), now, limit,
        )
        val goalMerge = mergeGoals(Part(first.goals, first.newer.goals), Part(second.goals, second.newer.goals), now, limit)
        var areas = emptySeedWorkDeleted(areaMerge.first, goalMerge.first, first.areas + second.areas, now, limit)
        var goals = goalMerge.first
        val rehomed = rehomed(areas, goals)
        areas = rehomed.first
        goals = rehomed.second
        val goalIds = goals.map { it.id }.toHashSet()
        val entryMerge = mergeWhole(
            Part(first.entries, first.newer.entries), Part(second.entries, second.newer.entries), now, limit,
            GoalTombstones::clean, GoalUnknowns::keepEntry, keepTombstones = true,
        )
        val reminderMerge = mergeWhole(
            Part(first.reminders, first.newer.reminders), Part(second.reminders, second.newer.reminders), now, limit,
            GoalTombstones::clean, GoalUnknowns::keepReminder,
        )
        val dropped = entryMerge.first.filterIsInstance<GoalEntry>().filter { it.goalId !in goalIds }.sortedBy { it.id.toString() }
        val entries = entryMerge.first.filter { it !is GoalEntry || it.goalId in goalIds }
        val reminders = reminderMerge.first.filter { it !is GoalReminder || it.goalId in goalIds }
        val reviewMerge = mergeReviews(first.reviews, second.reviews, first.newer.reviews, second.newer.reviews, now, limit)
        val areasSplit = split<GoalArea>(areas, areaMerge.second)
        val goalsSplit = split<Goal>(goals, goalMerge.second)
        val entriesSplit = split<GoalEntry>(entries, entryMerge.second)
        val remindersSplit = split<GoalReminder>(reminders, reminderMerge.second)
        val reviewsSplit = split<GoalReviewRecord>(reviewMerge.first, reviewMerge.second)
        val lists = GoalLists(
            areasSplit.known, goalsSplit.known, entriesSplit.known, remindersSplit.known, reviewsSplit.known,
            GoalNewerLists(areasSplit.newer, goalsSplit.newer, entriesSplit.newer, remindersSplit.newer, reviewsSplit.newer),
        )
        return GoalSyncMergeResult(lists, dropped)
    }

    /** Two copies of one goal merged part by part. */
    fun mergeGoal(one: Goal, other: Goal): Goal {
        require(one.id == other.id) { "Only copies of the same goal merge." }
        val stampsA = GoalFieldStamps.forGoal(one)
        val stampsB = GoalFieldStamps.forGoal(other)
        val valuesA = GoalFieldValues.all(one)
        val valuesB = GoalFieldValues.all(other)
        var result = one.copy(
            createdAt = earlier(one.createdAt, other.createdAt),
            editedAt = later(one.editedAt, other.editedAt),
            builtIn = one.builtIn || other.builtIn,
            extra = extraOf(one.extra, other.extra, one, other),
        )
        var stamps = GoalFieldStamps.NONE
        for (field in GoalField.values()) {
            val stampA = stampsA.of(field)!!
            val stampB = stampsB.of(field)!!
            val firstWins = firstIsNewer(
                GoalSeeds.isPristine(one, field, stampA), stampA, valuesA.getValue(field),
                GoalSeeds.isPristine(other, field, stampB), stampB, valuesB.getValue(field),
            )
            result = GoalFieldValues.with(result, if (firstWins) one else other, field)
            stamps = stamps.with(field, if (firstWins) stampA else stampB)
        }

        // Unknown stamps of a newer app's parts: the newer copy's list when it has one, else the other's.
        val stampsExtra = extraOf(one.stamps?.extra ?: EMPTY_EXTRA, other.stamps?.extra ?: EMPTY_EXTRA, one, other)
        result = result.copy(stamps = stamps.copy(extra = stampsExtra))
        // Nested unknown fields (target, rules, pauses, tiles) and a goal's newer rules: kept from the other copy where the winning part has none.
        return GoalUnknowns.keepGoal(other, GoalUnknowns.keepGoal(one, result))
    }

    /** Whether an entry shows: live, and its goal is live. */
    fun isShown(entry: GoalEntry, liveGoalIds: Set<UUID>): Boolean = entry.isLive && entry.goalId in liveGoalIds

    /** Whether a reminder shows: live, and its goal is live. */
    fun isShown(reminder: GoalReminder, liveGoalIds: Set<UUID>): Boolean = reminder.isLive && reminder.goalId in liveGoalIds

    /** The goal without a waiting change (it is never stamped or sent). */
    fun withoutPending(goal: Goal): Goal {
        var result = goal
        val target = goal.target
        if (target.pendingChange != null) result = result.copy(target = target.copy(pendingChange = null))
        val second = goal.secondTarget
        if (second?.pendingChange != null) result = result.copy(secondTarget = second.copy(pendingChange = null))
        return result
    }

    // ---------------------------------------------------------------- goals

    private fun mergeGoals(first: Part<Goal>, second: Part<Goal>, now: OffsetDateTime, limit: OffsetDateTime): Pair<List<GoalMergeItem>, List<JsonElement>> {
        val firstById = foldedGoals(first.known.filter(::isNotDeepWorkTombstone).map { prepared(it, limit) }, now)
        val secondById = foldedGoals(second.known.filter(::isNotDeepWorkTombstone).map { prepared(it, limit) }, now)
        for (id in firstById.keys.filter { it in secondById }) {
            val one = firstById.getValue(id)
            val other = secondById.getValue(id)
            if (one.isLive && other.isLive) {
                val merged = mergeGoal(one, other)
                firstById[id] = merged
                secondById[id] = merged
            }
        }

        val (rawFirst, keptFirst) = raw(first.newer)
        val (rawSecond, keptSecond) = raw(second.newer)
        val winners = GoalListMerge.merge(firstById.values.toList() + rawFirst, secondById.values.toList() + rawSecond, now)
        val kept = winners.map { if (it is Goal) whole(it) else it }
        return kept to passthrough(keptFirst, keptSecond)
    }

    private fun isNotDeepWorkTombstone(goal: Goal): Boolean = goal.isLive || goal.id != Goal.DEEP_WORK_ID

    // As a goal takes part: no waiting change, a tombstone cleaned, a live one with every part stamped, no stamp past the limit.
    private fun prepared(goal: Goal, limit: OffsetDateTime): Goal {
        var prepared = GoalTombstones.clean(withoutPending(goal))
        if (prepared.isLive) {
            var stamps = GoalFieldStamps.forGoal(prepared)
            for (field in GoalField.values()) {
                val stamp = stamps.of(field)
                if (stamp != null && DotNetTime.compare(stamp.at, limit) > 0) stamps = stamps.with(field, stamp.copy(at = limit))
            }

            prepared = prepared.copy(stamps = stamps)
        }

        return prepared.copy(editedAt = min(prepared.editedAt, limit), deletedAt = prepared.deletedAt?.let { min(it, limit) })
    }

    // Deep work kept whole whatever a copy said; no other goal built in.
    private fun whole(goal: Goal): Goal {
        if (!goal.isLive) return if (goal.builtIn) goal.copy(builtIn = false) else goal
        if (goal.id == Goal.DEEP_WORK_ID) {
            val seed = GoalSeeds.deepWork(goal.createdAt)
            return goal.copy(kind = seed.kind, target = seed.target, sources = seed.sources, newerSources = emptyList(), pauses = null, builtIn = true)
        }

        return if (goal.builtIn) goal.copy(builtIn = false) else goal
    }

    private fun foldedGoals(goals: List<Goal>, now: OffsetDateTime): LinkedHashMap<UUID, Goal> {
        val byId = LinkedHashMap<UUID, Goal>()
        for (goal in goals) {
            val held = byId[goal.id]
            byId[goal.id] = when {
                held == null -> goal
                held.isLive && goal.isLive -> mergeGoal(held, goal)
                else -> GoalListMerge.merge(listOf(held), listOf(goal), now, keepTombstones = true).single() as Goal
            }
        }

        return byId
    }

    // The newer part: an untouched seed value loses to any other, then the later time, the device id, the value, then (the same instant
    // written with another offset) the smaller offset, so the result's text is the same whichever copy came first.
    private fun firstIsNewer(pristineA: Boolean, stampA: FieldStamp, valueA: String, pristineB: Boolean, stampB: FieldStamp, valueB: String): Boolean {
        if (pristineA != pristineB) return pristineB
        val byTime = DotNetTime.compare(stampA.at, stampB.at)
        if (byTime != 0) return byTime > 0
        val byDevice = stampA.by.compareTo(stampB.by)
        if (byDevice != 0) return byDevice > 0
        val byValue = valueA.compareTo(valueB)
        if (byValue != 0) return byValue > 0
        return stampA.at.offset.totalSeconds <= stampB.at.offset.totalSeconds
    }

    // The earlier instant; the same instant with two offsets: the smaller offset.
    private fun earlier(a: OffsetDateTime, b: OffsetDateTime): OffsetDateTime {
        val byTime = DotNetTime.compare(a, b)
        return if (byTime < 0 || (byTime == 0 && a.offset.totalSeconds <= b.offset.totalSeconds)) a else b
    }

    // The later instant; the same instant with two offsets: the smaller offset.
    private fun later(a: OffsetDateTime, b: OffsetDateTime): OffsetDateTime {
        val byTime = DotNetTime.compare(a, b)
        return if (byTime > 0 || (byTime == 0 && a.offset.totalSeconds <= b.offset.totalSeconds)) a else b
    }

    // Unknown fields: the newer copy's list when it has one (a newer writer that dropped a field wins), else the other's.
    private fun extraOf(extraA: JsonObject, extraB: JsonObject, one: Goal, other: Goal): JsonObject {
        if (extraA.isEmpty() || extraB.isEmpty()) return if (extraA.isNotEmpty()) extraA else extraB
        val byTime = DotNetTime.compare(one.editedAt, other.editedAt)
        if (byTime != 0) return if (byTime > 0) extraA else extraB
        return if (extraText(extraA).compareTo(extraText(extraB)) >= 0) extraA else extraB
    }

    private fun extraText(extra: JsonObject): String = extra.entries.sortedBy { it.key }.joinToString("\n") { it.key + "=" + GoalJson.text(it.value) }

    // ---------------------------------------------------------------- areas and the tidy

    private fun mergeAreas(first: Part<GoalArea>, second: Part<GoalArea>, now: OffsetDateTime, limit: OffsetDateTime): Pair<List<GoalMergeItem>, List<JsonElement>> {
        // The first run's untouched "Work" never wins over an edited live copy of it, whatever its stamp.
        val workEdited = (first.known + second.known).any { it.id == GoalSeeds.WORK_AREA_ID && it.isLive && !GoalSeeds.isPristine(it) }
        fun kept(areas: Part<GoalArea>): Part<GoalArea> = if (workEdited) Part(areas.known.filter { !GoalSeeds.isPristine(it) }, areas.newer) else areas
        return mergeWhole(kept(first), kept(second), now, limit, GoalTombstones::clean, GoalUnknowns::keepArea)
    }

    // Q592: the first run's untouched "Work" that holds no live goal after the goal merge loses to a "Work" tombstone of either input
    // (within the tombstones' 30 days), whatever the stamps.
    private fun emptySeedWorkDeleted(
        areas: List<GoalMergeItem>, goals: List<GoalMergeItem>, inputs: List<GoalArea>, now: OffsetDateTime, limit: OffsetDateTime,
    ): List<GoalMergeItem> {
        val index = areas.indexOfFirst { it is GoalArea && it.id == GoalSeeds.WORK_AREA_ID }
        if (index < 0) return areas
        val work = areas[index] as GoalArea
        if (!GoalSeeds.isPristine(work) || goals.any { it.deletedAt == null && areaOf(it) == GoalSeeds.WORK_AREA_ID }) return areas
        val keptFrom = now.minus(GoalListMerge.TOMBSTONES_KEPT)
        val tombstone = inputs
            .filter { it.id == GoalSeeds.WORK_AREA_ID && !it.isLive }
            .map { GoalListMerge.clamped(GoalTombstones.clean(it), limit) as GoalArea }
            .filter { DotNetTime.compare(it.deletedAt!!, keptFrom) > 0 }
            .sortedWith(
                compareByDescending<GoalArea> { DotNetTime.ticks(GoalListMerge.stampOf(it)) }.thenComparator { a, b -> b.tieBreak.compareTo(a.tieBreak) },
            )
            .firstOrNull() ?: return areas
        val replaced = areas.toMutableList()
        replaced[index] = tombstone
        return ordered(replaced)
    }

    // The area a goal (or a newer app's goal) is in; null when it can't be read.
    private fun areaOf(item: GoalMergeItem): UUID? = when (item) {
        is Goal -> item.areaId
        is GoalRawItem -> ((item.json["areaId"] as? JsonPrimitive)?.takeIf { it.isString }?.content)?.let { text ->
            if (GUID_FORM.matches(text)) UUID.fromString(text) else null
        }
        else -> null
    }

    private val GUID_FORM = Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")

    // A live goal in an area that isn't live goes to "Work" (GoalAreaTidy), its area part stamped as the move by GoalAreaTidy.STAMP_ID.
    private fun rehomed(areas: List<GoalMergeItem>, goals: List<GoalMergeItem>): Pair<List<GoalMergeItem>, List<GoalMergeItem>> {
        val knownAreas = areas.filterIsInstance<GoalArea>()
        // A newer app's live area still holds its goals: a stand-in tells the tidy it is live.
        val standIns = areas.filterIsInstance<GoalRawItem>().filter { it.deletedAt == null }
            .map { GoalArea(it.id, "", "", "", it.position, it.editedAt) }
        val knownGoals = goals.filterIsInstance<Goal>()
        val (tidiedAreas, tidiedGoals) = GoalAreaTidy.rehome(knownAreas + standIns, knownGoals, GoalSeeds.workArea(DotNetTime.MIN))
        val unchanged = tidiedGoals === knownGoals || (tidiedGoals.size == knownGoals.size && tidiedGoals.indices.all { tidiedGoals[it] === knownGoals[it] })
        if (unchanged) return areas to goals
        val movedGoals = ArrayList<GoalMergeItem>(goals.size)
        var index = 0
        for (item in goals) {
            if (item is Goal) {
                val before = knownGoals[index]
                val after = tidiedGoals[index]
                index++
                movedGoals.add(
                    if (after.areaId == before.areaId) {
                        after
                    } else {
                        after.copy(stamps = GoalFieldStamps.forGoal(after).with(GoalField.Area, FieldStamp(after.editedAt, GoalAreaTidy.STAMP_ID)))
                    },
                )
            } else {
                movedGoals.add(item)
            }
        }

        val newAreas = tidiedAreas.filter { area -> standIns.none { it === area } } + areas.filterIsInstance<GoalRawItem>()
        return ordered(newAreas) to movedGoals
    }

    // ---------------------------------------------------------------- whole items

    private fun <T : GoalMergeItem> mergeWhole(
        first: Part<T>, second: Part<T>, now: OffsetDateTime, limit: OffsetDateTime,
        clean: (T) -> T, keep: (T, T) -> T, keepTombstones: Boolean = false,
    ): Pair<List<GoalMergeItem>, List<JsonElement>> {
        @Suppress("UNCHECKED_CAST")
        fun clampedClean(item: T): T = GoalListMerge.clamped(clean(item), limit) as T
        val firstKnown = first.known.map(::clampedClean)
        val secondKnown = second.known.map(::clampedClean)
        val (rawFirst, keptFirst) = raw(first.newer)
        val (rawSecond, keptSecond) = raw(second.newer)
        val merged = GoalListMerge.merge(firstKnown + rawFirst, secondKnown + rawSecond, now, keepTombstones)
        // Missing unknown fields are kept from another copy of the same item (newest first) only where the winner has none at all.
        val copies = (firstKnown + secondKnown).groupBy { it.id }
        val kept = merged.map { item ->
            @Suppress("UNCHECKED_CAST")
            val winner = item as? T
            if (winner == null || item is GoalRawItem || winner.deletedAt != null) {
                item
            } else {
                var result: T = winner
                val others = (copies[winner.id] ?: emptyList()).filter { it !== winner }
                    .sortedWith(compareByDescending<T> { DotNetTime.ticks(GoalListMerge.stampOf(it)) }.thenComparator { a, b -> b.tieBreak.compareTo(a.tieBreak) })
                for (copy in others) result = keep(copy, result)
                result
            }
        }

        return kept to passthrough(keptFirst, keptSecond)
    }

    // ---------------------------------------------------------------- reviews

    private fun mergeReviews(
        first: List<GoalReviewRecord>, second: List<GoalReviewRecord>, newerFirst: List<GoalNewerItem>, newerSecond: List<GoalNewerItem>,
        now: OffsetDateTime, limit: OffsetDateTime,
    ): Pair<List<GoalMergeItem>, List<JsonElement>> {
        val all = (first + second)
            .map { GoalListMerge.clamped(GoalTombstones.clean(it), limit) as GoalReviewRecord }
            .map { if (it.id == GoalReviewIds.forWeek(it.week)) it else it.copy(id = GoalReviewIds.forWeek(it.week)) }
        val perWeek = ArrayList<GoalMergeItem>()
        for (week in all.groupBy { it.week }.toSortedMap().values) {
            val candidates = ArrayList<GoalMergeItem>()
            val live = week.filter { it.isLive }
            if (live.isNotEmpty()) candidates.add(folded(live, limit))
            val tombstone = week.filter { !it.isLive }
                .sortedWith(compareByDescending<GoalReviewRecord> { DotNetTime.ticks(GoalListMerge.stampOf(it)) }.thenComparator { a, b -> b.tieBreak.compareTo(a.tieBreak) })
                .firstOrNull()
            if (tombstone != null) candidates.add(tombstone)
            perWeek.addAll(GoalListMerge.merge(candidates, emptyList(), now))
        }

        val (rawFirst, keptFirst) = raw(newerFirst)
        val (rawSecond, keptSecond) = raw(newerSecond)
        val merged = GoalListMerge.merge(perWeek + rawFirst, rawSecond, now)
        return merged to passthrough(keptFirst, keptSecond)
    }

    private sealed interface Decision {
        data class Known(val value: GoalReviewDecision) : Decision
        data class Raw(val json: JsonObject) : Decision
    }

    // Live records of one week as one: the base fields of the newest; each goal's decision the newest by its own time (decidedAt, else its
    // record's edit time; clamped; its JSON breaking a tie), written with that time; decisions by goal id, then unkeyed ones by their text.
    private fun folded(live: List<GoalReviewRecord>, limit: OffsetDateTime): GoalReviewRecord {
        val newestFirst = live.sortedWith(
            compareByDescending<GoalReviewRecord> { DotNetTime.ticks(it.editedAt) }
                .thenComparator { a, b -> baseKey(b).compareTo(baseKey(a)) }
                .thenComparator { a, b -> b.tieBreak.compareTo(a.tieBreak) },
        )
        val newest = newestFirst[0]
        class Best(val at: OffsetDateTime, val json: String, val decision: Decision)
        val best = HashMap<String, Best>()
        val unkeyed = java.util.TreeMap<String, JsonObject>()
        for (review in newestFirst) {
            for (written in decisionsInOrder(review)) {
                val key = decisionKey(written)
                if (key == null) {
                    unkeyed.putIfAbsent(decisionJson(written), (written as Decision.Raw).json)
                    continue
                }

                val (at, decision) = timed(written, review.editedAt, limit)
                val json = decisionJson(decision)
                val held = best[key]
                if (held == null || DotNetTime.compare(at, held.at) > 0 || (DotNetTime.compare(at, held.at) == 0 && json.compareTo(held.json) > 0)) {
                    best[key] = Best(at, json, decision)
                }
            }
        }

        val known = ArrayList<GoalReviewDecision>()
        val newer = ArrayList<GoalNewerItem>()
        var index = 0
        val ordered: List<Decision> = best.entries.sortedBy { it.key }.map { it.value.decision } + unkeyed.values.map { Decision.Raw(it) }
        for (decision in ordered) {
            if (decision is Decision.Known) known.add(decision.value) else newer.add(GoalNewerItem(index, (decision as Decision.Raw).json))
            index++
        }

        val extra = newestFirst.map { it.extra }.firstOrNull { it.isNotEmpty() } ?: EMPTY_EXTRA
        return newest.copy(
            id = GoalReviewIds.forWeek(newest.week),
            decisions = known,
            newerDecisions = newer,
            extra = if (newest.extra.isNotEmpty()) newest.extra else extra,
        )
    }

    // A decision with its own time: its decidedAt (a newer app's kept decision: its "decidedAt" when readable), else its record's edit time;
    // past the future limit, the limit. Returned with that time written in.
    private fun timed(decision: Decision, recordAt: OffsetDateTime, limit: OffsetDateTime): Pair<OffsetDateTime, Decision> {
        if (decision is Decision.Known) {
            val known = decision.value
            val at = min(known.decidedAt ?: recordAt, limit)
            return at to (if (known.decidedAt != null && known.decidedAt == at) decision else Decision.Known(known.copy(decidedAt = at)))
        }

        val json = (decision as Decision.Raw).json
        val written = (json["decidedAt"] as? JsonPrimitive)?.takeIf { it.isString }?.content?.let {
            try {
                OffsetDateTime.parse(it)
            } catch (e: java.time.format.DateTimeParseException) {
                null
            }
        }
        val time = min(written ?: recordAt, limit)
        if (written != null && written == time) return time to decision
        val fields = LinkedHashMap(json)
        // As the PC: the item is read back as JSON after this, so its time is written as any other text of it (a "+" escaped).
        fields["decidedAt"] = JsonPrimitive(GoalJson.time(time))
        return time to Decision.Raw(JsonObject(fields))
    }

    // The fields a folded record takes from its newest record, as one text: equal texts, the same fields.
    private fun baseKey(review: GoalReviewRecord): String =
        GoalJson.text(GoalJson.writeReview(review.copy(id = UUID(0L, 0L), decisions = emptyList(), newerDecisions = emptyList(), editedAt = DotNetTime.MIN)))

    // A review's decisions, known ones and ones kept as written, in their written order.
    private fun decisionsInOrder(review: GoalReviewRecord): List<Decision> {
        val newer = review.newerDecisions.sortedBy { it.index }
        val result = ArrayList<Decision>()
        var nextNewer = 0
        var written = 0
        for (decision in review.decisions) {
            while (nextNewer < newer.size && newer[nextNewer].index <= written) {
                result.add(Decision.Raw(newer[nextNewer++].json))
                written++
            }

            result.add(Decision.Known(decision))
            written++
        }

        while (nextNewer < newer.size) result.add(Decision.Raw(newer[nextNewer++].json))
        return result
    }

    private fun decisionKey(decision: Decision): String? = when (decision) {
        is Decision.Known -> decision.value.goalId.toString()
        is Decision.Raw -> (decision.json["goalId"] as? JsonPrimitive)?.takeIf { it.isString }?.content
            ?.takeIf { GUID_FORM.matches(it) }?.let { UUID.fromString(it).toString() }
    }

    private fun decisionJson(decision: Decision): String = when (decision) {
        is Decision.Known -> GoalJson.text(GoalJson.writeReviewDecision(decision.value))
        is Decision.Raw -> GoalJson.text(decision.json)
    }

    // ---------------------------------------------------------------- shared helpers

    // A list's newer items: the ones whose id and stamps read take part; the others are carried along as written.
    private fun raw(newer: List<GoalNewerItem>): Pair<List<GoalMergeItem>, List<JsonElement>> {
        val raw = ArrayList<GoalMergeItem>()
        val kept = ArrayList<JsonElement>()
        for (item in newer) {
            val readable = GoalRawItem.tryRead(item.json)
            if (readable != null) raw.add(readable) else kept.add(item.json)
        }

        return raw to kept
    }

    // Items carried along as written from both lists, once each, in text order (the same whichever list came first).
    private fun passthrough(first: List<JsonElement>, second: List<JsonElement>): List<JsonElement> =
        (first + second).groupBy { GoalJson.text(it) }.entries.sortedBy { it.key }.map { it.value.first() }

    class Split<T>(val known: List<T>, val newer: List<GoalNewerItem>)

    // The known items as the list, the others carried at their places after them in the merged order.
    @Suppress("UNCHECKED_CAST")
    private inline fun <reified T> split(merged: List<GoalMergeItem>, kept: List<JsonElement>): Split<T> {
        val known = ArrayList<T>()
        val newer = ArrayList<GoalNewerItem>()
        var index = 0
        for (item in merged) {
            if (item is GoalRawItem) newer.add(GoalNewerItem(index, item.json)) else if (item is T) known.add(item)
            index++
        }

        for (json in kept) newer.add(GoalNewerItem(index++, json as JsonObject))
        return Split(known, newer)
    }

    private fun ordered(items: List<GoalMergeItem>): List<GoalMergeItem> = GoalListMerge.orderedByPosition(items)

    private fun min(value: OffsetDateTime, limit: OffsetDateTime): OffsetDateTime = if (DotNetTime.compare(value, limit) > 0) limit else value
}
