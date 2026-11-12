package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalCounting
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.LocalDate

/** Clock changes (America/Chicago): a day's minutes from the synced shape, as the PC (and its Deep work card) count them (Q384). */
class GoalDstCheck : FixtureCheck {
    override val area = "goals"
    override val name = "dst"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            val result = runCatching {
                val world = GoalFixtureWorld(case.input)
                val state = GoalCounting.day(world.goal, world.data(), LocalDate.parse(case.text("day")), world.today)
                JsonParity.difference(case.expected, buildJsonObject { put("minutes", state.minutes) })
            }
            val detail = result.getOrElse { "threw ${it.message}" }
            report.check("$area/$name: ${case.name}", result.isSuccess && detail == null, detail ?: "")
        }
    }
}
