package com.accountabilibuddy.companion.model

import java.time.LocalDate
import java.time.LocalTime

/** A wake time changed on the PC for one night only: it applies to the morning of [morning] and nowhere else. */
data class WakeOnce(val morning: LocalDate, val wake: LocalTime, val overridesAlarm: Boolean = false)
