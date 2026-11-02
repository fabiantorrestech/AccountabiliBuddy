package com.accountabilibuddy.companion.stats

import java.time.Duration
import java.time.LocalDate

/** One day's deep work: counted focus across the user's devices (the PC's DeepWorkDay, without its longest stretch, which no phone view reads). */
data class StatsDay(val date: LocalDate, val focused: Duration)
