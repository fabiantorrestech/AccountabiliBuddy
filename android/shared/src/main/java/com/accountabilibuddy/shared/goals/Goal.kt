package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId
import java.util.UUID

/**
 * One goal (brief §5.1); the PC's twin is Core's `Goal`, with the same JSON ([GoalJson]). Carries the shared-item stamps of
 * ADR 48/72 so syncing it later (ADR 79, Proposed) is a merge, not a migration; nothing syncs it now. Its day values and
 * statuses are computed ([GoalCounting]), never stored.
 *
 * @property areaId The [GoalArea] it sits in.
 * @property name "Spanish". The user's private text: sealed at rest, never logged.
 * @property icon A Material icon name (ADR 32).
 * @property color "#RRGGBB", the heatmap's ramp and the goal's chips.
 * @property sources What counts toward it; a minute counts once per goal however many rules match it.
 * @property createdAt For the goal's age ([GoalAge]) and where its first period starts.
 * @property position Its place in its area: lower first.
 * @property editedAt Its newest change: the item stamp (newest edit wins).
 * @property builtIn True for Deep work only: it can't be paused or deleted, only switched to "Track time only".
 * @property secondTarget Deep work's weekly target next to its daily one; null for every other goal. On the phone Deep work's
 * daily target is read only (ADR 29: set on the PC; [GoalPhoneRules.canEditTarget]).
 * @property pauses The stretches it was paused, oldest first; one still going has no end. Null = never paused.
 * @property showOnToday Whether Today's goals card shows it.
 * @property extraTiles Streak and Total tiles on its detail panel; off by default.
 * @property notes Why it matters, with links. The user's private text: sealed at rest, never logged.
 * @property deletedAt Set on a tombstone (kept 30 days, no text).
 */
data class Goal(
    val id: UUID,
    val areaId: UUID,
    val name: String,
    val icon: String,
    val color: String,
    val kind: GoalKind,
    val target: GoalTarget,
    val sources: List<GoalSourceRule>,
    val createdAt: OffsetDateTime,
    val position: Double,
    val editedAt: OffsetDateTime,
    val builtIn: Boolean = false,
    val secondTarget: GoalTarget? = null,
    val pauses: List<GoalPauseStretch>? = null,
    val showOnToday: Boolean = false,
    val extraTiles: GoalExtraTiles? = null,
    val notes: String? = null,
    val deletedAt: OffsetDateTime? = null,
) {
    val isLive: Boolean get() = deletedAt == null

    /**
     * Whether this is the built-in Deep work goal: flagged built in, or carrying its fixed id (a record that lost the flag is
     * still Deep work). It counts all focus and nothing else (plan §3b 6) and its lowering follows the guarded rule.
     */
    val isDeepWork: Boolean get() = builtIn || id == DEEP_WORK_ID

    /** Whether the goal is paused on [day]. */
    fun isPausedOn(day: LocalDate): Boolean = pauses.orEmpty().any { it.covers(day) }

    /** The local date the goal was created on. */
    fun createdOn(zone: ZoneId): LocalDate = createdAt.atZoneSameInstant(zone).toLocalDate()

    /** Whether [day] is part of the goal's life: on or after the day it was created, and not paused. */
    fun isAliveOn(day: LocalDate, zone: ZoneId): Boolean = day >= createdOn(zone) && !isPausedOn(day)

    companion object {
        /** The built-in Deep work goal's id: the same on every device (the PC's `Goal.DeepWorkId`). */
        val DEEP_WORK_ID: UUID = UUID.fromString("d33b0a11-6f0c-4e2a-9d51-0000000000d1")
    }
}
