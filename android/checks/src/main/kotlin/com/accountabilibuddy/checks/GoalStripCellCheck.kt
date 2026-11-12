package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalDayState
import com.accountabilibuddy.shared.goals.GoalDayStatus
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalStrip14
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.double
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.time.LocalDate

/** GoalStrip14.cellOf for every day status × hit × today or not × three amounts, per kind of goal, against the PC's CellOf. */
class GoalStripCellCheck : FixtureCheck {
    override val area = "goals"
    override val name = "strip-cell"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val goal = GoalJson.readGoal(case.input.getValue("goal"))
                val today = LocalDate.parse(case.text("today"))
                val cells = case.input.getValue("states").jsonArray.map { item ->
                    val state = item.jsonObject
                    fun field(key: String) = state.getValue(key).jsonPrimitive
                    val dayState = GoalDayState(
                        LocalDate.parse(field("day").content), field("value").double, field("minutes").double, field("hasValue").boolean, field("hit").boolean,
                        GoalDayStatus.valueOf(field("status").content), 0.0, emptyList(), byHand = false, backFilled = false,
                    )
                    GoalSnapshotJson.stripCell(GoalStrip14.cellOf(goal, dayState, today))
                }
                JsonParity.difference(case.expected, JsonArray(cells))
            }
        }
    }
}
