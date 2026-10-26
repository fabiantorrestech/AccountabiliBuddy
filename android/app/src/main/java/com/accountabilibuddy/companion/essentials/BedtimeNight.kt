package com.accountabilibuddy.companion.essentials

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.model.LocalWindow
import java.time.LocalDateTime

/** The night the bedtime essentials cover: from bedtime until the wake time, as the sleep settings and day plan say. */
object BedtimeNight {
    /** The night [now] is in, or null outside it or with sleep mode off. */
    fun current(app: CompanionApp, now: LocalDateTime = LocalDateTime.now()): LocalWindow? {
        val night = around(app, now) ?: return null
        return night.takeIf { !now.isBefore(it.start) && now.isBefore(it.end) }
    }

    /** The next bedtime still ahead of [now], or null with sleep mode off. */
    fun nextStart(app: CompanionApp, now: LocalDateTime = LocalDateTime.now()): LocalDateTime? =
        around(app, now)?.start?.takeIf { it.isAfter(now) }

    private fun around(app: CompanionApp, now: LocalDateTime): LocalWindow? {
        val state = app.store.state.value
        if (!state.sleep.enabled) return null
        val night = state.sleep.toSchedule(state.document.dayPlan).nightAround(now)
        return night.takeIf { it.end.isAfter(it.start) }
    }
}
