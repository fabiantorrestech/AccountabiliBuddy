package com.accountabilibuddy.shared.sync

import java.time.LocalDate

/** One day's step count: [date] is the day on the phone's calendar. */
data class SyncStepDay(val date: LocalDate, val count: Int)
