package com.accountabilibuddy.companion.model

import java.time.LocalTime

/** When a night's sleep starts and ends. A bedtime before noon (e.g. 00:30) is after midnight. */
data class SleepTimes(val bedtime: LocalTime, val wake: LocalTime, val wakeByAlarm: Boolean = false)
