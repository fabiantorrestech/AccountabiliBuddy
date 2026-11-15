package com.accountabilibuddy.companion.engine

import java.time.LocalDateTime
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * Times as the phone shows them (12- or 24-hour), kept out of the engine's logic. The 12-hour text is always "4:00 PM" in capitals
 * (FX-A: a phone set to another English wrote "4:00 pm"), so the default locale is fixed.
 */
class ClockFormat(val is24Hour: Boolean, locale: Locale = Locale.US) {
    private val time = DateTimeFormatter.ofPattern(if (is24Hour) "HH:mm" else "h:mm a", locale)

    fun time(value: LocalDateTime): String = value.format(time)

    /** Hour label under the bar: "8a", "12p" or "08:00". */
    fun hour(hour: Int): String {
        if (is24Hour) {
            return "%02d:00".format(hour)
        }

        val shown = if (hour % 12 == 0) 12 else hour % 12
        return "$shown${if (hour < 12) "a" else "p"}"
    }
}
