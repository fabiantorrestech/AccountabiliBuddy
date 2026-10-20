package com.accountabilibuddy.companion.data

import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.DayBarSettings
import com.accountabilibuddy.companion.model.ProfileDocument
import com.accountabilibuddy.companion.model.SleepSettings
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject

/**
 * What an import brought: the profiles (always), and when the backup had them, sleep and day-bar settings and the
 * in-app calendar events. The raw JSON is kept so the phone stores exactly what the PC wrote.
 */
data class ImportedData(
    val document: ProfileDocument,
    val profilesJson: JsonObject,
    val sleep: SleepSettings?,
    val sleepJson: JsonObject?,
    val dayBar: DayBarSettings?,
    val dayBarJson: JsonObject?,
    val events: List<CalendarEvent>,
    val eventsJson: JsonArray?,
    val sourceDescription: String,
)
