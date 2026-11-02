package com.accountabilibuddy.companion.stats

/** A stretch on the day strip: [from] and [to] are 0 (12 AM) to 1 (the next 12 AM). */
data class DayStripSpan(val from: Float, val to: Float, val kind: DayStripKind, val description: String)
