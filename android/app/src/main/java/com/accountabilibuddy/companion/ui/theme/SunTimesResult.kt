package com.accountabilibuddy.companion.ui.theme

import java.time.ZonedDateTime

/** One day's sunrise and sunset in the local time zone. */
data class SunTimesResult(val sunrise: ZonedDateTime, val sunset: ZonedDateTime)
