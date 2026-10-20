package com.accountabilibuddy.companion.model

import java.time.LocalDateTime

/** A stretch of local (wall-clock) time: [start] inclusive, [end] exclusive. */
data class LocalWindow(val start: LocalDateTime, val end: LocalDateTime) {
    fun contains(time: LocalDateTime): Boolean = !time.isBefore(start) && time.isBefore(end)
}
