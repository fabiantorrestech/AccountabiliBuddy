package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.FieldStamps
import com.accountabilibuddy.shared.sync.SharedListMerge
import kotlinx.serialization.json.JsonObject
import java.time.LocalDate
import java.time.OffsetDateTime

/**
 * Merges the shared task list (ADR 76) field by field; the PC's twin is Core's TaskFieldMerge and both give the same result. Where
 * both lists hold a live copy of a task, each [TaskField] takes the newer stamp's value, so a change to one part on one device and
 * to another part on another device both survive. The title and the note also look at where each version came from
 * ([FieldStamp.bases]): a version made after seeing the other wins whatever its time; when each was made without seeing the other,
 * the newer one is shown and the other is kept on the task as a [TaskConflict] ("Changed on both devices"). Reminders are one
 * field: the newest stamp's whole list wins (Q80). Everything else (tombstones, a revive, local items, the 30-day rule, the
 * 2-minute future clamp, the order) is [SharedListMerge]'s. The result is the same whichever list comes first.
 */
object TaskFieldMerge {
    /** How many earlier values a title or note stamp remembers. */
    const val MAX_BASES = 10

    /** The merged list, tombstones included, ordered as [SharedListMerge.merge] orders it. */
    fun merge(first: List<SyncTaskItem>, second: List<SyncTaskItem>, now: OffsetDateTime, self: String? = null): List<SyncTaskItem> {
        // R7.1 review: the time cut to the PC's ticks, as every stamp is.
        val now = DotNetTime.cut(now)
        val limit = now.plus(SharedListMerge.FUTURE_LIMIT)
        val firstById = folded(first.map { clamped(it, limit) }, now, self)
        val secondById = folded(second.map { clamped(it, limit) }, now, self)

        for (id in firstById.keys.filter { it in secondById }) {
            val one = firstById.getValue(id)
            val other = secondById.getValue(id)
            if (one.task.isLive && other.task.isLive) {
                val merged = mergeItem(one, other)
                firstById[id] = merged
                secondById[id] = merged
            }
        }

        return SharedListMerge.merge(firstById.values.toList(), secondById.values.toList(), now.toInstant(), self)
    }

    /** Two live copies of the same task merged field by field; the same whichever is [one]. */
    fun mergeItem(one: SyncTaskItem, other: SyncTaskItem): SyncTaskItem {
        require(one.id == other.id) { "Only copies of the same task merge." }

        val a = one.task
        val b = other.task
        val firstIsLatest = firstIsLatest(one, other)
        var result = a.copy(
            createdAt = if (DotNetTime.compare(a.createdAt, b.createdAt) <= 0) a.createdAt else b.createdAt,
            editedAt = if (DotNetTime.compare(a.editedAt, b.editedAt) >= 0) a.editedAt else b.editedAt,
            touchedAt = if (DotNetTime.compare(a.touchedAt, b.touchedAt) >= 0) a.touchedAt else b.touchedAt,
            origin = originOf(a, b),
            autoSavedFor = autoSavedForOf(a.autoSavedFor, b.autoSavedFor),
            effort = null,
            // Not on the PC (it drops unknown fields): the copy edited last keeps its own.
            extra = if (firstIsLatest) a.extra else b.extra,
        )

        var stamps = TaskFieldStamps.NONE.copy(extra = if (firstIsLatest) one.stamps.extra else other.stamps.extra)
        val conflicts = ArrayList<TaskConflict>(a.conflicts.orEmpty() + b.conflicts.orEmpty())
        for (field in TaskFieldValues.ALL) {
            val stampA = one.stamps.of(field) ?: unstamped(a)
            val stampB = other.stamps.of(field) ?: unstamped(b)
            // R7.1 review (performance): a field's text is made only when the stamps' time and device tie; title and note are their text.
            val valueA = { TaskFieldValues.canonical(a, field) }
            val valueB = { TaskFieldValues.canonical(b, field) }

            val pick = when (field) {
                TaskField.TITLE, TaskField.NOTE -> pickText(field, a, b, stampA, stampB, valueA(), valueB())
                TaskField.NUMBER -> pickNumber(a, b, stampA, stampB, valueA, valueB)
                else -> pickNewer(stampA, stampB, valueA, valueB)
            }

            result = TaskFieldValues.with(result, if (pick.fromFirst) a else b, field)
            stamps = stamps.with(field, pick.stamp)
            pick.conflict?.let { conflicts += it }
        }

        result = result.copy(
            formerNumbers = formerNumbersOf(a, b, result.number),
            conflicts = surviving(conflicts, result, stamps),
        )
        return SyncTaskItem(result, stamps, if (firstIsLatest) one.extra else other.extra)
    }

    private class FieldPick(val fromFirst: Boolean, val stamp: FieldStamp, val conflict: TaskConflict? = null)

