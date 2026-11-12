package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalReviewRules
import com.accountabilibuddy.shared.goals.GoalReviewSettings
import com.accountabilibuddy.shared.goals.GoalWeekStart
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.time.DayOfWeek
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * Round 2 R6b3: the phone's weekly review due rule ([GoalReviewRules.reviewDay], [GoalReviewRules.dueAt], [GoalReviewRules.weekOf],
 * the PC form of [GoalReviewRules.weekText]) against the PC's `GoalReviewService` run by AppChecks in that PC's zone.
 */
class GoalsUiReviewDueCheck : FixtureCheck {
    override val area = "goals-ui"
    override val name = "review-due"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val input = case.input
                val zone = ZoneId.of(case.text("zoneId"))
                val now = OffsetDateTime.parse(case.text("now")).toInstant()
                val day = input["day"]?.jsonPrimitive?.contentOrNull?.let { DayOfWeek.valueOf(it.uppercase()) }
                val time = LocalTime.parse(case.text("time"))
                val weekStart = GoalWeekStart.valueOf(case.text("weekStart"))
                val reviewDay = GoalReviewRules.reviewDay(GoalReviewSettings(day = day, time = time), weekStart)
                val due = GoalReviewRules.dueAt(now, reviewDay, time, zone)
                val week = GoalReviewRules.weekOf(due, weekStart, zone)
                val expected = case.expected.jsonObject
                val expectedDue = OffsetDateTime.parse(expected.getValue("due").jsonPrimitive.content)
                when {
                    due.toInstant() != expectedDue.toInstant() || due.offset != expectedDue.offset -> "due: C# $expectedDue, kotlin $due"
                    week.toString() != expected.getValue("week").jsonPrimitive.content -> "week: C# ${expected["week"]}, kotlin $week"
                    GoalReviewRules.weekText(week) != expected.getValue("weekText").jsonPrimitive.content -> "weekText: C# ${expected["weekText"]}, kotlin ${GoalReviewRules.weekText(week)}"
                    else -> null
                }
            }
        }
    }
}
