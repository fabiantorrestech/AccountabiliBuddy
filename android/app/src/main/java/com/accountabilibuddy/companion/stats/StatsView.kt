package com.accountabilibuddy.companion.stats

/**
 * The views of the Stats tab (boards 19e to 19h), in their default order. [key] is stored in the `stats` prefs and is the
 * part after `stats/` in an `ab.open` route. [ranges] are the segments a view offers (calendar kinds first, then the rolling ones and
 * Custom); none for Sleep and Phone. [usesPeriod] is true for the views that read the chosen period and step through it with ‹ ›;
 * Intensity and Focus graph keep their fixed windows ending today, as on the PC.
 * Personal bests (board 19h) is not here: the phone has no such numbers yet.
 */
enum class StatsView(val key: String, val title: String, val about: String, val ranges: List<StatsRange>, val usesPeriod: Boolean = false) {
    Overview(
        "overview", "Overview", "The week at a glance",
        listOf(StatsRange.Day, StatsRange.Week, StatsRange.Month, StatsRange.Last7, StatsRange.Last30, StatsRange.Last90, StatsRange.Custom), true,
    ),
    Consistency(
        "consistency", "Consistency", "Heatmap of days you hit your goal",
        listOf(StatsRange.Week, StatsRange.Month, StatsRange.Year, StatsRange.Last7, StatsRange.Last30, StatsRange.Last90), true,
    ),
    Intensity("intensity", "Intensity", "Hour-by-day focus heatmap", listOf(StatsRange.Week, StatsRange.Month, StatsRange.Year)),
    FocusGraph("focus-graph", "Focus graph", "Deep work and drift over weeks", listOf(StatsRange.Week, StatsRange.Month)),
    Drift(
        "drift", "Drift", "What pulls you away, and when",
        listOf(StatsRange.Week, StatsRange.Month, StatsRange.Last7, StatsRange.Last30, StatsRange.Last90, StatsRange.Custom), true,
    ),
    Sleep("sleep", "Sleep", "Bedtime kept, hours slept", emptyList()),
    Phone("phone", "Phone", "Pickups and screen time in sessions", emptyList()),
    Steps(
        "steps", "Steps", "Daily steps against your goal",
        listOf(StatsRange.Week, StatsRange.Month, StatsRange.Last7, StatsRange.Last30, StatsRange.Last90, StatsRange.Custom), true,
    );

    /** The range a person who never chose one gets (the one the board shows); null for a view with no ranges. */
    val defaultRange: StatsRange?
        get() = when (this) {
            Overview -> StatsRange.Week
            Consistency -> StatsRange.Year
            Intensity, FocusGraph, Drift -> StatsRange.Month
            Steps -> StatsRange.Last30
            Sleep, Phone -> null
        }

    /** [range] when this view has it, else the nearest one it has (the PC's rule); null for a view with no ranges. */
    fun coerce(range: StatsRange?): StatsRange? {
        if (ranges.isEmpty()) return null
        if (range != null && range in ranges) return range
        val wanted = range ?: defaultRange
        return when {
            wanted == null -> ranges.first()
            wanted in ranges -> wanted
            wanted == StatsRange.Day -> StatsRange.Week
            StatsRange.Month in ranges -> StatsRange.Month
            else -> ranges.first()
        }
    }

    companion object {
        fun fromKey(key: String?): StatsView? = entries.firstOrNull { it.key == key?.lowercase() }
    }
}
