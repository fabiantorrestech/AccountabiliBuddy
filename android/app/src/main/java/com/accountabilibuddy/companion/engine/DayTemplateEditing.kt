package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.model.DayTemplate
import com.accountabilibuddy.companion.model.PlanPart
import java.time.LocalTime
import java.util.UUID
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt

/**
 * The day plan editor's changes to a template, in minutes of the day (0–1440): snapped to 15 minutes and never
 * overlapping another part. A port of the Windows Core's DayTemplateEditing (same rules, kept in step by hand).
 */
object DayTemplateEditing {
    const val SNAP_MINUTES = 15
    private const val MINUTES_PER_DAY = 24 * 60
    private const val NEW_PART_MINUTES = 60

    /** The nearest multiple of 15 minutes, within the day. */
    fun snap(minutes: Double): Int = ((minutes / SNAP_MINUTES).roundToInt() * SNAP_MINUTES).coerceIn(0, MINUTES_PER_DAY)

    /** Minutes from midnight to [part]'s end; an end of 00:00 is 1440 (midnight at the end of its own day). */
    fun endMinutes(part: PlanPart): Int = if (part.endsAtMidnight) MINUTES_PER_DAY else minutesOf(part.end)

    fun minutesOf(time: LocalTime): Int = time.hour * 60 + time.minute

    /** Minutes of the day as a time; 1440 is 00:00. */
    fun timeOf(minutes: Int): LocalTime = LocalTime.of((minutes % MINUTES_PER_DAY) / 60, minutes % 60)

    /**
     * True when the editor may lay these two categories' parts over each other: any two (ADR 63), given the plan's categories; none: no
     * overlaps. As on the PC.
     */
    fun mayOverlap(a: UUID, b: UUID, categories: List<DayCategory>): Boolean = categories.isNotEmpty()

    /**
     * A new hour-long part from [startMinute] (rounded down to 15 minutes), shortened to end where the next part starts. In a part's time
     * it is laid over that part at its full hour (ADR 63). Null when, with no categories given, that time already has a part or less than 15 minutes are free.
     */
    fun addAt(template: DayTemplate, startMinute: Int, categoryId: UUID, categories: List<DayCategory> = emptyList()): DayTemplate? {
        val start = (startMinute / SNAP_MINUTES * SNAP_MINUTES).coerceIn(0, MINUTES_PER_DAY - SNAP_MINUTES)
        val taken = template.parts.filterNot { it.isFlexible }.flatMap(::ranges)
        val fullEnd = min(start + NEW_PART_MINUTES, MINUTES_PER_DAY)
        val overTaken = taken.any { it.first <= start && start < it.second }
        if (overTaken && !mayOverlap(categoryId, categoryId, categories)) {
            return null
        }

        var end = if (overTaken) fullEnd else (taken.filter { it.first > start }.map { it.first } + fullEnd).min()
        if (end - start < SNAP_MINUTES) {
            if (!mayOverlap(categoryId, categoryId, categories)) {
                return null
            }

            end = fullEnd
        }

        return template.copy(parts = template.parts + PlanPart(UUID.randomUUID(), categoryId, timeOf(start), timeOf(end)))
    }

    /**
     * Moves or resizes a part to [startMinute]–[endMinute] (snapped); with no categories given, cut back where it would run into another part.
     * [crossesMidnight] is decided when the drag starts: a night part stays a night part, a day part a day part. With [stickToNeighbours], an
     * edge being dragged stops at a neighbour's edge for the first 15 minutes past it, so lining parts up doesn't overlap them by accident
     * (ADR 63); dragging further overlaps. Null when the change is refused or less than 15 minutes would be left.
     */
    fun move(
        template: DayTemplate, partId: UUID, startMinute: Int, endMinute: Int, crossesMidnight: Boolean, categories: List<DayCategory> = emptyList(), stickToNeighbours: Boolean = false,
    ): DayTemplate? {
        val part = template.parts.firstOrNull { it.id == partId } ?: return null
        var start = min(snap(startMinute.toDouble()), MINUTES_PER_DAY - SNAP_MINUTES)
        var end = snap(endMinute.toDouble())
        if (stickToNeighbours && !crossesMidnight && !part.isFlexible) {
            val startMoved = start != minutesOf(part.start)
            val endMoved = end != endMinutes(part)
            for ((otherStart, otherEnd) in template.parts.filter { it.id != partId && !it.isFlexible }.flatMap(::ranges)) {
                if (endMoved && start < otherStart && otherStart < end && end <= otherStart + SNAP_MINUTES) end = otherStart
                if (startMoved && otherEnd < end && otherEnd > start && start >= otherEnd - SNAP_MINUTES) start = otherEnd
            }
        }

        // A flexible break's window lies over the work around it (schema v8): neither pushes the other.
        val others = template.parts.filter { it.id != partId && !part.isFlexible && !it.isFlexible && !mayOverlap(part.categoryId, it.categoryId, categories) }
            .flatMap(::ranges).sortedBy { it.first }

        if (!crossesMidnight) {
            if (start >= end) {
                return null
            }

            for ((otherStart, otherEnd) in others.filter { it.first < end && start < it.second }) {
                if (otherStart <= start) {
                    start = otherEnd
                } else {
                    end = min(end, otherStart)
                }
            }

            if (end - start < SNAP_MINUTES) {
                return null
            }
        } else {
            // The wake time stays in the morning: 00:00 or 24:00 would make it an evening part of its own day.
            if (end == 0 || end >= MINUTES_PER_DAY) {
                return null
            }

            // The evening piece runs from the start to midnight, the morning piece from midnight to the end.
            for ((_, otherEnd) in others.filter { it.second > start }) {
                start = max(start, otherEnd)
            }

            for ((otherStart, _) in others.filter { it.first < end }) {
                end = min(end, otherStart)
            }

            if (start <= end || start >= MINUTES_PER_DAY || MINUTES_PER_DAY - start + end < SNAP_MINUTES) {
                return null
            }
        }

        val moved = part.copy(start = timeOf(start), end = timeOf(end))
        return template.copy(parts = template.parts.map { if (it.id == partId) moved else it })
    }

    /** A part as minute ranges within one day; a midnight-crossing part is its start to midnight plus midnight to its end. */
    fun ranges(part: PlanPart): List<Pair<Int, Int>> {
        val start = minutesOf(part.start)
        if (!part.crossesMidnight) {
            return listOf(start to endMinutes(part))
        }

        val end = minutesOf(part.end)
        return if (end > 0) listOf(start to MINUTES_PER_DAY, 0 to end) else listOf(start to MINUTES_PER_DAY)
    }
}
