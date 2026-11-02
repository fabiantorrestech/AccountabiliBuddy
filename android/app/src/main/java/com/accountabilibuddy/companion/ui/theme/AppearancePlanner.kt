package com.accountabilibuddy.companion.ui.theme

import java.time.LocalTime
import java.time.ZoneId
import java.time.ZonedDateTime

/** Works out the dark plan for the schedule modes (At a set time, After sunset); null for the modes with no schedule. */
object AppearancePlanner {
    fun plan(
        mode: LightDarkMode,
        darkAt: LocalTime,
        lightAt: LocalTime,
        place: AppearancePlace?,
        now: ZonedDateTime = ZonedDateTime.now(),
        zone: ZoneId = now.zone,
    ): DarkPlan? = when (mode) {
        LightDarkMode.SetTime -> DarkSchedule.forSetTimes(now, darkAt, lightAt, zone)
        LightDarkMode.Sunset ->
            if (place != null) DarkSchedule.forSunset(now, place.point, darkAt, lightAt, zone)
            else DarkSchedule.forSetTimes(now, darkAt, lightAt, zone, usedSetTimes = true)
        else -> null
    }
}
