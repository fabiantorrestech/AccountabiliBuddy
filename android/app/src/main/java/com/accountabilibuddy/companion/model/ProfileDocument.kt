package com.accountabilibuddy.companion.model

import java.util.UUID

/**
 * The profiles and day plan from the Windows app's versioned JSON document (ADR 7). The phone reads the parts it
 * uses; the rest of the document (Pomodoro, escalation, friction) stays on the PC for now.
 */
data class ProfileDocument(
    val schemaVersion: Int,
    val profiles: List<FocusProfile>,
    val dayPlan: DayPlan,
) {
    fun profile(id: UUID?): FocusProfile? = profiles.firstOrNull { it.id == id }

    companion object {
        /** The newest schema this app reads; a newer document needs an app update. */
        const val CURRENT_SCHEMA_VERSION = 13

        val Empty = ProfileDocument(CURRENT_SCHEMA_VERSION, emptyList(), DayPlan.Default)
    }
}
