package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.time.ZoneId

/**
 * On track / Behind / Not due yet (brief §5.5) per kind, the PC's `GoalStatus` rule for rule (see its summary and Q367, Q386):
 * daily goals by this week's missed days against [GoalTarget.missesAllowed]; "N of the period's days" by the live days left; an
 * amount per period against its pace line (at most: Behind once over); a long-term target (or a level to reach with one) by the
 * Target tile; a level per period with no long-term target by the period. A goal paused today is [GoalPeriodStatus.Paused].
 * Deep work's weekly target is read the same way: pass the goal with [Goal.target] set to its [Goal.secondTarget].
 */
object GoalStatus {
    /** The span the "This period" tile shows: the week for a daily goal, else the goal's own period. */
    fun thisPeriodSpan(goal: Goal, today: LocalDate, weekStart: GoalWeekStart, zone: ZoneId): GoalPeriodSpan =
        if (goal.target.period == GoalPeriod.Day) {
            GoalPeriods.weekOf(today, weekStart)
        } else {
            GoalPeriods.periodOf(goal.target.period, today, weekStart, goal.createdOn(zone))
        }

    /** "This period" for the goal today. */
    fun thisPeriod(goal: Goal, data: GoalSourceData, today: LocalDate, weekStart: GoalWeekStart): GoalPeriodStatus {
        if (goal.isPausedOn(today)) {
            return GoalPeriodStatus.Paused
        }

        if (today < goal.createdOn(data.zone)) {
            return GoalPeriodStatus.NotDueYet
        }

        val span = thisPeriodSpan(goal, today, weekStart, data.zone)
        val days = GoalCounting.history(goal, data, today, span.start)
        if (GoalCounting.countsDays(goal) && goal.target.period == GoalPeriod.Day) {
            val missed = days.values.count { it.day < today && (it.status == GoalDayStatus.Missed || it.status == GoalDayStatus.NotCounted) }
            return if (missed > (goal.target.missesAllowed ?: 0)) GoalPeriodStatus.Behind else GoalPeriodStatus.OnTrack
        }

        if (GoalCounting.countsDays(goal)) {
            val result = evaluate(goal, data, span, days, today, false)
            val needed = result.required - result.progress
            if (needed <= 0) {
                return GoalPeriodStatus.OnTrack
            }

            val todayHit = days[today]?.hit == true
            val daysLeft = span.days().count { it > today && goal.isAliveOn(it, data.zone) } +
                (if (goal.isAliveOn(today, data.zone) && !todayHit) 1 else 0)
            if (daysLeft < needed) {
                return GoalPeriodStatus.Behind
            }

            return if (result.progress == 0.0 && daysLeft > needed) GoalPeriodStatus.NotDueYet else GoalPeriodStatus.OnTrack
        }

        if (goal.target.direction == GoalDirection.Reach && goal.target.targetAmount == null && goal.target.amount != null) {
            // A level per period with no long-term target: met once the period's latest value reaches it; nothing to be behind on.
            val met = evaluate(goal, data, span, days, today, GoalCounting.reachesDown(goal, data)).met
            return if (met) GoalPeriodStatus.OnTrack else GoalPeriodStatus.NotDueYet
        }

        if (goal.target.amount == null || goal.target.direction == GoalDirection.Reach) {
            val target = target(goal, data, today)
            return when {
                target == null -> GoalPeriodStatus.NotDueYet
                target.met -> GoalPeriodStatus.OnTrack
                target.expectedToday == null -> GoalPeriodStatus.NotDueYet
                else -> target.status
            }
        }

        val period = evaluate(goal, data, span, days, today, false)
        if (goal.target.direction == GoalDirection.AtMost) {
            return if (period.met) GoalPeriodStatus.OnTrack else GoalPeriodStatus.Behind
        }

        if (period.met) {
            return GoalPeriodStatus.OnTrack
        }

        val elapsed = span.days().count { it < today && goal.isAliveOn(it, data.zone) }
        val expected = if (period.aliveDays == 0) 0.0 else period.required * elapsed / period.aliveDays
        if (expected <= 0 && period.progress <= 0) {
            return GoalPeriodStatus.NotDueYet
        }

        return if (period.progress >= expected) GoalPeriodStatus.OnTrack else GoalPeriodStatus.Behind
    }

