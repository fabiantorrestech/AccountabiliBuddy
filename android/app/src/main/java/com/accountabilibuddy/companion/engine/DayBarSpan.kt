package com.accountabilibuddy.companion.engine

import java.util.UUID

/**
 * A stretch on the bar, as fractions of its width (0 = left edge, 1 = right edge).
 * @param color ARGB of a day plan part's category; null = the lane's own colour.
 * @param name drawn inside it when there's room (a plan part's name).
 */
data class DayBarSpan(
    val start: Double,
    val end: Double,
    val label: String,
    val profileId: UUID? = null,
    val color: Int? = null,
    val name: String? = null,
)
