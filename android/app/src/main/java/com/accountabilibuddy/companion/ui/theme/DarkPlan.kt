package com.accountabilibuddy.companion.ui.theme

import java.time.ZonedDateTime

/**
 * Whether the app should look dark at a moment, and when that next flips (set times or sunset, ADR 49).
 * [usedSetTimes] is true when sunset could not be worked out (polar day or night) and the set times were used.
 */
data class DarkPlan(val isDark: Boolean, val nextSwitch: ZonedDateTime, val usedSetTimes: Boolean)
