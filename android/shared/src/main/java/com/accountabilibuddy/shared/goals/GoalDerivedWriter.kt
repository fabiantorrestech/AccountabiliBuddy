package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamps
import java.time.OffsetDateTime
import java.util.UUID

/**
 * How a device writes the values it measures (ADR 79; the PC's `GoalDerivedWriter`): one entry per goal, source and day under a
 * deterministic id, updated in place only when the value changed, never another device's, never a hand entry.
 */
object GoalDerivedWriter {
    /** The sources a device measures itself (Beeminder is read by one device under its own ids). */
    fun isDerived(source: GoalEntrySource): Boolean =
        source == GoalEntrySource.HealthConnect || source == GoalEntrySource.ScreenTime || source == GoalEntrySource.FocusLocal || source == GoalEntrySource.Calendar

    /** Sources whose real 0 is written (a measured nothing); tags and calendar write 0 only over an earlier value. */
    fun writesZero(source: GoalEntrySource): Boolean = source == GoalEntrySource.HealthConnect || source == GoalEntrySource.ScreenTime

    /** Whether the entry is this device's own derived entry. */
    fun isOwn(entry: GoalEntry, deviceId: UUID): Boolean = entry.deviceId == deviceId && GoalDerivedIds.isDerivedId(entry)

    /** Whether this device may change the entry (Beeminder: only with a token). */
    fun mayChange(entry: GoalEntry, deviceId: UUID, hasBeeminderToken: Boolean): Boolean =
        if (entry.source == GoalEntrySource.Beeminder) hasBeeminderToken else isOwn(entry, deviceId)

    /** The entries to change so this device's derived entries match [readings] ([covered]: days the read covered). Ordered by id text. */
    fun changes(
        held: List<GoalEntry>,
        deviceId: UUID,
        readings: List<GoalDerivedReading>,
        covered: List<GoalDerivedCover>,
        now: OffsetDateTime,
    ): List<GoalEntry> {
        readings.firstOrNull { !isDerived(it.source) }?.let { throw IllegalArgumentException("${it.source} values are not written as a device's derived value.") }
        covered.firstOrNull { !isDerived(it.source) }?.let { throw IllegalArgumentException("${it.source} values are not written as a device's derived value.") }
        // This device's own entries by id (the newest copy when a list holds one twice).
        val own = LinkedHashMap<UUID, GoalEntry>()
        for (entry in held) {
            if (!isOwn(entry, deviceId)) continue
            val kept = own[entry.id]
            if (kept == null || DotNetTime.compare(entry.stamp, kept.stamp) > 0) own[entry.id] = entry
        }

        val fresh = LinkedHashMap<UUID, GoalDerivedReading>()
        val unreadable = HashSet<UUID>()
        for (reading in readings) {
            val id = GoalDerivedIds.forEntry(reading.goalId, reading.source, deviceId, reading.day)
            if (reading.value.isFinite()) {
                fresh[id] = reading
                unreadable.remove(id)
            } else {
                fresh.remove(id)
                unreadable.add(id)
            }
        }

        val changes = LinkedHashMap<UUID, GoalEntry>()
        for ((id, reading) in fresh) {
            val before = own[id]
            if (before == null || !before.isLive) {
                if (reading.value == 0.0 && !writesZero(reading.source)) continue
                val at = FieldStamps.next(now, before?.stamp)
                changes[id] = GoalEntry(id, reading.goalId, reading.day, null, reading.value, reading.source, at, before?.position ?: 0.0, null, deviceId = deviceId)
            } else if (before.value != reading.value) {
                changes[id] = before.copy(value = reading.value, editedAt = FieldStamps.next(now, before.stamp))
            }
        }

        for (entry in own.values) {
            if (entry.isLive && entry.id !in fresh && entry.id !in unreadable &&
                covered.any { it.goalId == entry.goalId && it.source == entry.source && entry.day >= it.from && entry.day <= it.to }
            ) {
                val at = FieldStamps.next(now, entry.stamp)
                changes[entry.id] = entry.copy(editedAt = at, deletedAt = at)
            }
        }

        return changes.values.sortedBy { it.id.toString() }
    }

    /** [held] with [changes] put in place (new ones appended); the list's newer items stay. */
    fun applied(held: GoalLists, changes: List<GoalEntry>): GoalLists {
        if (changes.isEmpty()) return held
        val byId = changes.associateBy { it.id }
        val result = ArrayList<GoalEntry>(held.entries.size + changes.size)
        val placed = HashSet<UUID>()
        for (entry in held.entries) {
            val changed = byId[entry.id]
            if (changed != null) {
                if (placed.add(entry.id)) result.add(changed)
                continue
            }
            result.add(entry)
        }
        result.addAll(changes.filter { it.id !in placed })
        return held.copy(entries = result)
    }
}