    private fun pickNewer(stampA: FieldStamp, stampB: FieldStamp, valueA: () -> String, valueB: () -> String): FieldPick {
        val firstWins = FieldStamps.firstIsNewer(stampA, stampB, valueA, valueB)
        return FieldPick(firstWins, if (firstWins) stampA else stampB)
    }

    private fun pickText(field: TaskField, a: TaskItem, b: TaskItem, stampA: FieldStamp, stampB: FieldStamp, valueA: String, valueB: String): FieldPick {
        var firstWins = FieldStamps.firstIsNewer(stampA, stampB, valueA, valueB)
        if (valueA == valueB) {
            // Q584: on a full tie (time, device, value) the newer is the one whose bases, joined, sort later (ordinal), so the
            // union's order no longer depends on which copy came first. Both twins together (the PC lane takes the same rule).
            if (DotNetTime.compare(stampA.at, stampB.at) == 0 && stampA.by == stampB.by) {
                firstWins = joinedBases(stampA) >= joinedBases(stampB)
            }

            // The same value: either history leads to it, so the stamp keeps both.
            val newer = if (firstWins) stampA else stampB
            val older = if (firstWins) stampB else stampA
            return FieldPick(firstWins, newer.copy(bases = unionBases(newer.bases, older.bases)))
        }

        val textA = TaskFieldValues.text(a, field)
        val textB = TaskFieldValues.text(b, field)
        val firstSawSecond = holds(stampA.bases, TaskFieldValues.hash(textB))
        val secondSawFirst = holds(stampB.bases, TaskFieldValues.hash(textA))
        if (firstSawSecond != secondSawFirst) {
            // One version was made from the other: it wins whatever the clocks say.
            return if (firstSawSecond) FieldPick(true, stampA) else FieldPick(false, stampB)
        }

        if (firstSawSecond) {
            // Each holds the other (an edit undone on one device): the newer stamp decides, no conflict.
            return FieldPick(firstWins, if (firstWins) stampA else stampB)
        }

        val winnerStamp = if (firstWins) stampA else stampB
        val loserStamp = if (firstWins) stampB else stampA
        val loserText = if (firstWins) textB else textA
        val fieldName = if (field == TaskField.TITLE) TaskConflict.TITLE else TaskConflict.NOTE
        val conflict = TaskConflict(fieldName, loserText, loserStamp.by, loserStamp.at)

        // The shown value keeps its own stamp but also remembers what the other version was made from (Q173): the task now carries
        // that version as the conflict, so an older copy of it (a third device that synced in between) is already seen, and the
        // user's pick (TaskEdits.resolve, which builds on these bases) beats it instead of raising the conflict again. The other
        // version's own value stays out, or the conflict would close at once.
        val bases = winnerFirstBases(winnerStamp.bases, loserStamp.bases, TaskFieldValues.hash(loserText))
        return FieldPick(firstWins, winnerStamp.copy(bases = bases), conflict)
    }

    // The winner's bases all stay; the loser's (but never the loser's own value) fill what is left of the ring, placed before the
    // winner's (oldest first) so a later edit drops them first. The winner is the same whichever copy came first, so this is too.
    private fun winnerFirstBases(winner: List<String>?, loser: List<String>?, loserValue: String): List<String>? {
        val own = winner.orEmpty().takeLast(MAX_BASES)
        val room = MAX_BASES - own.size
        val borrowed = loser.orEmpty().filter { it != loserValue && it !in own }.distinct().takeLast(room)
        val all = borrowed + own
        return all.ifEmpty { null }
    }

    // A final number beats a temporary one (the numbering device's word is final); otherwise the newer stamp.
    private fun pickNumber(a: TaskItem, b: TaskItem, stampA: FieldStamp, stampB: FieldStamp, valueA: () -> String, valueB: () -> String): FieldPick {
        val rankA = numberRank(a.number)
        val rankB = numberRank(b.number)
        if (rankA != rankB) {
            return if (rankA > rankB) FieldPick(true, stampA) else FieldPick(false, stampB)
        }

        return pickNewer(stampA, stampB, valueA, valueB)
    }

    private fun numberRank(number: TaskNumber?): Int = when {
        number == null -> 0
        number.temporary -> 1
        else -> 2
    }

    // The temporary numbers either copy had, plus a temporary number that lost to another, so "143b" still finds the task.
    private fun formerNumbersOf(a: TaskItem, b: TaskItem, kept: TaskNumber?): List<Int>? {
        val former = sortedSetOf<Int>()
        former += a.formerNumbers.orEmpty()
        former += b.formerNumbers.orEmpty()
        for (number in listOf(a.number, b.number)) {
            if (number != null && number.temporary && number.value != kept?.value) {
                former += number.value
            }
        }

        if (kept != null) {
            former -= kept.value
        }

        return former.toList().ifEmpty { null }
    }

