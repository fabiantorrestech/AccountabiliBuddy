package com.accountabilibuddy.companion.usage

import java.time.Duration

/** One app's time in front today, and how much of it fell inside planned focus blocks. */
data class AppUsage(val packageName: String, val label: String, val total: Duration, val inFocus: Duration)
