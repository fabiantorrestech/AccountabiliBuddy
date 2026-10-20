package com.accountabilibuddy.companion.model

import java.time.LocalTime

/** Which stretch of the day the bar shows, as in the Windows app's "daybar" document. */
data class DayBarSettings(val range: DayBarRange, val dayStart: LocalTime, val dayEnd: LocalTime) {
    companion object {
        val Default = DayBarSettings(DayBarRange.FitToDay, LocalTime.of(6, 0), LocalTime.MIDNIGHT)
    }
}
