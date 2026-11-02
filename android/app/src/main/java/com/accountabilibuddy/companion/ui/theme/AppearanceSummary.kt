package com.accountabilibuddy.companion.ui.theme

import java.time.LocalTime
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter
import java.util.Locale

/** The line under the Light or dark card ("Paper by day, switches to dark at 6:12 PM tonight."). Times are always 12-hour. */
object AppearanceSummary {
    private val twelveHour = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

    fun format(time: LocalTime): String = time.format(twelveHour)

    fun format(moment: ZonedDateTime): String = moment.format(twelveHour)

    /** @param plan the plan for the set times or sunset; null for Light, Dark and System. */
    fun build(theme: ThemeName, mode: LightDarkMode, darkAt: LocalTime, lightAt: LocalTime, plan: DarkPlan?, now: ZonedDateTime): String {
        val name = theme.label
        return when (mode) {
            LightDarkMode.Light -> "$name, always light."
            LightDarkMode.Dark -> "$name, always dark."
            LightDarkMode.System -> "$name, light or dark as the phone has it."
            LightDarkMode.SetTime, LightDarkMode.Sunset -> when {
                plan == null -> "$name."
                mode == LightDarkMode.SetTime && darkAt == lightAt -> "$name. The two times are the same, so it stays light."
                plan.isDark -> "$name after dark, switches to light at ${format(plan.nextSwitch)} ${dayWord(plan.nextSwitch, now, toDark = false)}."
                else -> "$name by day, switches to dark at ${format(plan.nextSwitch)} ${dayWord(plan.nextSwitch, now, toDark = true)}."
            }
        }
    }

    /** Why After sunset is using the set times, or null when all is well. */
    fun sunsetNote(mode: LightDarkMode, hasPlace: Boolean, plan: DarkPlan?, darkAt: LocalTime, lightAt: LocalTime): String? {
        if (mode != LightDarkMode.Sunset) return null
        val setTimes = "dark from ${format(darkAt)} to ${format(lightAt)}"
        return when {
            !hasPlace -> "No place yet, so the set times are used ($setTimes). Pick a city or type a place."
            plan?.usedSetTimes == true -> "The sun doesn't rise or set there today, so the set times are used ($setTimes)."
            else -> null
        }
    }

    private fun dayWord(at: ZonedDateTime, now: ZonedDateTime, toDark: Boolean): String {
        val atLocal = at.withZoneSameInstant(now.zone)
        return when {
            atLocal.toLocalDate() == now.toLocalDate() -> when {
                toDark && atLocal.hour >= 17 -> "tonight"
                !toDark && atLocal.hour < 12 -> "this morning"
                else -> "today"
            }
            atLocal.toLocalDate() == now.toLocalDate().plusDays(1) -> "tomorrow"
            else -> "on ${atLocal.format(DateTimeFormatter.ofPattern("EEEE", Locale.US))}"
        }
    }
}
