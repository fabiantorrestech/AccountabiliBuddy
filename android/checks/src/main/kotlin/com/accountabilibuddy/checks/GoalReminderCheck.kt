package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalWeekday
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject
import java.time.DayOfWeek

/**
 * A goal reminder's JSON (weekdays by name) reads and writes back exactly as the PC wrote it, and an older reminder with
 * numbered weekdays (.NET's DayOfWeek numbers, Sunday 0) still reads.
 */
class GoalReminderCheck : FixtureCheck {
    override val area = "goals"
    override val name = "reminder"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            val result = runCatching {
                val text = case.text("json")
                val reminder = GoalJson.readReminder(Json.parseToJsonElement(text))
                val written = GoalJson.writeReminder(reminder)
                val expected = case.expected.jsonObject
                JsonParity.difference(expected.getValue("weekdays"), JsonArray(reminder.weekdays.orEmpty().map { JsonPrimitive(it.name) }), "$.weekdays")
                    ?: JsonParity.difference(Json.parseToJsonElement(expected.getValue("json").let { (it as JsonPrimitive).content }), written, "$.json")
                    ?: (if (GoalJson.text(written) == (expected.getValue("json") as JsonPrimitive).content) null else "text differs: $written")
            }
            val detail = result.getOrElse { "threw ${it.message}" }
            report.check("$area/$name: ${case.name}", result.isSuccess && detail == null, detail ?: "")

            val numbered = runCatching {
                val old = case.text("json").replace("[\"Monday\",\"Sunday\"]", "[1,0]")
                GoalJson.readReminder(Json.parseToJsonElement(old)).weekdays
            }.getOrNull()
            report.check(
                "$area/$name: ${case.name} (older numbered weekdays read, Sunday = 0)",
                numbered == listOf(GoalWeekday.Monday, GoalWeekday.Sunday) && numbered[0].dayOfWeek == DayOfWeek.MONDAY && numbered[1].dayOfWeek == DayOfWeek.SUNDAY,
                "$numbered",
            )
        }
    }
}
