package com.accountabilibuddy.companion.data

import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.DayBarSettings
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.ProfileDocument
import com.accountabilibuddy.companion.model.SleepSettings

/**
 * Everything the phone knows: the profiles and calendar events imported from the PC, sleep and day-bar settings,
 * the sleep Health Connect measured, and the phone's own choices (notification, bedtime reminder).
 */
data class CompanionState(
    val document: ProfileDocument = ProfileDocument.Empty,
    val events: List<CalendarEvent> = emptyList(),
    val sleep: SleepSettings = SleepSettings.Default,
    val dayBar: DayBarSettings = DayBarSettings.Default,
    val importedFrom: String? = null,
    val importedAt: String? = null,
    val slept: List<LocalWindow> = emptyList(),
    val timelineNotificationOn: Boolean = true,
    val bedtimeReminderOn: Boolean = true,
    val lastReminderNight: String? = null,
) {
    val hasImport: Boolean get() = importedFrom != null
}
