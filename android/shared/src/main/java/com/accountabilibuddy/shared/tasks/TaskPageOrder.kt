package com.accountabilibuddy.shared.tasks

/**
 * The left-to-right order of the Shelf's pages on the phone (round 2 R7.3; the PC's TaskColumnOrder, rule for rule, where it orders
 * the columns): per grouping, the order this device stored first, then any page it doesn't name in the grouping's own order (a new
 * label or tag lands at the right). Moving a page rewrites only the places of the pages shown now: a page out of view (an empty label
 * or tag group, one a filter hides) keeps its stored slot for when it comes back. Device-local (plan §3b item 3), never synced.
 */
object TaskPageOrder {
    /** The grouping a view's Group by stores its order under: "bins", "label" or "tag"; null for None (one list, nothing to order). */
    fun groupingOf(group: String): String? = when (group) {
        TaskViewConfig.GROUP_BINS -> TaskViewConfig.GROUP_BINS
        TaskViewConfig.GROUP_LABEL -> TaskViewConfig.GROUP_LABEL
        TaskViewConfig.GROUP_TAG -> TaskViewConfig.GROUP_TAG
        else -> null
    }

    /** [items] in the stored order (by [keyOf]), the ones it doesn't name after them as they came. */
    fun <T> apply(items: List<T>, stored: List<String>?, keyOf: (T) -> String): List<T> {
        if (stored.isNullOrEmpty()) return items.toList()
        val rank = HashMap<String, Int>()
        stored.forEachIndexed { index, key -> rank.putIfAbsent(key, index) }
        // A stable sort: named pages by their stored place, the rest after them in their own order.
        return items.withIndex().sortedWith(compareBy({ rank[keyOf(it.value)] ?: Int.MAX_VALUE }, { it.index })).map { it.value }
    }

    /** The order to store after [key] moved [step] places (-1 or 1) among the pages [shown]; null when it can't move (an end, or not shown). */
    fun movedBy(shown: List<String>, stored: List<String>?, key: String, step: Int): List<String>? {
        val from = shown.indexOf(key)
        val to = from + step
        return if (from < 0 || to < 0 || to >= shown.size) null else movedTo(shown, stored, key, to)
    }

    /** The order to store after [key] was dragged onto the place of [targetKey] among the pages [shown]; null when nothing changes. */
    fun movedOnto(shown: List<String>, stored: List<String>?, key: String, targetKey: String): List<String>? {
        val from = shown.indexOf(key)
        val to = shown.indexOf(targetKey)
        return if (from < 0 || to < 0 || from == to) null else movedTo(shown, stored, key, to)
    }

    /** The order to store after the page at [from] of [shown] was dragged to [to] (the Page order sheet's ≡); null when nothing changes. */
    fun movedIndex(shown: List<String>, stored: List<String>?, from: Int, to: Int): List<String>? =
        if (from !in shown.indices || to !in shown.indices || from == to) null else movedTo(shown, stored, shown[from], to)

    /**
     * The order to store after the pages [shown] were put in the order [newShown] (the same keys): the full order (the stored keys,
     * then the shown ones it doesn't name yet) with the shown pages' slots refilled in their new order; every other slot keeps its key.
     */
    fun reordered(shown: List<String>, stored: List<String>?, newShown: List<String>): List<String> {
        val full = ArrayList<String>()
        for (known in stored.orEmpty() + shown) {
            if (known !in full) full += known
        }

        val shownKeys = shown.toSet()
        val wanted = newShown.filter { it in shownKeys }.distinct().let { named -> named + shown.filter { it !in named } }
        var next = 0
        for (index in full.indices) {
            if (full[index] in shownKeys) full[index] = wanted[next++]
        }

        return full
    }

    private fun movedTo(shown: List<String>, stored: List<String>?, key: String, to: Int): List<String> {
        val newShown = shown.toMutableList()
        newShown.remove(key)
        newShown.add(to, key)
        return reordered(shown, stored, newShown)
    }
}
