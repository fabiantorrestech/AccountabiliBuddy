package com.accountabilibuddy.companion.today

/** Where one Today item is drawn: above the "More today" line, inside the fold, or not at all. [key] is what the prefs store. */
enum class TodayPlacement(val key: String, val label: String) {
    Above("a", "Shown"),
    Fold("f", "In More today"),
    Hidden("h", "Hidden"),
    ;

    companion object {
        fun fromKey(key: String): TodayPlacement? = entries.firstOrNull { it.key == key }
    }
}
