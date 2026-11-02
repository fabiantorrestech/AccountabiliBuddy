package com.accountabilibuddy.companion.stats

import java.time.LocalDateTime

/**
 * A counted focus session on one of the user's devices, for a day's list: its device, local times and the profile's name
 * (never an added session's note, which never travels). [title] is the profile's name, "Added by hand", or "Focus session".
 */
data class StatsSession(val device: String, val start: LocalDateTime, val end: LocalDateTime, val title: String)
