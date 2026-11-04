package com.accountabilibuddy.companion.stats

/**
 * How far a Stats view looks (the segmented controls). The names are what the `stats` preferences store, so the first four
 * keep theirs and a value saved by an older version still loads. Day, Week, Month and Year are calendar terms; Last 7, 30
 * and 90 are rolling windows ending today; Custom is two picked dates.
 */
enum class StatsRange(val label: String, val isRolling: Boolean = false) {
    Day("Day"),
    Week("Week"),
    Month("Month"),
    Year("Year"),
    Last7("Last 7", true),
    Last30("Last 30", true),
    Last90("Last 90", true),
    Custom("Custom", true),
}
