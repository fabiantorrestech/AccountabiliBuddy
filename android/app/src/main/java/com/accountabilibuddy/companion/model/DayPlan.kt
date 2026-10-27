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
    /** A Sleep part woken by the wake-up alarm (schema v9, ADR 33): the alarm, else bedtime plus the goal; its end follows bedtime plus the goal. */
    val wakeByAlarm: Boolean = false,
    /**
     * A fixed lunch period (schema v13): a Rest part with fixed hours (not a flexible break) that starts by itself at its
     * start and lasts as long as the part does, as if "Start lunch" had been pressed. False = the user starts it, or it is just time in the plan.
     */
    val autoStart: Boolean = false,
) {
    val endsAtMidnight: Boolean get() = end == LocalTime.MIDNIGHT
    val crossesMidnight: Boolean get() = !endsAtMidnight && !end.isAfter(start)
    val isFlexible: Boolean get() = (flexMinutes ?: 0) > 0
}

/** A day's flexible break placed on its date (as on the PC's Core FlexibleBreak). */
data class FlexibleBreak(val name: String, val color: Long, val window: LocalWindow, val minutes: Int, val hintAt: java.time.LocalDateTime?)

/** A day's fixed lunch period (schema v13) placed on its date: it starts by itself at [window]'s start and lasts as long (the PC's Core FixedLunch). */
data class FixedLunch(val name: String, val color: Long, val window: LocalWindow)

/** How a day template says its day is guarded (schema v12, ADR 47; the PC's DayMode). */
enum class DayMode { Schedule, Budget, Free }

/** A Schedule day's simple work window (schema v12, ADR 47): the work part when the template has none; an [end] of 00:00 is midnight. */
data class WorkWindow(val start: LocalTime, val end: LocalTime)

/**
 * A day's parts; [blocks] = its block rules (schema v10, ADR 37), null = [BlockRules.Default]. [mode], [workWindow]
 * and [budgetMinutes] are schema v12 (ADR 47): a Budget day's parts are only its non-productive windows.
 */
data class DayTemplate(
    val parts: List<PlanPart> = emptyList(),
    val blocks: BlockRules? = null,
    val mode: DayMode = DayMode.Schedule,
    val workWindow: WorkWindow? = null,
    val budgetMinutes: Int? = null,
) {
    val rules: BlockRules get() = blocks ?: BlockRules.Default

    /**
     * A Schedule template that has a work window and no work parts of its own, with the window added as work parts
     * (the first Watch category): one part, or several when fixed rest, sleep or other parts of the template lie inside
     * the window (lunch, a nap), which are left out of the work and stay as they are. A flexible break stays inside the
     * window, as it does over a timeline's work. Pieces under 15 minutes are left out. Any other template as it is.
     * The PC's DayTemplate.WithWorkWindowPart.
     */
    fun withWorkWindowPart(categories: List<DayCategory>): DayTemplate {
        val window = workWindow ?: return this
        if (mode != DayMode.Schedule || parts.any { part -> categories.firstOrNull { it.id == part.categoryId }?.behavior?.isWork == true }) return this
        val work = categories.firstOrNull { it.behavior == CategoryBehavior.Watch } ?: return this
        return copy(parts = parts + windowPieces(window, work))
    }

    // The window minus the fixed parts inside it, as work parts: the first with WORK_WINDOW_PART_ID, the others numbered after it.
    private fun windowPieces(window: WorkWindow, work: DayCategory): List<PlanPart> {
        val day = 24 * 60
        val start = window.start.hour * 60 + window.start.minute
        val end = if (window.end == LocalTime.MIDNIGHT) day else window.end.hour * 60 + window.end.minute
        val holes = parts.filter { !it.isFlexible }.flatMap { part ->
            val from = part.start.hour * 60 + part.start.minute
            val to = if (part.endsAtMidnight) day else part.end.hour * 60 + part.end.minute
            if (part.crossesMidnight) listOf(from to day, 0 to to) else listOf(from to to)
        }
        val pieces = mutableListOf<PlanPart>()
        fun timeAt(minutes: Int): LocalTime = if (minutes >= day) LocalTime.MIDNIGHT else LocalTime.of(minutes / 60, minutes % 60)
        fun add(from: Int, to: Int) {
            if (to - from < 15) return
            val id = if (pieces.isEmpty()) WORK_WINDOW_PART_ID else UUID.fromString("5c7a2e10-0f10-4b3d-8f6e-1a9c4000" + "%04x".format(pieces.size))
            pieces += PlanPart(id, work.id, timeAt(from), timeAt(to))
        }
        var cursor = start
        for ((holeStart, holeEnd) in holes.filter { it.second > start && it.first < end }.sortedBy { it.first }) {
            if (holeStart > cursor) add(cursor, minOf(holeStart, end))
            cursor = maxOf(cursor, holeEnd)
        }
        if (cursor < end) add(cursor, end)
        return pieces
    }

    companion object {
        /** The id of the part standing in for a work window (its first piece), the PC's same one. */
        val WORK_WINDOW_PART_ID: UUID = UUID.fromString("5c7a2e10-0f00-4b3d-8f6e-1a9c40000f00")

        /** True for any piece standing in for a work window: [WORK_WINDOW_PART_ID] or the ids numbered after it. */
        fun isWorkWindowPartId(id: UUID): Boolean = id == WORK_WINDOW_PART_ID || id.toString().startsWith("5c7a2e10-0f10-4b3d-8f6e-1a9c4000")
    }
}

