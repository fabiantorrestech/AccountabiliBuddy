package com.accountabilibuddy.companion.ui.theme

import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId
import java.time.ZonedDateTime

/**
 * When the app should look dark: from a time of day to another (the set times) or from sunset to the next sunrise (ADR 49).
 * A port of the PC's `DarkSchedule`. Pure: a moment in, the state then and the next flip out.
 */
object DarkSchedule {
    // Days looked at on each side of today: enough to find the last flip before now and the first one after it, even when
    // a place's sun times land a day off its time zone's date.
    private const val DAYS_BEFORE = 2
    private const val DAYS_AFTER = 2

    private class Flip(val at: ZonedDateTime, val toDark: Boolean)

    /** Dark from [darkAt] until [lightAt] (either may be the earlier time of day). */
    fun forSetTimes(now: ZonedDateTime, darkAt: LocalTime, lightAt: LocalTime, zone: ZoneId, usedSetTimes: Boolean = false): DarkPlan {
        val today = now.toLocalDate()
        if (darkAt == lightAt) {
            // Nothing to switch between: stays light, looked at again tomorrow.
            return DarkPlan(false, localMoment(today.plusDays(1), LocalTime.MIDNIGHT, zone), usedSetTimes)
        }

        val flips = mutableListOf<Flip>()
        for (offset in -DAYS_BEFORE..DAYS_AFTER) {
            val day = today.plusDays(offset.toLong())
            flips += Flip(localMoment(day, darkAt, zone), toDark = true)
            flips += Flip(localMoment(day, lightAt, zone), toDark = false)
        }
        return planFrom(flips, now, usedSetTimes)
    }

    /**
     * Dark from sunset to the next sunrise at [point]. Where the sun does not rise or set (polar day or night), the set
     * times are used instead.
     */
    fun forSunset(now: ZonedDateTime, point: GeoPoint, fallbackDarkAt: LocalTime, fallbackLightAt: LocalTime, zone: ZoneId): DarkPlan {
        val today = now.toLocalDate()
        val flips = mutableListOf<Flip>()
        for (offset in -DAYS_BEFORE..DAYS_AFTER) {
            val sun = SunTimes.calculate(today.plusDays(offset.toLong()), point, zone)
                ?: return forSetTimes(now, fallbackDarkAt, fallbackLightAt, zone, usedSetTimes = true)
            flips += Flip(sun.sunrise, toDark = false)
            flips += Flip(sun.sunset, toDark = true)
        }
        return planFrom(flips, now, usedSetTimes = false)
    }

    private fun planFrom(flips: MutableList<Flip>, now: ZonedDateTime, usedSetTimes: Boolean): DarkPlan {
        flips.sortBy { it.at.toInstant() }
        var isDark = false
        var next: ZonedDateTime? = null
        for (flip in flips) {
            if (!flip.at.toInstant().isAfter(now.toInstant())) {
                isDark = flip.toDark
            } else {
                next = flip.at
                break
            }
        }
        return DarkPlan(isDark, next ?: now.plusHours(1), usedSetTimes)
    }

    // ZonedDateTime.of moves a time the clock skips (daylight saving) forward by the gap, the same moment the PC uses.
    private fun localMoment(day: LocalDate, time: LocalTime, zone: ZoneId): ZonedDateTime = ZonedDateTime.of(day, time, zone)
}
