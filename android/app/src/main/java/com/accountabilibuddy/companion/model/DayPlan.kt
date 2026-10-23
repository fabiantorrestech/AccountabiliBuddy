package com.accountabilibuddy.companion.model

import java.time.DayOfWeek
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.util.UUID

/**
 * What a day-plan category asks of the buddy (the PC's CategoryBehavior). Comms (schema v6) is work time for mail and
 * chat: planned work like Watch.
 */
enum class CategoryBehavior {
    Watch, Rest, Sleep, Comms,

    /** A guarded warm-up before work (schema v7, ADR 31): not planned work; the phone shows a quiet notice in it. */
    PreWork;

    /** Planned work: Watch and Comms parts. */
    val isWork: Boolean get() = this == Watch || this == Comms
}

/** A kind of time in the day plan, e.g. Work, Extra Work, Lunch or Sleep. [color] is "#RRGGBB". */
data class DayCategory(val id: UUID, val name: String, val color: String, val behavior: CategoryBehavior) {
    /** The colour as ARGB, opaque. */
    val argb: Long get() = 0xFF000000L or (color.removePrefix("#").toLongOrNull(16) ?: 0x64748BL)
}

/**
 * One stretch of a day template. A part whose [end] isn't after its [start] belongs to the day it ends on and starts
 * the evening before; an [end] of 00:00 is midnight at the end of its own day.
 */
data class PlanPart(
    val id: UUID,
    val categoryId: UUID,
    val start: LocalTime,
    val end: LocalTime,
    val label: String? = null,
    /** A flexible break (schema v8, e.g. lunch): start–end is its window, this how long it lasts once started; null = fixed hours. */
    val flexMinutes: Int? = null,
    /** A flexible break not started by then gets one hint; null = none. */
    val hintAt: LocalTime? = null,
) {
    val endsAtMidnight: Boolean get() = end == LocalTime.MIDNIGHT
    val crossesMidnight: Boolean get() = !endsAtMidnight && !end.isAfter(start)
    val isFlexible: Boolean get() = (flexMinutes ?: 0) > 0
}

/** A day's flexible break placed on its date (as on the PC's Core FlexibleBreak). */
data class FlexibleBreak(val name: String, val color: Long, val window: LocalWindow, val minutes: Int, val hintAt: java.time.LocalDateTime?)

data class DayTemplate(val parts: List<PlanPart> = emptyList())

/** A single date planned differently; its template replaces that day's usual one. */
data class OneOffDay(val id: UUID, val date: LocalDate, val template: DayTemplate, val note: String? = null)

/** A part of the day plan placed on real dates. */
data class PlannedPart(val part: PlanPart, val category: DayCategory, val window: LocalWindow) {
    val name: String get() = part.label ?: category.name
}

/**
 * The PC's day plan (schema v5, ADR 18): categories, a template for workdays and one for the other days, and one-off
 * days. A port of the Windows Core's DayPlan (same rules, kept in step by hand).
 */
