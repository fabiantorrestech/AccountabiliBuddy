package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalAge
import com.accountabilibuddy.shared.goals.GoalCounting
import com.accountabilibuddy.shared.goals.GoalDayState
import com.accountabilibuddy.shared.goals.GoalHeat
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalReviewSuggestion
import com.accountabilibuddy.shared.goals.GoalSourceData
import com.accountabilibuddy.shared.goals.GoalStatus
import com.accountabilibuddy.shared.goals.GoalStreak
import com.accountabilibuddy.shared.goals.GoalStrip14
import com.accountabilibuddy.shared.goals.GoalStripCell
import com.accountabilibuddy.shared.goals.GoalTargetProgress
import com.accountabilibuddy.shared.goals.GoalWeekStart
import com.accountabilibuddy.shared.goals.GoalWhatItTakes
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.LocalDate

/**
 * The Kotlin results in the shape the C# checks write them (GoalFixtures.Snapshot and the computed records through
 * SyncJson.Options: every field written, nulls too), so [JsonParity] can compare them.
 */
object GoalSnapshotJson {
    fun snapshot(goal: Goal, data: GoalSourceData, today: LocalDate, weekStart: GoalWeekStart, from: LocalDate): JsonObject {
        val days = GoalCounting.days(goal, data, from, today, today)
        val span = GoalStatus.thisPeriodSpan(goal, today, weekStart, data.zone)
        val period = GoalStatus.period(goal, data, span, today)
        return buildJsonObject {
            put("days", JsonArray(days.map { day(goal, it) }))
            put("status", GoalStatus.thisPeriod(goal, data, today, weekStart).name)
            put("thisPeriod", buildJsonObject {
                put("start", span.start.toString())
                put("end", span.end.toString())
                put("required", period.required)
                put("progress", period.progress)
                put("met", period.met)
                put("skipped", period.skipped)
                put("aliveDays", period.aliveDays)
            })
            put("streak", GoalStreak.count(goal, data, today, weekStart))
            put("strip", JsonArray(GoalStrip14.forGoal(goal, data, today).map(::stripCell)))
            put("age", age(GoalAge.forGoal(goal, today, data.zone)))
            put("target", GoalStatus.target(goal, data, today)?.let(::target) ?: JsonNull)
            put("whatItTakes", GoalWhatItTakes.forGoal(goal, data, today)?.let(::whatItTakes) ?: JsonNull)
            put("review", review(GoalReviewSuggestion.forGoal(goal, data, today, weekStart)))
        }
    }

    fun day(goal: Goal, state: GoalDayState): JsonObject = buildJsonObject {
        put("day", state.day.toString())
        put("value", state.value)
        put("minutes", state.minutes)
        put("hasValue", state.hasValue)
        put("hit", state.hit)
        put("status", state.status.name)
        put("short", state.short)
        put("sources", JsonArray(state.sources.map { JsonPrimitive(it.name) }))
        put("byHand", state.byHand)
        put("backFilled", state.backFilled)
        put("heat", GoalHeat.step(goal, state))
    }

    fun stripCell(cell: GoalStripCell): JsonObject = buildJsonObject {
        put("day", cell.day.toString())
        put("kind", cell.kind.name)
        put("progress", cell.progress)
        put("isToday", cell.isToday)
    }

    fun age(age: GoalAge): JsonObject = buildJsonObject {
        put("days", age.days)
        put("pausedDays", age.pausedDays)
        put("short", age.short)
        put("tooltip", age.tooltip)
    }

    fun target(progress: GoalTargetProgress): JsonObject = buildJsonObject {
        put("current", progress.current)
        put("target", progress.target)
        put("expectedToday", progress.expectedToday)
        put("start", progress.start)
        put("met", progress.met)
        put("status", progress.status.name)
    }

    fun whatItTakes(takes: GoalWhatItTakes): JsonObject = buildJsonObject {
        put("verdict", takes.verdict.name)
        put("remaining", takes.remaining)
        put("daysLeft", takes.daysLeft)
        put("eachDay", takes.eachDay)
        put("eachWeek", takes.eachWeek)
        put("pacePerDay", takes.pacePerDay)
        put("projectedFinish", takes.projectedFinish?.toString())
        put("ownPerDay", takes.ownPerDay)
        put("morePerDay", takes.morePerDay)
        put("morePerWeek", takes.morePerWeek)
    }

    fun review(suggestion: GoalReviewSuggestion): JsonObject = buildJsonObject {
        put("choice", suggestion.choice.name)
        put("behindPeriods", suggestion.behindPeriods)
        put("periodsChecked", suggestion.periodsChecked)
        put("behindDays", suggestion.behindDays)
        put("changePercent30", suggestion.changePercent30)
        put("pacePerDay", suggestion.pacePerDay)
        put("projectedFinish", suggestion.projectedFinish?.toString())
        put("quiet", suggestion.quiet)
    }

    /** A time as the C# side writes it, or JSON null. */
    fun time(time: java.time.OffsetDateTime?): JsonPrimitive = time?.let { JsonPrimitive(GoalJson.time(it)) } ?: JsonNull
}
