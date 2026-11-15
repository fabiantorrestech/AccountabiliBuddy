package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.util.Locale
import kotlin.math.floor
import kotlin.math.max
import kotlin.math.pow

/**
 * The goals' words and numbers (brief §3.11, change note §4a), the PC's `GoalTexts` (App, `Services/Goals`) rule for rule: "Wed
 * 11/11" dates (the year only when it differs), whole minutes under an hour then "1h 25m", values in the goal's unit, a goal's
 * target line ("15 min a day", "3× a week", "175 lb by Sun 3/1/2027") and its kind ("daily habit"). Goal names, notes and
 * reasons never go through here into a log.
 */
object GoalTexts {
    // .NET's invariant short day names, Sunday first (as GoalAge).
    private val DAY_NAMES = listOf("Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat")

    /** "Wed", .NET's invariant "ddd". */
    fun weekday(day: LocalDate): String = DAY_NAMES[day.dayOfWeek.value % 7]

    /** "Wed 11/11", or "Sun 1/31/2027" when the year differs from [today]'s (§3.11). */
    fun date(day: LocalDate, today: LocalDate): String =
        if (day.year == today.year) "${weekday(day)} ${day.monthValue}/${day.dayOfMonth}" else "${weekday(day)} ${day.monthValue}/${day.dayOfMonth}/${day.year}"

    /** "12 min", "2h", "1h 10m" (whole minutes, rounded down). */
    fun minutes(minutes: Double): String = GoalWhatItTakes.minutesText(floor(max(0.0, minutes) + 1e-9).toInt())

    /** A value in the goal's unit: "1h 10m", "6,240", "182.4 lb", "4". */
    fun value(value: Double, unit: GoalUnit): String = when (unit) {
        GoalUnit.Minutes -> minutes(value)
        GoalUnit.Lb -> "${number(value, 1)} lb"
        GoalUnit.Kg -> "${number(value, 1)} kg"
        GoalUnit.Steps -> number(value, 0)
        GoalUnit.YesNo -> if (value > 0) "Done" else "Not yet"
        GoalUnit.Count -> number(value, 1)
    }

    /** A target in the goal's unit, short where it stands after a "/" ("2h", "8k", "15 min", "7"). */
    fun shortValue(value: Double, unit: GoalUnit): String =
        if (unit == GoalUnit.Steps && value >= 1000) "${number(value / 1000, 1)}k" else value(value, unit)

    /**
     * Whether the goal is an "at most" limit with an amount ("at most 1h a day"): a Time or Amount goal, never a habit (FX-C). Its words
     * read as room left under the limit, never as "to go" or "short".
     */
    fun isLimit(goal: Goal): Boolean = goal.kind != GoalKind.Habit && goal.target.direction == GoalDirection.AtMost && goal.target.amount != null

    /**
     * How much of an at-most goal's limit is used, in words that start lower case: "15 min left", "at the limit", "over by 10 min".
     * The limit is the goal's amount (a day's) unless [limit] says otherwise (a period's prorated cap, `GoalPeriodResult.required`);
     * [used] is the day's or the period's value.
     */
    fun limitLeft(goal: Goal, used: Double, limit: Double? = null): String {
        val cap = limit ?: goal.target.amount ?: 0.0
        val unit = goal.target.unit
        return when {
            used > cap -> "over by ${value(used - cap, unit)}"
            used < cap -> "${value(cap - used, unit)} left"
            else -> "at the limit"
        }
    }

    /** [limitLeft] starting with a capital: "15 min left", "At the limit", "Over by 10 min". */
    fun limitLeftSentence(goal: Goal, used: Double, limit: Double? = null): String = limitLeft(goal, used, limit).replaceFirstChar { it.uppercaseChar() }

    /** A limit goal's period status in its own words: "within the limit" / "over the limit"; null for any other status or goal (FX-C). */
    fun limitStatus(goal: Goal, status: GoalPeriodStatus): String? {
        if (!isLimit(goal)) return null
        return when (status) {
            GoalPeriodStatus.OnTrack -> "within the limit"
            GoalPeriodStatus.Behind -> "over the limit"
            else -> null
        }
    }

    /** Whether the goal has a switched-on Beeminder source (its day values can be replaced by a hand entry). */
    fun readsBeeminder(goal: Goal): Boolean = GoalSources.countedFor(goal).any { it.automatic && it.type == GoalSourceType.Beeminder }

    /**
     * What a hand entry does to a Beeminder day (FX-C, owner-visible): "Replaces Beeminder's 1h 30m", or "Replaces Beeminder for this day"
     * with no reading; null when the goal reads no Beeminder, is a habit (its tick has its own button) or is Deep work.
     */
    fun replacesBeeminder(goal: Goal, state: GoalDayState): String? {
        if (goal.isDeepWork || goal.kind == GoalKind.Habit || !readsBeeminder(goal)) return null
        val reading = state.beeminder
        return if (reading != null) "Replaces Beeminder's ${value(reading, goal.target.unit)}" else "Replaces Beeminder for this day"
    }

