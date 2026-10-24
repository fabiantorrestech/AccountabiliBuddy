package com.accountabilibuddy.companion.data

import com.accountabilibuddy.companion.data.JsonValues.bool
import com.accountabilibuddy.companion.data.JsonValues.int
import com.accountabilibuddy.companion.data.JsonValues.obj
import com.accountabilibuddy.companion.data.JsonValues.string
import com.accountabilibuddy.companion.model.BarRow
import com.accountabilibuddy.companion.model.BarRowChoice
import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.DayBarRange
import com.accountabilibuddy.companion.model.DayBarSettings
import com.accountabilibuddy.companion.model.SleepSettings
import com.accountabilibuddy.companion.model.SleepTimes
import com.accountabilibuddy.companion.model.WakeOnce
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.LocalTime

/**
 * The Windows app's own settings documents, in its JSON shape (camelCase, enums as numbers, times "HH:mm:ss"), so
 * they read the same from a backup as from the phone's own storage.
 */
object SettingsJson {
    fun sleep(json: JsonObject): SleepSettings {
        val default = SleepSettings.Default
        return SleepSettings(
            enabled = json.bool("enabled", default.enabled),
            sameEveryNight = json.bool("sameEveryNight", default.sameEveryNight),
            weeknights = times(json.obj("weeknights")) ?: default.weeknights,
            weekends = times(json.obj("weekends")) ?: default.weekends,
            goalHours = (json["goalHours"] as? JsonPrimitive)?.doubleOrNull ?: default.goalHours,
            wakeOnce = wakeOnce(json.obj("wakeOnce")),
            smartWorkdays = json.bool("smartWorkdays", default.smartWorkdays),
            smartOffDays = json.bool("smartOffDays", default.smartOffDays),
            minHours = (json["minHours"] as? JsonPrimitive)?.doubleOrNull ?: default.minHours,
            maxHours = (json["maxHours"] as? JsonPrimitive)?.doubleOrNull ?: default.maxHours,
        )
    }

    fun sleepJson(settings: SleepSettings): JsonObject = buildJsonObject {
        put("enabled", settings.enabled)
        put("sameEveryNight", settings.sameEveryNight)
        put("weeknights", timesJson(settings.weeknights))
        put("weekends", timesJson(settings.weekends))
        put("goalHours", settings.goalHours)
        put("smartWorkdays", settings.smartWorkdays)
        put("smartOffDays", settings.smartOffDays)
        put("minHours", settings.minHours)
        put("maxHours", settings.maxHours)
        settings.wakeOnce?.let { once ->
            put("wakeOnce", buildJsonObject {
                put("morning", once.morning.toString())
                put("wake", time(once.wake))
                if (once.overridesAlarm) put("overridesAlarm", true)
            })
        }
    }

    // A wake time changed on the PC for one night; null when there's none or it can't be read.
    private fun wakeOnce(json: JsonObject?): WakeOnce? {
        val morning = json?.string("morning") ?: return null
        val wake = json.string("wake") ?: return null
        return runCatching { WakeOnce(JsonValues.date(morning), JsonValues.time(wake), json.bool("overridesAlarm", false)) }.getOrNull()
    }

    fun dayBar(json: JsonObject): DayBarSettings {
        val default = DayBarSettings.Default
        return DayBarSettings(
            range = runCatching { JsonValues.enumValue<DayBarRange>(json["range"], "day bar range") }.getOrDefault(default.range),
            dayStart = json.string("dayStart")?.let(JsonValues::time) ?: default.dayStart,
            dayEnd = json.string("dayEnd")?.let(JsonValues::time) ?: default.dayEnd,
            rows = (json["rows"] as? JsonArray)?.mapNotNull { element ->
                val row = (element as? JsonObject)?.int("row")?.let { BarRow.entries.getOrNull(it) } ?: return@mapNotNull null
                BarRowChoice(row, (element as JsonObject).bool("shown", true))
            },
            calendarsOff = (json["calendarsOff"] as? JsonArray)?.mapNotNull { (it as? JsonPrimitive)?.contentOrNull }?.toSet().orEmpty(),
        )
    }

    fun dayBarJson(settings: DayBarSettings): JsonObject = buildJsonObject {
        put("show", true)
        put("range", settings.range.ordinal)
        put("dayStart", time(settings.dayStart))
        put("dayEnd", time(settings.dayEnd))
        settings.rows?.let { rows ->
            put("rows", buildJsonArray {
                rows.forEach { choice -> add(buildJsonObject { put("row", choice.row.ordinal); put("shown", choice.shown) }) }
            })
        }
        put("calendarsOff", buildJsonArray { settings.calendarsOff.sorted().forEach { add(JsonPrimitive(it)) } })
    }

    fun events(json: JsonArray): List<CalendarEvent> = json.mapNotNull { element ->
        val e = element as? JsonObject ?: return@mapNotNull null
        runCatching {
            CalendarEvent(
                id = e.string("id") ?: "",
                calendarId = e.string("calendarId") ?: "",
                title = e.string("title") ?: "(no title)",
                localStart = JsonValues.dateTime(e.string("localStart")!!),
                localEnd = JsonValues.dateTime(e.string("localEnd")!!),
                isAllDay = e.bool("isAllDay", false),
                color = e.string("color")?.let(::argb),
            )
        }.getOrNull()
    }

    private fun times(json: JsonObject?): SleepTimes? {
        val bed = json?.string("bedtime") ?: return null
        val wake = json.string("wake") ?: return null
        return SleepTimes(JsonValues.time(bed), JsonValues.time(wake))
    }

    private fun timesJson(times: SleepTimes): JsonObject = buildJsonObject {
        put("bedtime", time(times.bedtime))
        put("wake", time(times.wake))
    }

    // "#0B8043" (or "#AA0B8043") as an opaque ARGB int; null when it isn't a colour.
    private fun argb(hex: String): Int? {
        val digits = hex.removePrefix("#")
        val value = digits.toLongOrNull(16) ?: return null
        return when (digits.length) {
            6 -> (0xFF000000L or value).toInt()
            8 -> value.toInt()
            else -> null
        }
    }

    private fun time(value: LocalTime): String = "%02d:%02d:%02d".format(value.hour, value.minute, value.second)
}
