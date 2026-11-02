package com.accountabilibuddy.companion.stats

/**
 * A stretch of whole hours on the Intensity view (the PC's IntensityWindow).
 *
 * @property startHour 0 to 23.
 * @property endHour After [startHour], up to 24.
 * @property share The part of all focus that falls in it, 0 to 1.
 * @property focusMinutes Focus minutes in it, added up over the range.
 * @property driftCount Drift visits that began in it.
 */
data class IntensityWindow(val startHour: Int, val endHour: Int, val share: Double, val focusMinutes: Double, val driftCount: Int)