    /** A habit with a threshold that Beeminder ticked: its focus minutes are under the threshold, so "0 of 15 min" would be wrong (FX-C). */
    fun tickedByBeeminder(state: GoalDayState, threshold: Double?): Boolean =
        state.hit && !state.byHand && state.beeminder != null && threshold != null && state.minutes < threshold

    /** The sources editor's note when a milestone counts both Beeminder and its tasks. */
    const val BEEMINDER_AND_TASKS_NOTE = "Beeminder and Milestone tasks may count the same work twice."

    /**
     * A whole or one-decimal number with group separators ("6,240", "182.4"): .NET's `Math.Round` (to even, scaled) and its
     * "#,0" / "#,0.#" formats with the invariant culture.
     */
    fun number(value: Double, decimals: Int): String {
        val scale = 10.0.pow(decimals)
        val rounded = Math.rint(value * scale) / scale
        val whole = decimals == 0 || rounded == floor(rounded)
        // .NET keeps the sign of a negative value that rounds to zero ("-0"), as Java's formatter does.
        return if (whole) String.format(Locale.ROOT, "%,.0f", rounded) else String.format(Locale.ROOT, "%,.1f", rounded)
    }

    /**
     * The goal's target in a few words, as the list's "↳" line and the detail's subtitle start: "15 min a day", "2h a day", "3× a
     * week", "8,000 a day", "175 lb by Sun 3/1/2027", "50h by Sun 1/31"; "tracking only" when nothing is asked.
     */
    fun targetLine(goal: Goal, today: LocalDate): String {
        val target = goal.target
        val period = periodPart(goal)
        val amount = target.targetAmount
        val longTerm = if (amount != null) {
            val date = target.targetDate
            if (date != null) "${value(amount, target.unit)} by ${date(date, today)}" else "Reach ${value(amount, target.unit)}"
        } else {
            null
        }
        if (period != null && longTerm != null) {
            return if (goal.kind == GoalKind.Amount) longTerm else period
        }

        return period ?: longTerm ?: "tracking only"
    }

    /** "daily habit", "weekly habit", "time goal", "amount", "milestone", "built in". */
    fun kindText(goal: Goal): String {
        if (goal.isDeepWork) {
            return "built in"
        }

        return when (goal.kind) {
            GoalKind.Habit -> when (goal.target.period) {
                GoalPeriod.Day -> "daily habit"
                GoalPeriod.Week -> "weekly habit"
                GoalPeriod.TwoWeeks -> "two-week habit"
                GoalPeriod.Month -> "monthly habit"
            }
            GoalKind.Time -> "time goal"
            GoalKind.Amount -> "amount"
            GoalKind.Milestone -> "milestone"
        }
    }

    /** "a day", "a week", "every 2 weeks", "a month". */
    fun per(period: GoalPeriod): String = when (period) {
        GoalPeriod.Day -> "a day"
        GoalPeriod.Week -> "a week"
        GoalPeriod.TwoWeeks -> "every 2 weeks"
        GoalPeriod.Month -> "a month"
    }

    /** "this week", "these two weeks", "this month": the This period tile's caption. */
    fun periodName(period: GoalPeriod): String = when (period) {
        GoalPeriod.Day, GoalPeriod.Week -> "this week"
        GoalPeriod.TwoWeeks -> "these two weeks"
        GoalPeriod.Month -> "this month"
    }

    /** "1 day", "3 days". */
    fun plural(count: Int, word: String): String = if (count == 1) "1 $word" else "$count ${word}s"

    /** A habit's "ticks itself at" minutes (the smallest threshold of its switched-on time sources), or null. */
    fun threshold(goal: Goal): Double? =
        goal.sources.filter { it.automatic && it.countsTime && (it.thresholdMinutes ?: 0.0) > 0 }.mapNotNull { it.thresholdMinutes }.minOrNull()

    private fun periodPart(goal: Goal): String? {
        val target = goal.target
        if (goal.kind == GoalKind.Habit) {
            val threshold = threshold(goal)
            if (target.period == GoalPeriod.Day) {
                return if (threshold != null) "${minutes(threshold)} a day" else "every day"
            }

            val times = target.weeklyDays ?: max(1.0, Math.rint(target.amount ?: 1.0)).toInt()
            return "$times× ${per(target.period)}"
        }

        val amount = target.amount ?: return null
        val days = target.weeklyDays
        if (days != null) {
            return "${value(amount, target.unit)} on $days days ${per(target.period)}"
        }

        val prefix = if (target.direction == GoalDirection.AtMost) "at most " else ""
        return "$prefix${value(amount, target.unit)} ${per(target.period)}"
    }
}
