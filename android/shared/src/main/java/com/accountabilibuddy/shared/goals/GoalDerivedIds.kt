package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.util.UUID

/** The id of a device's derived entry for a goal, source and day: the same on every device, so a re-read updates it (the PC's `GoalDerivedIds`). */
object GoalDerivedIds {
    private val NAMESPACE: UUID = UUID.fromString("7c1d2e94-3b5a-4f60-9e8d-0a7b6c5d4e31")

    fun forEntry(goalId: UUID, source: GoalEntrySource, deviceId: UUID, day: LocalDate): UUID =
        GoalNameId.of(NAMESPACE, listOf(goalId.toString(), source.name, deviceId.toString(), day.toString()).joinToString("|"))

    /** Whether the entry carries the id its device would give it (a derived source with a device). */
    fun isDerivedId(entry: GoalEntry): Boolean {
        val device = entry.deviceId ?: return false
        return GoalDerivedWriter.isDerived(entry.source) && entry.id == forEntry(entry.goalId, entry.source, device, entry.day)
    }
}
