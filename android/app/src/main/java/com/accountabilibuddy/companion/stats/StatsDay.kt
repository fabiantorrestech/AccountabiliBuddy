package com.accountabilibuddy.companion.stats

import java.time.Duration
import java.time.LocalDate

/** One day's deep work: counted focus across the user's devices, and its longest unbroken stretch (the PC's DeepWorkDay). */
data class StatsDay(val date: LocalDate, val focused: Duration, val longest: Duration)
