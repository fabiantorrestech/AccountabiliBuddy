package com.accountabilibuddy.companion.engine

import java.util.UUID

/** A stretch on the bar, as fractions of its width (0 = left edge, 1 = right edge). */
data class DayBarSpan(val start: Double, val end: Double, val label: String, val profileId: UUID? = null)