/** A named day template (schema v12, ADR 47); the cycle's weeks point at its [id]. */
data class PlanTemplate(val id: UUID, val name: String, val template: DayTemplate)

/** One weekday of a week of the cycle (schema v12): which template that weekday follows. */
data class WeekdayTemplate(val day: DayOfWeek, val templateId: UUID)

/**
 * Which template each weekday follows (schema v13, ADR 47): a repeating cycle of 1 to [MAX_WEEKS] weeks, each a
 * weekday-to-template map. Week 1 starts on [startsOn] (a Monday) and the cycle repeats from there, before it too: the
 * week of a date is the whole weeks since [startsOn], floored, modulo the number of weeks. A weekday missing from the
 * week in force follows the Off day template. The PC's PlanCycle.
 */
data class PlanCycle(val weeks: List<List<WeekdayTemplate>>, val startsOn: LocalDate) {
    /** The week of the cycle [date] is in, 0-based (week 1 is 0). */
    fun weekIndexFor(date: LocalDate): Int {
        if (weeks.isEmpty()) return 0
        // Floor division, so the weeks before startsOn repeat the cycle backwards (the week before week 1 is the last).
        val weeksSince = Math.floorDiv(java.time.temporal.ChronoUnit.DAYS.between(startsOn, date), 7L)
        return Math.floorMod(weeksSince, weeks.size.toLong()).toInt()
    }

    /** The week of the cycle [date] is in, 1-based. */
    fun weekNumberFor(date: LocalDate): Int = weekIndexFor(date) + 1

    /** The template id [date] follows, or null when its weekday is missing from the week in force. */
    fun templateIdFor(date: LocalDate): UUID? = weeks.getOrNull(weekIndexFor(date))?.firstOrNull { it.day == date.dayOfWeek }?.templateId

    companion object {
        /** The longest cycle the plan allows. */
        const val MAX_WEEKS = 8
    }
}

/** A single date planned differently; its template replaces that day's usual one. */
data class OneOffDay(val id: UUID, val date: LocalDate, val template: DayTemplate, val note: String? = null)

/** A part of the day plan placed on real dates. */
data class PlannedPart(val part: PlanPart, val category: DayCategory, val window: LocalWindow) {
    val name: String get() = part.label ?: category.name
}

/**
 * The PC's day plan (schema v5, ADR 18; named templates since schema v12 and a repeating cycle of weeks since v13, ADR
 * 47): categories, named [templates], the [cycle] saying which one each weekday follows in each of 1 to 8 weeks, and
 * one-off days. A port of the Windows Core's DayPlan (same rules, kept in step by hand).
 */
