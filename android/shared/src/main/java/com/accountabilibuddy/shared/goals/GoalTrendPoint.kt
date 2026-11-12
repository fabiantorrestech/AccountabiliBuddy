package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/** One charted day: its value (the day's latest hand edit, else its first reading). */
data class GoalTrendPoint(val day: LocalDate, val value: Double)
