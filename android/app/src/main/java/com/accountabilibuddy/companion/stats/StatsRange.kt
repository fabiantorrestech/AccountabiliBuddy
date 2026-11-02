package com.accountabilibuddy.companion.stats

/** How far back a Stats view looks (the segmented control). */
enum class StatsRange(val label: String) {
    Day("Day"),
    Week("Week"),
    Month("Month"),
    Year("Year"),
}