data class DayPlan(
    val categories: List<DayCategory>,
    val templates: List<PlanTemplate>,
    val cycle: PlanCycle,
    val oneOffDays: List<OneOffDay> = emptyList(),
) {
    /** The "Workday" template as stored (the classic two-template view, until the editors know named templates); empty when removed. */
    val workday: DayTemplate get() = templateById(WORKDAY_TEMPLATE_ID) ?: DayTemplate()

    /** The "Off day" template as stored; see [workday]. */
    val otherDay: DayTemplate get() = templateById(OFF_DAY_TEMPLATE_ID) ?: DayTemplate()

    /** The weekdays of week 1 that follow the "Workday" template (the classic two-template view). */
    val workdays: Set<DayOfWeek> get() = cycle.weeks.firstOrNull().orEmpty().filter { it.templateId == WORKDAY_TEMPLATE_ID }.map { it.day }.toSet()

    /** The template with [id] as stored; null when there is none. */
    fun templateById(id: UUID): DayTemplate? = templates.firstOrNull { it.id == id }?.template

    /** The plan with the "Workday" template replaced (added when it was removed). */
    fun withWorkday(template: DayTemplate): DayPlan = withTemplate(WORKDAY_TEMPLATE_ID, "Workday", template)

    /** The plan with the "Off day" template replaced (added when it was removed). */
    fun withOtherDay(template: DayTemplate): DayPlan = withTemplate(OFF_DAY_TEMPLATE_ID, "Off day", template)

    /**
     * Week 1 with exactly [days] following "Workday" (the classic two-template view): a weekday that follows "Workday"
     * and isn't listed goes to "Off day"; one following any other template stays as it is unless listed.
     */
    fun withWorkdays(days: Set<DayOfWeek>): DayPlan {
        val a = MONDAY_FIRST.map { day ->
            val current = cycle.weeks.firstOrNull().orEmpty().firstOrNull { it.day == day }?.templateId ?: OFF_DAY_TEMPLATE_ID
            val next = if (day in days) WORKDAY_TEMPLATE_ID else if (current == WORKDAY_TEMPLATE_ID) OFF_DAY_TEMPLATE_ID else current
            WeekdayTemplate(day, next)
        }
        return copy(cycle = cycle.copy(weeks = listOf(a) + cycle.weeks.drop(1)))
    }

    private fun withTemplate(id: UUID, name: String, template: DayTemplate): DayPlan =
        if (templates.any { it.id == id }) copy(templates = templates.map { if (it.id == id) it.copy(template = template) else it })
        else copy(templates = templates + PlanTemplate(id, name, template))

    fun category(id: UUID): DayCategory? = categories.firstOrNull { it.id == id }

    /** The flexible break (e.g. lunch) on [date]; null on a day without one. */
    fun flexibleBreakOn(date: LocalDate): FlexibleBreak? {
        val part = templateFor(date).parts.firstOrNull { it.isFlexible && !it.crossesMidnight } ?: return null
        val category = category(part.categoryId) ?: return null
        val day = date.atStartOfDay()
        val end = if (part.endsAtMidnight) day.plusDays(1) else date.atTime(part.end)
        return FlexibleBreak(part.label ?: category.name, category.argb, LocalWindow(date.atTime(part.start), end), part.flexMinutes!!, part.hintAt?.let { date.atTime(it) })
    }

    /** The fixed lunch period (a Rest part with fixed hours that starts by itself) on [date]; null on a day without one. */
    fun fixedLunchOn(date: LocalDate): FixedLunch? {
        val part = templateFor(date).parts.firstOrNull { it.autoStart && !it.isFlexible && !it.crossesMidnight } ?: return null
        val category = category(part.categoryId) ?: return null
        val end = if (part.endsAtMidnight) date.atStartOfDay().plusDays(1) else date.atTime(part.end)
        return FixedLunch(part.label ?: category.name, category.argb, LocalWindow(date.atTime(part.start), end))
    }

    /**
     * True on a workday, the one rule for every device and feature (the PC's DayPlan.IsWorkday is the same): a one-off day
     * with work in it, else a date whose week of the cycle maps its weekday to the "Workday" template, or to a template of its own that
     * has work (a Budget day, or work parts or a work window). A one-off day replaces a workday's template, so a one-off
     * day off on a Monday isn't one. With [withOneOffDays] off, the week map decides alone.
     */
    fun isWorkday(date: LocalDate, withOneOffDays: Boolean = true): Boolean {
        val oneOff = if (withOneOffDays) oneOffDays.firstOrNull { it.date == date } else null
        if (oneOff != null) return hasWork(oneOff.template)
        val id = cycle.templateIdFor(date) ?: return false
        return isWorkdayTemplate(id)
    }

    /** [isWorkday] by the week map alone (one-off days ignored): the kind of a night or morning for the smart bedtime and wake estimates. */
    fun isWorkdayByWeek(date: LocalDate): Boolean = isWorkday(date, withOneOffDays = false)

    /** True when a day that follows template [id] is a workday: the "Workday" template, or a template of its own with work. */
    fun isWorkdayTemplate(id: UUID): Boolean =
        id == WORKDAY_TEMPLATE_ID || (id != OFF_DAY_TEMPLATE_ID && templateById(id)?.let { hasWork(it) } == true)

    /**
     * The template a plan's workdays (or, with [workday] off, its other days) usually follow: the one the first such
     * weekday of week 1, Monday first, follows; null when there is none. The PC's DayPlan.TypicalTemplate.
     */
    fun typicalTemplate(workday: Boolean): DayTemplate? {
        for (day in MONDAY_FIRST) {
            val id = cycle.weeks.firstOrNull().orEmpty().firstOrNull { it.day == day }?.templateId ?: OFF_DAY_TEMPLATE_ID
            if (isWorkdayTemplate(id) == workday) templateById(id)?.let { return it }
        }

        return null
    }

    private fun hasWork(template: DayTemplate): Boolean =
        template.mode == DayMode.Budget ||
            (template.mode == DayMode.Schedule && template.withWorkWindowPart(categories).parts.any { category(it.categoryId)?.behavior?.isWork == true })

    /**
     * The template a date follows, in order: its one-off day, that week of the cycle's template for the weekday, the Off day
     * template (empty when there is none). A Schedule template with only a work window comes with that window as its work
     * part, so everything that reads parts sees it.
     */
    fun templateFor(date: LocalDate): DayTemplate =
        (oneOffDays.firstOrNull { it.date == date }?.template ?: weekMapTemplate(date)).withWorkWindowPart(categories)

    /** The template the week map gives a date, ignoring one-off days: what the date would follow if nothing replaced it. */
    fun weekTemplateFor(date: LocalDate): DayTemplate = weekMapTemplate(date).withWorkWindowPart(categories)

    private fun weekMapTemplate(date: LocalDate): DayTemplate =
        cycle.templateIdFor(date)?.let { templateById(it) } ?: templateById(OFF_DAY_TEMPLATE_ID) ?: DayTemplate()

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
        return night?.let { SleepTimes(it.start, it.end, it.wakeByAlarm) }
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

        /** The migrated "Workday" template (schema v12), the PC's same id. */
        val WORKDAY_TEMPLATE_ID: UUID = UUID.fromString("9d3e5a20-0001-4e8a-b2c4-6f1d00000001")

        /** The "Off day" template, also the fallback for a date no map entry covers (schema v12), the PC's same id. */
        val OFF_DAY_TEMPLATE_ID: UUID = UUID.fromString("9d3e5a20-0002-4e8a-b2c4-6f1d00000002")

        /** The weekdays in the order the week map lists them (declared before [Default], which uses it). */
        val MONDAY_FIRST: List<DayOfWeek> = listOf(
            DayOfWeek.MONDAY, DayOfWeek.TUESDAY, DayOfWeek.WEDNESDAY, DayOfWeek.THURSDAY, DayOfWeek.FRIDAY, DayOfWeek.SATURDAY, DayOfWeek.SUNDAY,
        )

        /**
         * The Monday week 1 starts on for a plan that never set one (migrated from before the cycle, or new), the PC's same
         * date, so the same plan is the same everywhere. It makes no difference with one week.
         */
        val DEFAULT_CYCLE_START: LocalDate = LocalDate.of(2024, 1, 1)

        /**
         * A plan of the two classic templates (schema v11's shape): [workdays] follow "Workday", every other weekday "Off day",
         * a one-week cycle. The PC's DayPlan.FromWorkdayAndOffDay.
         */
        fun fromWorkdayAndOffDay(
            categories: List<DayCategory>,
            workdays: Set<DayOfWeek>,
            workday: DayTemplate,
            otherDay: DayTemplate,
            oneOffDays: List<OneOffDay> = emptyList(),
        ): DayPlan = DayPlan(
            categories = categories,
            templates = listOf(PlanTemplate(WORKDAY_TEMPLATE_ID, "Workday", workday), PlanTemplate(OFF_DAY_TEMPLATE_ID, "Off day", otherDay)),
            cycle = PlanCycle(listOf(MONDAY_FIRST.map { WeekdayTemplate(it, if (it in workdays) WORKDAY_TEMPLATE_ID else OFF_DAY_TEMPLATE_ID) }), DEFAULT_CYCLE_START),
            oneOffDays = oneOffDays,
        )

        /** The PC's starting plan (same ids, two comms windows since schema v6), used for documents from before schema v5. */
        val Default = fromWorkdayAndOffDay(
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
