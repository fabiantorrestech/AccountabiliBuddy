package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalReviewAdjustment
import com.accountabilibuddy.shared.goals.GoalReviewChoice
import com.accountabilibuddy.shared.goals.GoalReviewSuggestion
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.time.LocalDate

/** Round 2 R6b3: "Adjust the target"'s pre-filled numbers ([GoalReviewAdjustment]) against the PC's `GoalReviewAdjustment.For`. */
class GoalAdjustCheck : FixtureCheck {
    override val area = "goals"
    override val name = "adjust"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val goal = GoalJson.readGoal(case.input.getValue("goal"))
                val today = LocalDate.parse(case.text("today"))
                val adjustment = GoalReviewAdjustment.forGoal(goal, suggestion(case.input.getValue("suggestion").jsonObject), today)
                val actual = adjustment?.let {
                    buildJsonObject {
                        put("targetDate", it.targetDate?.toString())
                        put("amount", GoalSnapshotJson.num(it.amount))
                        put("weeklyDays", it.weeklyDays)
                    }
                } ?: JsonNull
                JsonParity.difference(case.expected, actual)
            }
        }
    }

    private fun suggestion(json: JsonObject): GoalReviewSuggestion = GoalReviewSuggestion(
        choice = GoalReviewChoice.valueOf(json.getValue("choice").jsonPrimitive.content),
        behindPeriods = json.getValue("behindPeriods").jsonPrimitive.int,
        periodsChecked = json.getValue("periodsChecked").jsonPrimitive.int,
        behindDays = json.getValue("behindDays").jsonPrimitive.int,
        changePercent30 = json["changePercent30"]?.jsonPrimitive?.doubleOrNull,
        pacePerDay = json["pacePerDay"]?.jsonPrimitive?.doubleOrNull,
        projectedFinish = json["projectedFinish"]?.jsonPrimitive?.contentOrNull?.let(LocalDate::parse),
        quiet = json.getValue("quiet").jsonPrimitive.boolean,
    )
}
