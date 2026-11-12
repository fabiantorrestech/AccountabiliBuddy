package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalPeriod
import com.accountabilibuddy.shared.goals.GoalPeriods
import com.accountabilibuddy.shared.goals.GoalWeekStart
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.LocalDate

/** GoalPeriods.periodOf against the PC's GoalPeriods.PeriodOf (day, week, two weeks, month × Sunday / Monday / Saturday). */
class GoalPeriodsCheck : FixtureCheck {
    override val area = "goals"
    override val name = "periods"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            val result = runCatching {
                val span = GoalPeriods.periodOf(
                    GoalPeriod.valueOf(case.text("period")), LocalDate.parse(case.text("day")), GoalWeekStart.valueOf(case.text("weekStart")),
                    LocalDate.parse(case.text("createdOn")),
                )
                JsonParity.difference(case.expected, buildJsonObject {
                    put("start", span.start.toString())
                    put("end", span.end.toString())
                })
            }
            val detail = result.getOrElse { "threw ${it.message}" }
            report.check("$area/$name: ${case.name}", result.isSuccess && detail == null, detail ?: "")
        }
    }
}
