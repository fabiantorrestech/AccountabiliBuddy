package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.time.LocalTime
import kotlin.math.abs
import kotlin.math.floor
import kotlin.math.max

/**
 * A goal's day values and day statuses (brief §5.4; change note §4, §4a), computed from its sources, never stored; the PC's
 * `GoalCounting`, rule for rule:
 * - **Time** sources (focus rules through [GoalFocusAttribution], calendar categories, and time added by hand with a start) are
 *   joined as windows, so a minute counts once per goal; hand time without a start and each reading source's latest reading of
 *   the day add on top. Whole minutes: the windows' total is rounded down.
 * - **Amount** goals: the latest hand edit if there is one, else the day's first reading.
 * - **Habits**: the latest hand entry decides (a tick, or an untick that wins over the threshold); without one the day ticks
 *   itself once its time sources reach the smallest [GoalSourceRule.thresholdMinutes].
 * - **Milestones**: the day's hand entries added up.
 * - A paused day is neither hit nor missed; a day before the goal existed or after today asks for nothing.
 * Readings only count from a source the goal has a switched-on rule for; hand entries always count. Deep work counts all focus
 * and nothing else ([GoalSources.countedFor]). Goal values never feed a guard, the goal unlock or `focusDay`.
 */
object GoalCounting {
    /** The goal's days from [from] to [to], both included. */
    fun days(goal: Goal, data: GoalSourceData, from: LocalDate, to: LocalDate, today: LocalDate): List<GoalDayState> {
        val context = Context(goal, data)
        val days = mutableListOf<GoalDayState>()
        var day = from
        while (day <= to) {
            days += context.day(day, today)
            day = day.plusDays(1)
        }

        return days
    }

    /** One day of the goal. */
    fun day(goal: Goal, data: GoalSourceData, day: LocalDate, today: LocalDate): GoalDayState = Context(goal, data).day(day, today)

    /** The goal's days from the day it was created (or [from], when later) to [today], by day, earliest first. */
    fun history(goal: Goal, data: GoalSourceData, today: LocalDate, from: LocalDate? = null): Map<LocalDate, GoalDayState> {
        val created = goal.createdOn(data.zone)
        val start = if (from != null && from > created) from else created
        return if (start > today) emptyMap() else days(goal, data, start, today, today).associateBy { it.day }
    }

    /**
     * Whether days can be hit one by one: any habit, or a daily target or "N of the period's days" with an amount. A goal with no
     * period amount has no day to hit or miss.
     */
    fun countsDays(goal: Goal): Boolean = goal.kind == GoalKind.Habit || (goal.target.amount != null && goal.target.countsDays)

    /** For a [GoalDirection.Reach] target: whether the level lies below the goal's first value (a weight to lose). False with no value yet. */
    fun reachesDown(goal: Goal, data: GoalSourceData): Boolean {
        val level = goal.target.targetAmount ?: goal.target.amount
        val first = firstValue(goal, data)
        return level != null && first != null && level < first
    }

    /** The goal's first day value ever (the earliest day with a live entry, by the Amount rule); null with none. */
    fun firstValue(goal: Goal, data: GoalSourceData): Double? {
        val readable = readableSources(goal)
        val first = entriesOf(goal, data)
            .filter { it.source == GoalEntrySource.Hand || it.source in readable }
            .groupBy { it.day }
            .minByOrNull { it.key }
            ?: return null
        return amountValue(first.value, readable).first
    }

    /** Which entry source a reading rule reads; null for a rule that counts time or ticks. */
    fun entrySourceOf(type: GoalSourceType): GoalEntrySource? = when (type) {
        GoalSourceType.HealthSteps, GoalSourceType.HealthWeight, GoalSourceType.HealthSleep -> GoalEntrySource.HealthConnect
        GoalSourceType.ScreenTime -> GoalEntrySource.ScreenTime
        GoalSourceType.Beeminder -> GoalEntrySource.Beeminder
        else -> null
    }

    // The goal's live entries; none for Deep work, which counts focus only.
    private fun entriesOf(goal: Goal, data: GoalSourceData): List<GoalEntry> =
        if (goal.isDeepWork) emptyList() else data.entries.filter { it.goalId == goal.id && it.isLive }

    private fun readableSources(goal: Goal): Set<GoalEntrySource> =
        GoalSources.countedFor(goal).filter { it.automatic }.mapNotNull { entrySourceOf(it.type) }.toSet()

    // The latest first: its edit time, then its id (as text), so every device picks the same one.
    private val latestFirst: Comparator<GoalEntry> =
        compareByDescending<GoalEntry> { it.editedAt.toInstant() }.thenByDescending { it.id.toString() }

    private fun latestHand(entries: List<GoalEntry>): GoalEntry? =
        entries.filter { it.source == GoalEntrySource.Hand }.sortedWith(latestFirst).firstOrNull()

