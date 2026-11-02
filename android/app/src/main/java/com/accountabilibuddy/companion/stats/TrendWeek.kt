package com.accountabilibuddy.companion.stats

import java.time.Duration
import java.time.LocalDate

/**
 * One week (Monday to Sunday) on the Focus graph.
 *
 * @property start The week's Monday.
 * @property dailyFocus Deep work per day over the week's workdays so far (and any other day with focus); null when no day counts.
 * @property dailyDrift Drift per day over the same days; null when no day counts.
 */
data class TrendWeek(val start: LocalDate, val dailyFocus: Duration?, val dailyDrift: Duration?)
