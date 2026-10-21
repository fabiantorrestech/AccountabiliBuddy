package com.accountabilibuddy.companion.data

import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.DayBarSettings
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.PendingDayPlan
import com.accountabilibuddy.companion.model.ProfileDocument
import com.accountabilibuddy.companion.model.SleepSettings
import com.accountabilibuddy.shared.FocusDay
import java.time.Instant

/**
 * Everything the phone knows: the profiles and calendar events imported from the PC, sleep and day-bar settings,
 * the sleep Health Connect measured, and the phone's own choices (notification, bedtime reminder).
 * While a day plan edit waits for the PC ([pendingPlan], ADR 19), [document] shows the edited plan and [pcDayPlan]
 * keeps the PC's. [focusDay] is today's focus as the linked PC last sent it (ADR 21).
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
    /** The PC's own day plan as last received (the document shows a waiting edit instead). */
    val pcDayPlan: DayPlan = document.dayPlan,
    /** The PC's day plan version and last edit time, from its settings push (null from older PCs). */
    val pcPlanVersion: String? = null,
    val pcPlanEditedAt: Instant? = null,
    val pendingPlan: PendingDayPlan? = null,
    val focusDay: FocusDay? = null,
) {
    val hasImport: Boolean get() = importedFrom != null
}