data class DayPlan(
    val categories: List<DayCategory>,
    val workdays: Set<DayOfWeek>,
    val workday: DayTemplate,
    val otherDay: DayTemplate,
    val oneOffDays: List<OneOffDay> = emptyList(),
) {
    fun category(id: UUID): DayCategory? = categories.firstOrNull { it.id == id }

    /** The flexible break (e.g. lunch) on [date]; null on a day without one. */
    fun flexibleBreakOn(date: LocalDate): FlexibleBreak? {
        val part = templateFor(date).parts.firstOrNull { it.isFlexible && !it.crossesMidnight } ?: return null
        val category = category(part.categoryId) ?: return null
        val day = date.atStartOfDay()
        val end = if (part.endsAtMidnight) day.plusDays(1) else date.atTime(part.end)
        return FlexibleBreak(part.label ?: category.name, category.argb, LocalWindow(date.atTime(part.start), end), part.flexMinutes!!, part.hintAt?.let { date.atTime(it) })
    }

    fun templateFor(date: LocalDate): DayTemplate =
        oneOffDays.firstOrNull { it.date == date }?.template ?: if (date.dayOfWeek in workdays) workday else otherDay

    /**
     * Every part touching [range], on real dates, clipped to the range, in order. Where parts of different dates
     * overlap (yesterday's evening and tonight's sleep, which belongs to tomorrow), the later date's part wins and the
     * earlier one keeps only what's outside it.
     */
    fun partsIn(range: LocalWindow): List<PlannedPart> {
        val placed = mutableListOf<Pair<PlannedPart, LocalDate>>()
        var date = range.start.toLocalDate().minusDays(1)
        val last = range.end.toLocalDate().plusDays(1)
        while (!date.isAfter(last)) {
            for (part in templateFor(date).parts) {
                val category = category(part.categoryId) ?: continue
                val start = (if (part.crossesMidnight) date.minusDays(1) else date).atTime(part.start)
                val end = if (part.endsAtMidnight) date.plusDays(1).atStartOfDay() else date.atTime(part.end)
                placed.add(PlannedPart(part, category, LocalWindow(start, end)) to date)
            }

            date = date.plusDays(1)
        }

        val parts = mutableListOf<PlannedPart>()
        for ((planned, day) in placed) {
            // Parts of the same date never overlap (the PC validates them); later dates take precedence.
            var pieces = listOf(planned.window)
            for ((later, _) in placed.filter { it.second.isAfter(day) }) {
                pieces = pieces.flatMap { without(it, later.window) }
            }

            for (piece in pieces) {
                val start = maxOf(piece.start, range.start)
                val end = minOf(piece.end, range.end)
                if (end.isAfter(start)) {
                    parts.add(planned.copy(window = LocalWindow(start, end)))
                }
            }
        }

        return parts.sortedWith(compareBy<PlannedPart>({ it.window.start }, { it.window.end }, { it.part.id }))
    }

    /**
     * The night before [day] as the plan has it for that day: its Sleep part from the evening before, else one
     * starting before noon; a nap later in the day isn't the night. Null when it has none.
     */
    fun nightBefore(day: LocalDate): SleepTimes? {
        val sleeps = templateFor(day).parts.filter { category(it.categoryId)?.behavior == CategoryBehavior.Sleep }
        val night = sleeps.firstOrNull { it.crossesMidnight }
            ?: sleeps.filter { it.start.isBefore(LocalTime.NOON) }.minByOrNull { it.start }
        return night?.let { SleepTimes(it.start, it.end) }
    }

    fun partAt(time: LocalDateTime): PlannedPart? =
        partsIn(LocalWindow(time, time.plusMinutes(1))).firstOrNull { it.window.contains(time) }

    /** What's left of [window] outside [hole]: nothing, one piece or two. */
    private fun without(window: LocalWindow, hole: LocalWindow): List<LocalWindow> {
        if (!hole.end.isAfter(window.start) || !hole.start.isBefore(window.end)) {
            return listOf(window)
        }

        val pieces = mutableListOf<LocalWindow>()
        if (hole.start.isAfter(window.start)) {
            pieces.add(LocalWindow(window.start, hole.start))
        }

        if (hole.end.isBefore(window.end)) {
            pieces.add(LocalWindow(hole.end, window.end))
        }

        return pieces
    }

    companion object {
        private val work = DayCategory(UUID.fromString("8b1f0c52-0001-4c6e-9a51-3f2d7c000001"), "Work", "#2563EB", CategoryBehavior.Watch)
        private val extraWork = DayCategory(UUID.fromString("8b1f0c52-0002-4c6e-9a51-3f2d7c000002"), "Extra Work", "#7C3AED", CategoryBehavior.Watch)
        private val lunch = DayCategory(UUID.fromString("8b1f0c52-0003-4c6e-9a51-3f2d7c000003"), "Lunch", "#CA8A04", CategoryBehavior.Rest)
        private val evenings = DayCategory(UUID.fromString("8b1f0c52-0004-4c6e-9a51-3f2d7c000004"), "Evenings", "#0D9488", CategoryBehavior.Rest)
        private val freeTime = DayCategory(UUID.fromString("8b1f0c52-0005-4c6e-9a51-3f2d7c000005"), "Free time", "#16A34A", CategoryBehavior.Rest)
        private val sleep = DayCategory(UUID.fromString("8b1f0c52-0006-4c6e-9a51-3f2d7c000006"), "Sleep", "#334155", CategoryBehavior.Sleep)

        /** Mail and chat windows (schema v6), the PC's same id. */
        val comms = DayCategory(UUID.fromString("8b1f0c52-0007-4c6e-9a51-3f2d7c000007"), "Comms", "#0EA5E9", CategoryBehavior.Comms)

        /** The guarded warm-up before work (schema v7, ADR 31), the PC's same id; no parts by default. */
        val preWork = DayCategory(UUID.fromString("8b1f0c52-0008-4c6e-9a51-3f2d7c000008"), "Pre-Work", "#D97706", CategoryBehavior.PreWork)

        /** The PC's starting plan (same ids, two comms windows since schema v6), used for documents from before schema v5. */
        val Default = DayPlan(
            categories = listOf(work, extraWork, lunch, evenings, freeTime, sleep, comms, preWork),
            workdays = setOf(DayOfWeek.MONDAY, DayOfWeek.TUESDAY, DayOfWeek.WEDNESDAY, DayOfWeek.THURSDAY, DayOfWeek.FRIDAY),
            workday = DayTemplate(
                listOf(
                    PlanPart(UUID.fromString("5c7a2e10-0001-4b3d-8f6e-1a9c40000001"), work.id, LocalTime.of(9, 0), LocalTime.of(11, 30)),
                    PlanPart(UUID.fromString("5c7a2e10-0008-4b3d-8f6e-1a9c40000008"), comms.id, LocalTime.of(11, 30), LocalTime.of(11, 45)),
                    PlanPart(UUID.fromString("5c7a2e10-0009-4b3d-8f6e-1a9c40000009"), work.id, LocalTime.of(11, 45), LocalTime.of(12, 0)),
                    PlanPart(UUID.fromString("5c7a2e10-0002-4b3d-8f6e-1a9c40000002"), lunch.id, LocalTime.of(12, 0), LocalTime.of(13, 0)),
                    PlanPart(UUID.fromString("5c7a2e10-0003-4b3d-8f6e-1a9c40000003"), work.id, LocalTime.of(13, 0), LocalTime.of(16, 0)),
                    PlanPart(UUID.fromString("5c7a2e10-000a-4b3d-8f6e-1a9c4000000a"), comms.id, LocalTime.of(16, 0), LocalTime.of(16, 15)),
                    PlanPart(UUID.fromString("5c7a2e10-000b-4b3d-8f6e-1a9c4000000b"), work.id, LocalTime.of(16, 15), LocalTime.of(17, 0)),
                    PlanPart(UUID.fromString("5c7a2e10-0004-4b3d-8f6e-1a9c40000004"), extraWork.id, LocalTime.of(18, 0), LocalTime.of(20, 0)),
                    PlanPart(UUID.fromString("5c7a2e10-0005-4b3d-8f6e-1a9c40000005"), sleep.id, LocalTime.of(23, 0), LocalTime.of(7, 0)),
                ),
            ),
            otherDay = DayTemplate(
                listOf(
                    PlanPart(UUID.fromString("5c7a2e10-0006-4b3d-8f6e-1a9c40000006"), freeTime.id, LocalTime.of(10, 0), LocalTime.of(22, 0)),
                    PlanPart(UUID.fromString("5c7a2e10-0007-4b3d-8f6e-1a9c40000007"), sleep.id, LocalTime.of(0, 30), LocalTime.of(8, 30)),
                ),
            ),
        )
    }
}
