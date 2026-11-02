package com.accountabilibuddy.companion.ui.stats

import com.accountabilibuddy.companion.ui.HealthStatus

/**
 * What the Sleep view says about the night, the content the old Stats card held: the planned night, and whether the watch's
 * measured sleep can be read from Health Connect.
 *
 * @property plannedText "Planned: 10:30 PM – 6:30 AM (8h 00m)"; null with sleep mode off.
 * @property lastSleepText "Last sleep: 7h 05m (11:20 PM – 6:25 AM)"; null when none was recorded.
 */
class SleepNightModel(val plannedText: String?, val health: HealthStatus, val lastSleepText: String?)
