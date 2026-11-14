package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.Instant

/** One counted stretch of the day's lunch (ADR 88): when it started and when it ended. A lunch can be taken in several pieces. */
data class LunchPiece(val start: Instant, val end: Instant) {
    /** How long the piece lasted (never negative). */
    val length: Duration get() = if (end.isAfter(start)) Duration.between(start, end) else Duration.ZERO
}
