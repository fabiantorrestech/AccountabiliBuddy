package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime
import java.util.UUID
import kotlinx.serialization.json.JsonObject

/**
 * A user-made group of goals (brief §5.1), e.g. "Work" or "Learning"; the PC's twin is Core's `GoalArea`, with the same JSON
 * ([GoalJson]). Carries the shared-item stamps of ADR 48/72 (stable id, position, edit time, tombstone) so syncing it later
 * (ADR 79, Proposed) is a merge, not a migration; nothing syncs it now. Whether it is collapsed is per-device UI state.
 *
 * @property name The user's private text: sealed at rest, never logged.
 * @property icon A Material icon name (ADR 32).
 * @property color "#RRGGBB".
 * @property position Its place among the areas: lower first; fractions put it between two others.
 * @property editedAt Its newest change: the item stamp (newest edit wins).
 * @property deletedAt Set on a tombstone (kept 30 days, no text).
 * @property extra Fields written by a newer app, kept and written back unchanged after the known ones (ADR 79 forward compatibility);
 * empty when there are none. May hold private text: sealed at rest, never logged.
 */
data class GoalArea(
    override val id: UUID,
    val name: String,
    val icon: String,
    val color: String,
    override val position: Double,
    override val editedAt: OffsetDateTime,
    override val deletedAt: OffsetDateTime? = null,
    val extra: JsonObject = JsonObject(emptyMap()),
) : GoalMergeItem {
    override val tieBreak: String get() = GoalJson.text(GoalJson.writeArea(this))

    override fun withStamps(editedAt: OffsetDateTime, deletedAt: OffsetDateTime?): GoalArea = copy(editedAt = editedAt, deletedAt = deletedAt)

    val isLive: Boolean get() = deletedAt == null
}
