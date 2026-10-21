package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.SleepAmount

/** The evening countdown, e.g. "Sleep if you go now: 7h 30m · up 7:00 AM", coloured by [amount] against the sleep goal. */
data class SleepLine(val text: String, val amount: SleepAmount)