    // A conflict stays while its other version is neither the shown value nor one the shown value was made from (the user's pick
    // is an edit whose bases hold both versions). One per field and other version; the same list whichever copy came first.
    private fun surviving(conflicts: List<TaskConflict>, merged: TaskItem, stamps: TaskFieldStamps): List<TaskConflict>? {
        val kept = conflicts
            .filter { stillOpen(it, merged, stamps) }
            .groupBy { it.field to TaskFieldValues.hash(it.otherValue) }
            .values
            .map { group ->
                group.sortedWith(
                    compareByDescending<TaskConflict> { DotNetTime.ticks(it.otherEditedAt) }.thenByDescending { it.otherDeviceId },
                ).first()
            }
            .sortedWith(compareBy<TaskConflict> { it.field }.thenBy { TaskFieldValues.hash(it.otherValue) })
        return kept.ifEmpty { null }
    }

    private fun stillOpen(conflict: TaskConflict, merged: TaskItem, stamps: TaskFieldStamps): Boolean {
        val field = when (conflict.field) {
            TaskConflict.TITLE -> TaskField.TITLE
            TaskConflict.NOTE -> TaskField.NOTE
            else -> return false
        }

        val otherHash = TaskFieldValues.hash(conflict.otherValue)
        val shownHash = TaskFieldValues.hash(TaskFieldValues.text(merged, field))
        return otherHash != shownHash && !holds(stamps.of(field)?.bases, otherHash)
    }

    private fun holds(bases: List<String>?, hash: String): Boolean = bases != null && hash in bases

    /** Q584: a stamp's bases as one text for the full-tie rule ("," between, none = ""). */
    internal fun joinedBases(stamp: FieldStamp): String = stamp.bases.orEmpty().joinToString(",")

    private fun unionBases(first: List<String>?, second: List<String>?): List<String>? {
        val all = first.orEmpty() + second.orEmpty().filter { !holds(first, it) }
        if (all.isEmpty()) {
            return null
        }

        return all.takeLast(MAX_BASES)
    }

    private fun originOf(a: TaskItem, b: TaskItem): String? {
        if (a.origin == null || b.origin == null) {
            return a.origin ?: b.origin
        }

        return if (a.origin <= b.origin) a.origin else b.origin
    }

    private fun autoSavedForOf(a: LocalDate?, b: LocalDate?): LocalDate? =
        if (a != null && b != null) (if (a <= b) a else b) else a ?: b

    private fun unstamped(task: TaskItem): FieldStamp = FieldStamp(task.createdAt, FieldStamp.NOBODY)

    // Which copy's unknown fields the merged task keeps: the one edited last, then the one whose PC text sorts later, so the
    // choice never depends on the order.
    private fun firstIsLatest(one: SyncTaskItem, other: SyncTaskItem): Boolean {
        val byTime = DotNetTime.compare(one.task.editedAt, other.task.editedAt)
        if (byTime != 0) {
            return byTime > 0
        }

        return unknownText(one) >= unknownText(other)
    }

    private fun unknownText(item: SyncTaskItem): String =
        TaskJson.text(JsonObject(mapOf("task" to item.task.extra, "stamps" to item.stamps.extra, "item" to item.extra)))

    private fun folded(items: List<SyncTaskItem>, now: OffsetDateTime, self: String?): LinkedHashMap<String, SyncTaskItem> {
        val byId = LinkedHashMap<String, SyncTaskItem>()
        for (item in items) {
            // R7.1 review: ids by lower case, as the shared lists compare them.
            val key = item.id.lowercase()
            val held = byId[key]
            byId[key] = when {
                held == null -> item
                held.task.isLive && item.task.isLive -> mergeItem(held, item)
                // Tombstones kept here whatever their age: the merge of the two lists applies the 30-day rule to the winner.
                else -> SharedListMerge.merge(listOf(held), listOf(item), now.toInstant(), self, keepTombstones = true).single()
            }
        }

        return byId
    }

    // A stamp more than 2 minutes past the receiver's now (a fast clock) counts as that far ahead, the shared lists' rule.
    private fun clamped(item: SyncTaskItem, limit: OffsetDateTime): SyncTaskItem {
        val task = item.task
        val clampedTask = task.copy(
            editedAt = DotNetTime.atMost(task.editedAt, limit),
            touchedAt = DotNetTime.atMost(task.touchedAt, limit),
            deletedAt = task.deletedAt?.let { DotNetTime.atMost(it, limit) },
        )

        var stamps = item.stamps
        for (field in TaskFieldValues.ALL) {
            val stamp = stamps.of(field) ?: continue
            if (DotNetTime.compare(stamp.at, limit) > 0) {
                stamps = stamps.with(field, FieldStamps.clamped(stamp, limit))
            }
        }

        return item.copy(task = clampedTask, stamps = stamps)
    }
}
