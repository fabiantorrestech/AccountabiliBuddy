package com.accountabilibuddy.companion.engine

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
     * A new hour-long part from [startMinute] (rounded down to 15 minutes), shortened to end where the next part starts.
     * Null when that time already has a part or less than 15 minutes are free.
     */
    fun addAt(template: DayTemplate, startMinute: Int, categoryId: UUID): DayTemplate? {
        val start = (startMinute / SNAP_MINUTES * SNAP_MINUTES).coerceIn(0, MINUTES_PER_DAY - SNAP_MINUTES)
        val taken = template.parts.flatMap(::ranges)
        if (taken.any { it.first <= start && start < it.second }) {
            return null
        }

        val end = (taken.filter { it.first > start }.map { it.first } + min(start + NEW_PART_MINUTES, MINUTES_PER_DAY)).min()
        if (end - start < SNAP_MINUTES) {
            return null
        }

        return DayTemplate(template.parts + PlanPart(UUID.randomUUID(), categoryId, timeOf(start), timeOf(end)))
    }

    /**
     * Moves or resizes a part to [startMinute]–[endMinute] (snapped), cut back where it would run into another part.
     * [crossesMidnight] is decided when the drag starts: a night part stays a night part, a day part a day part.
     * Null when the change is refused or less than 15 minutes would be left.
     */
    fun move(template: DayTemplate, partId: UUID, startMinute: Int, endMinute: Int, crossesMidnight: Boolean): DayTemplate? {
        val part = template.parts.firstOrNull { it.id == partId } ?: return null
        var start = min(snap(startMinute.toDouble()), MINUTES_PER_DAY - SNAP_MINUTES)
        var end = snap(endMinute.toDouble())
        val others = template.parts.filter { it.id != partId }.flatMap(::ranges).sortedBy { it.first }

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
        return DayTemplate(template.parts.map { if (it.id == partId) moved else it })
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
