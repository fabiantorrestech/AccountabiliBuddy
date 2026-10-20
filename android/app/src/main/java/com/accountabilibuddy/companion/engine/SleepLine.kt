package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.SleepAmount

/** The evening line: "Bedtime 11:45 PM · 7h 00m of sleep" or "Sleep if you go now: 6h 35m", coloured by [amount]. */
data class SleepLine(val text: String, val amount: SleepAmount)