    /**
     * The entry that decides an Amount goal's value on a day: the latest hand edit, else the first reading of a switched-on source (by
     * its time, then edit time, then id); null when none counts (and always for Deep work). The goal page marks it "charted".
     */
    fun decidingEntry(goal: Goal, dayEntries: List<GoalEntry>): GoalEntry? =
        if (goal.isDeepWork) null else decidingEntry(dayEntries, readableSources(goal))

    private fun decidingEntry(dayEntries: List<GoalEntry>, readable: Set<GoalEntrySource>): GoalEntry? {
        val hand = latestHand(dayEntries)
        if (hand != null) {
            return hand
        }

        return dayEntries
            .filter { it.source != GoalEntrySource.Hand && it.source in readable }
            .sortedWith(compareBy<GoalEntry> { it.time ?: LocalTime.MIN }.thenBy { it.editedAt.toInstant() }.thenBy { it.id.toString() })
            .firstOrNull()
    }

    // An Amount day: the latest hand edit, else the first reading (by its time, then edit time, then id). Second: by hand.
    private fun amountValue(dayEntries: List<GoalEntry>, readable: Set<GoalEntrySource>): Pair<Double?, Boolean> {
        val deciding = decidingEntry(dayEntries, readable)
        return deciding?.value to (deciding?.source == GoalEntrySource.Hand)
    }

    private fun wholeMinutes(windows: List<GoalWindow>): Double = floor(GoalTimeline.union(windows).sumOf { it.minutes } + 1e-9)

    // .NET's DateTime.AddMinutes: whole 100 ns ticks, the fraction cut toward zero.
    private fun minutesAsNanos(minutes: Double): Long {
        val whole = minutes.toLong()
        val ticks = whole * 600_000_000L + ((minutes - whole) * 600_000_000L).toLong()
        return ticks * 100L
    }

    /** The goal's sources prepared once, then read day by day. */
    private class Context(private val goal: Goal, private val data: GoalSourceData) {
        private val createdOn: LocalDate = goal.createdOn(data.zone)
        private val rules: List<GoalSourceRule> = GoalSources.countedFor(goal)
        private val timeRules: List<Pair<GoalSourceRule, List<GoalWindow>>> =
            rules.filter { it.automatic && it.countsTime }.map { it to windowsOf(it, data) }
        private val readable: Set<GoalEntrySource> = readableSources(goal)
        private val entriesByDay: Map<LocalDate, List<GoalEntry>> = entriesOf(goal, data).groupBy { it.day }
        private val threshold: Double? = rules
            .filter { it.automatic && it.countsTime && (it.thresholdMinutes ?: 0.0) > 0 }
            .mapNotNull { it.thresholdMinutes }
            .minOrNull()
        private val reachesDown: Boolean = goal.target.direction == GoalDirection.Reach && reachesDown(goal, data)

