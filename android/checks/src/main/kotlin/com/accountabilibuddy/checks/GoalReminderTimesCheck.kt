package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalReminderTimes
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonPrimitive
import java.time.LocalDate
import java.time.ZoneId
import java.time.ZoneOffset

/** Round 2 R6b3: the phone's [GoalReminderTimes] (on, nudgeOn; clock changes included) against the PC's `GoalReminderTimes`. */
class GoalReminderTimesCheck : FixtureCheck {
    override val area = "goals"
    override val name = "reminder-times"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val input = case.input
                val zone: ZoneId = input["zoneId"]?.jsonPrimitive?.content?.let { ZoneId.of(it) }
                    ?: ZoneOffset.ofTotalSeconds(input.getValue("zoneOffsetMinutes").jsonPrimitive.int * 60)
                val reminder = GoalJson.readReminder(input.getValue("reminder"))
                val day = LocalDate.parse(case.text("day"))
                JsonParity.difference(case.expected, buildJsonObject {
                    put("at", GoalSnapshotJson.time(GoalReminderTimes.on(reminder, day, zone)))
                    put("nudge", GoalSnapshotJson.time(GoalReminderTimes.nudgeOn(day, zone)))
                })
            }
        }
    }
}
