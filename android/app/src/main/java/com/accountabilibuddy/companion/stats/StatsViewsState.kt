package com.accountabilibuddy.companion.stats

/**
 * Which views the chip row shows and in what order (board 19h). A hidden view is still one tap away in the sheet. At least one
 * view always stays shown. Plain Kotlin, so the tablet's left column can list the same order.
 */
data class StatsViewsState(val order: List<StatsView> = StatsView.entries, val hidden: Set<StatsView> = emptySet()) {
    /** The views shown as chips, in order. */
    val shown: List<StatsView> get() = order.filter { it !in hidden }

    /** Moves [view] to the place [to] (0 is first). */
    fun move(view: StatsView, to: Int): StatsViewsState {
        val from = order.indexOf(view)
        if (from < 0) return this
        val next = order.toMutableList().apply { removeAt(from); add(to.coerceIn(0, size), view) }
        return copy(order = next)
    }

    /** Hides [view], or shows it again; the last shown view can't be hidden. */
    fun toggle(view: StatsView): StatsViewsState = when {
        view in hidden -> copy(hidden = hidden - view)
        shown.size <= 1 -> this
        else -> copy(hidden = hidden + view)
    }

    /** The view to show when [current] is hidden or gone: [current] itself when shown, else the first shown one. */
    fun resolve(current: StatsView): StatsView = if (current in shown) current else shown.first()

    /** The state as the prefs store it: view keys, comma separated. */
    fun orderText(): String = order.joinToString(",") { it.key }

    fun hiddenText(): String = hidden.joinToString(",") { it.key }

    companion object {
        /** Reads what [orderText] and [hiddenText] wrote; a view that is new (or an unreadable value) takes its default place at the end. */
        fun parse(order: String?, hidden: String?): StatsViewsState {
            val known = order.orEmpty().split(',').mapNotNull { StatsView.fromKey(it.trim()) }.distinct()
            val all = known + StatsView.entries.filter { it !in known }
            val hiddenViews = hidden.orEmpty().split(',').mapNotNull { StatsView.fromKey(it.trim()) }.toSet()
            return StatsViewsState(all, if (hiddenViews.size >= all.size) emptySet() else hiddenViews)
        }
    }
}
