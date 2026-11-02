package com.accountabilibuddy.companion.stats

import java.time.Duration
import java.time.Instant

/**
 * One visit off track inside a counted session: when it began and ended, and a key that tells visits to the same place apart
 * for joining them ([DriftMarks]). Only times are used by Stats: the key is never shown.
 */
data class DriftVisit(val start: Instant, val end: Instant, val key: String) {
    val length: Duration get() = Duration.between(start, end)
}
