package com.accountabilibuddy.companion.stats

import java.time.LocalDate

/** One cell of a heatmap. [level] is 0 (nothing) to 4 (the most), or -1 when there is no data for it (not drawn). */
data class HeatCell(val row: Int, val column: Int, val level: Int, val description: String, val date: LocalDate? = null)
