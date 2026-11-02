package com.accountabilibuddy.companion.stats

/** A small tile under a chart: a title, a big value and a line of detail. With [view] the tile opens that view. */
data class StatCard(val title: String, val value: String, val detail: String, val view: StatsView? = null)