        fun day(day: LocalDate, today: LocalDate): GoalDayState {
            val dayEntries = entriesByDay[day] ?: emptyList()
            val sources = mutableListOf<GoalSourceType>()
            val dayStart = day.atStartOfDay()
            val dayWindow = GoalWindow(dayStart, dayStart.plusDays(1))

            // Time sources, each minute once.
            val autoWindows = mutableListOf<GoalWindow>()
            for ((rule, windows) in timeRules) {
                val clipped = windows.mapNotNull { GoalTimeline.clip(it, dayWindow) }
                if (clipped.isNotEmpty() && rule.type !in sources) {
                    sources += rule.type
                }

                autoWindows += clipped
            }

            val autoMinutes = wholeMinutes(autoWindows)

            // Readings from the goal's switched-on reading sources.
            val readings = dayEntries.filter { it.source != GoalEntrySource.Hand && it.source in readable }
            for (rule in rules) {
                val source = entrySourceOf(rule.type)
                if (rule.automatic && source != null && readings.any { it.source == source } && rule.type !in sources) {
                    sources += rule.type
                }
            }

            val hand = dayEntries.filter { it.source == GoalEntrySource.Hand }
            val backFilled = hand.any { it.isBackFill(data.zone) }

            val value: Double
            var minutes = autoMinutes
            val valueWithoutHand: Double
            val hasValue: Boolean
            val byHand: Boolean
            var handDecision: Boolean? = null // A habit's tick (true) or explicit untick (false) by hand.
            when (goal.kind) {
                GoalKind.Time -> {
                    val handTime = hand.filter { it.value > 0 }
                    val timed = handTime.mapNotNull { entry ->
                        val time = entry.time ?: return@mapNotNull null
                        val start = day.atTime(time)
                        GoalTimeline.clip(GoalWindow(start, start.plusNanos(minutesAsNanos(entry.value))), dayWindow)
                    }
                    val untimed = handTime.filter { it.time == null }.sumOf { it.value }
                    val readingTotal = readings.groupBy { it.source }.values.sumOf { group -> group.sortedWith(latestFirst).first().value }
                    minutes = wholeMinutes(autoWindows + timed) + untimed
                    value = minutes + readingTotal
                    valueWithoutHand = autoMinutes + readingTotal
                    byHand = handTime.isNotEmpty()
                    hasValue = value > 0
                }

                GoalKind.Habit -> {
                    val latest = latestHand(hand)
                    handDecision = latest?.let { it.value > 0 }
                    val autoHit = threshold != null && autoMinutes >= threshold
                    val ticked = handDecision ?: autoHit
                    value = if (ticked) 1.0 else 0.0
                    valueWithoutHand = if (autoHit) 1.0 else 0.0
                    byHand = latest != null
                    hasValue = ticked || autoMinutes > 0 || latest != null
                }

                GoalKind.Amount -> {
                    val (amount, fromHand) = amountValue(dayEntries, readable)
                    value = amount ?: 0.0
                    valueWithoutHand = value
                    byHand = fromHand
                    hasValue = amount != null
                }

                GoalKind.Milestone -> {
                    value = hand.sumOf { it.value }
                    valueWithoutHand = 0.0
                    byHand = hand.isNotEmpty()
                    hasValue = value > 0
                }
            }

            if (byHand) {
                sources += GoalSourceType.Hand
            }

            val countsDays = countsDays(goal)
            val hit = countsDays && isHit(value, hasValue, handDecision)
            val hitWithoutHand = when (goal.kind) {
                GoalKind.Habit -> handDecision == null && hit
                GoalKind.Time -> countsDays && isHit(valueWithoutHand, valueWithoutHand > 0, null)
                else -> hit && !byHand
            }

            fun state(hit: Boolean, status: GoalDayStatus, gap: Double) =
                GoalDayState(day, value, minutes, hasValue, hit, status, gap, sources, byHand, backFilled)

            if (day > today || day < createdOn) {
                return state(false, GoalDayStatus.NotDue, 0.0)
            }

            if (goal.isPausedOn(day)) {
                return state(false, GoalDayStatus.Paused, 0.0)
            }

            if (!countsDays) {
                val loose = when {
                    day == today -> GoalDayStatus.InProgress
                    hasValue -> if (byHand) GoalDayStatus.Counted else GoalDayStatus.CountedTickedItself
                    else -> GoalDayStatus.NotDue
                }
                return state(false, loose, 0.0)
            }

            if (hit && (day < today || goal.target.direction != GoalDirection.AtMost)) {
                return state(true, if (hitWithoutHand) GoalDayStatus.CountedTickedItself else GoalDayStatus.Counted, 0.0)
            }

            val gap = gap(value, minutes)
            val over = goal.target.direction == GoalDirection.AtMost && !hit
            if (day == today) {
                // An at-most day under its cap isn't hit until it is over.
                val todayStatus = if (over) GoalDayStatus.NotCounted else GoalDayStatus.InProgress
                val toGo = if (over || goal.kind == GoalKind.Habit || goal.target.direction == GoalDirection.AtLeast) gap else 0.0
                return state(false, todayStatus, toGo)
            }

            // An explicit untick by hand is the user's decision: the day reads as missed, whatever the time sources say.
            val someProgress = if (goal.kind == GoalKind.Habit) minutes > 0 && handDecision != false else hasValue && value > 0
            if (someProgress || over) {
                return state(false, GoalDayStatus.NotCounted, gap)
            }

            // Nothing on a day that was asked for: missed on a daily goal; a day an "N of the period's days" goal didn't need is not due.
            return state(false, if (goal.target.period == GoalPeriod.Day) GoalDayStatus.Missed else GoalDayStatus.NotDue, 0.0)
        }

        private fun isHit(value: Double, hasValue: Boolean, handDecision: Boolean?): Boolean {
            if (goal.kind == GoalKind.Habit) {
                return value >= 1 || handDecision == true
            }

            val amount = goal.target.amount ?: return false
            return when (goal.target.direction) {
                GoalDirection.AtMost -> value <= amount
                GoalDirection.Reach -> hasValue && (if (reachesDown) value <= amount else value >= amount)
                GoalDirection.AtLeast -> hasValue && value >= amount
            }
        }

        // "N short" / "N to go" / "N over": in minutes for a habit with a threshold, else in the goal's unit.
        private fun gap(value: Double, minutes: Double): Double {
            if (goal.kind == GoalKind.Habit) {
                return if (threshold != null) max(0.0, threshold - minutes) else 0.0
            }

            val amount = goal.target.amount ?: return 0.0
            return when (goal.target.direction) {
                GoalDirection.AtMost -> max(0.0, value - amount)
                GoalDirection.Reach -> abs(value - amount)
                GoalDirection.AtLeast -> max(0.0, amount - value)
            }
        }

        private fun windowsOf(rule: GoalSourceRule, data: GoalSourceData): List<GoalWindow> {
            if (rule.type == GoalSourceType.CalendarCategory) {
                val windows = rule.calendarCategoryId?.let { data.calendarWindows?.get(it) } ?: return emptyList()
                return GoalTimeline.union(windows)
            }

            return GoalFocusAttribution.focusWindows(data.focus, listOf(rule), data.tagsAvailable)
        }
    }
}
