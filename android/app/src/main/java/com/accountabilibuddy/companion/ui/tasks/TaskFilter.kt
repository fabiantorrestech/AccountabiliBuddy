package com.accountabilibuddy.companion.ui.tasks

/**
 * The category chip and the search of the Tasks pane, as one test a row passes or not. [category] is the chip picked (a name);
 * null shows every category, and a row with none then passes too. [search] is matched against the texts a row offers (its
 * title, steps and note); a blank search matches all. It lives in memory only.
 */
class TaskFilter(private val category: String?, search: String) {
    private val needle = search.trim()

    /** True when a row of [rowCategory] with these [texts] is shown. */
    fun allows(rowCategory: String?, vararg texts: String?): Boolean {
        if (category != null && !category.equals(rowCategory, ignoreCase = true)) return false
        return needle.isEmpty() || texts.any { it?.contains(needle, ignoreCase = true) == true }
    }

    /** True while a chip or a search narrows the lists. */
    val isOn: Boolean get() = category != null || needle.isNotEmpty()
}