    /** How the goal did in [span] (days after [today] count as live but not done). */
    fun period(goal: Goal, data: GoalSourceData, span: GoalPeriodSpan, today: LocalDate): GoalPeriodResult {
        val last = if (span.end < today) span.end else today
        val days = if (span.start > last) emptyMap() else GoalCounting.days(goal, data, span.start, last, today).associateBy { it.day }
        return evaluate(goal, data, span, days, today, GoalCounting.reachesDown(goal, data))
    }

    /** The Target tile; null when the goal has no long-term amount. */
    fun target(goal: Goal, data: GoalSourceData, today: LocalDate): GoalTargetProgress? {
        val target = goal.target.targetAmount ?: return null
        val created = goal.createdOn(data.zone)
        val live = GoalCounting.history(goal, data, today).values
            .filter { goal.isAliveOn(it.day, data.zone) }
            .sortedBy { it.day }
        val date = goal.target.targetDate
        var fraction: Double? = null
        if (date != null) {
            val span = (date.toEpochDay() - created.toEpochDay() + 1).toInt()
            val elapsed = (today.toEpochDay() - created.toEpochDay() + 1).toInt()
            fraction = if (span <= 0) 1.0 else (elapsed.toDouble() / span).coerceIn(0.0, 1.0)
        }

        val current: Double
        var start: Double? = null
        val expected: Double?
        val met: Boolean
        val onPace: Boolean
        if (goal.target.direction == GoalDirection.Reach) {
            val down = GoalCounting.reachesDown(goal, data)
            start = GoalCounting.firstValue(goal, data)
            val latest = live.lastOrNull { it.hasValue }
            current = latest?.value ?: start ?: 0.0
            val from = start ?: current
            expected = fraction?.let { from + ((target - from) * it) }
            met = start != null && (if (down) current <= target else current >= target)
            onPace = expected != null && (if (down) current <= expected else current >= expected)
        } else {
            current = live.sumOf { it.value }
            expected = fraction?.let { target * it }
            met = goal.target.direction == GoalDirection.AtLeast && current >= target
            onPace = expected != null && (if (goal.target.direction == GoalDirection.AtMost) current <= expected else current >= expected)
        }

        val status = when {
            met -> GoalPeriodStatus.OnTrack
            date == null -> GoalPeriodStatus.NotDueYet
            today > date -> GoalPeriodStatus.Behind
            onPace -> GoalPeriodStatus.OnTrack
            else -> GoalPeriodStatus.Behind
        }
        return GoalTargetProgress(current, target, expected, start, met, status)
    }

    internal fun evaluate(
        goal: Goal,
        data: GoalSourceData,
        span: GoalPeriodSpan,
        days: Map<LocalDate, GoalDayState>,
        today: LocalDate,
        reachesDown: Boolean,
    ): GoalPeriodResult {
        val aliveDays = span.days().count { goal.isAliveOn(it, data.zone) }
        val done = span.days()
            .filter { it <= today && goal.isAliveOn(it, data.zone) }
            .mapNotNull { days[it] }

        if (GoalCounting.countsDays(goal)) {
            val asked = if (goal.target.period == GoalPeriod.Day) {
                span.length
            } else {
                goal.target.weeklyDays ?: maxOf(1.0, Math.rint(goal.target.amount ?: 1.0)).toInt()
            }
            val required = minOf(asked, aliveDays)
            val hits = done.count { it.hit }
            return GoalPeriodResult(span, required.toDouble(), hits.toDouble(), required > 0 && hits >= required, required == 0, aliveDays)
        }

        val amount = goal.target.amount
        if (amount == null || aliveDays == 0) {
            return GoalPeriodResult(span, 0.0, done.sumOf { it.value }, false, true, aliveDays)
        }

        val requiredAmount = amount * aliveDays / span.length
        if (goal.target.direction == GoalDirection.Reach) {
            val latest = done.lastOrNull { it.hasValue }
            val reached = latest != null && (if (reachesDown) latest.value <= amount else latest.value >= amount)
            return GoalPeriodResult(span, amount, latest?.value ?: 0.0, reached, false, aliveDays)
        }

        val progress = done.sumOf { it.value }
        val met = if (goal.target.direction == GoalDirection.AtMost) progress <= requiredAmount else progress >= requiredAmount
        return GoalPeriodResult(span, requiredAmount, progress, met, false, aliveDays)
    }
}
