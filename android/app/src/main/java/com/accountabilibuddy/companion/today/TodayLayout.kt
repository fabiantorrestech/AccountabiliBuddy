package com.accountabilibuddy.companion.today

/**
 * The person's choice for the phone's Today: the order of its parts and where each sits (above the fold, in "More today", hidden).
 * Plain Kotlin; kept in the `ui` preferences on this phone only, never synced.
 *
 * - Nothing chosen ([order] null, [placements] empty) draws Today exactly as before. With nothing running that is the
 *   declaration order of [TodayItem]; while a session runs the ring comes first, then the lunch chip and the guard banner
 *   ([SESSION_FIRST]), as it always did. Once the person reorders, their order is used in both states.
 * - A part missing from a saved order (added in a later version) goes to the end of the order, in its default place.
 * - A saved place a part may not take (see [TodayItem.allowed]) counts as its default place, so a stored value can never hide a
 *   guard or an obligation.
 */
data class TodayLayout(
    val order: List<TodayItem>? = null,
    val placements: Map<TodayItem, TodayPlacement> = emptyMap(),
) {
    val isDefault: Boolean get() = order == null && placements.isEmpty()

    fun placementOf(item: TodayItem): TodayPlacement = item.clamp(placements[item] ?: item.defaultPlacement)

    /** Every part, in the order Today draws them for the state of the day. */
    fun orderFor(inSession: Boolean): List<TodayItem> {
        val base = order ?: if (inSession) SESSION_FIRST else emptyList()
        return base + TodayItem.entries.filter { it !in base }
    }

    /** The parts at [placement], in drawing order. */
    fun itemsAt(placement: TodayPlacement, inSession: Boolean): List<TodayItem> =
        orderFor(inSession).filter { placementOf(it) == placement }

    /** The parts at [placement] as the editor lists them (the order of a day with no session). */
    fun editorItems(placement: TodayPlacement): List<TodayItem> = itemsAt(placement, inSession = false)

    /** Puts [item] at [placement]; a place the part may not take changes nothing. The part keeps its place in the order. */
    fun withPlacement(item: TodayItem, placement: TodayPlacement): TodayLayout {
        if (placement !in item.allowed) return this
        val next = if (placement == item.defaultPlacement) placements - item else placements + (item to placement)
        return copy(placements = next)
    }

    /** Hides [item], or brings it back to its default place; a part that is always shown is left alone. */
    fun toggleHidden(item: TodayItem): TodayLayout = when {
        item.alwaysShown -> this
        placementOf(item) == TodayPlacement.Hidden -> withPlacement(item, item.defaultPlacement)
        else -> withPlacement(item, TodayPlacement.Hidden)
    }

    /** Moves a part between the shown part and the fold (a hidden part is brought back first). */
    fun toggleFolded(item: TodayItem): TodayLayout = when {
        !item.canFold -> this
        placementOf(item) == TodayPlacement.Fold -> withPlacement(item, TodayPlacement.Above)
        else -> withPlacement(item, TodayPlacement.Fold)
    }

    /** The parts at [placement] in [newOrder] (the same parts, reordered); the others keep their places in the order. */
    fun withGroupOrder(placement: TodayPlacement, newOrder: List<TodayItem>): TodayLayout {
        val base = orderFor(inSession = false)
        val slots = base.indices.filter { placementOf(base[it]) == placement }
        if (slots.size != newOrder.size || slots.map { base[it] }.toSet() != newOrder.toSet()) return this
        val next = base.toMutableList()
        slots.forEachIndexed { i, slot -> next[slot] = newOrder[i] }
        return copy(order = next)
    }

    fun orderText(): String? = order?.joinToString(",") { it.key }

    fun placementsText(): String? = placements.takeIf { it.isNotEmpty() }?.entries?.joinToString(",") { "${it.key.key}:${it.value.key}" }

    companion object {
        /** Ring first, then the lunch chip and the guard banner: how Today has always drawn a running session. */
        private val SESSION_FIRST = listOf(TodayItem.Ring, TodayItem.LunchChip, TodayItem.Guard, TodayItem.Noise, TodayItem.DailySteps)

        /** Reads what [orderText] and [placementsText] wrote; anything unreadable or unknown is ignored. */
        fun parse(order: String?, placements: String?): TodayLayout {
            val items = order?.split(',')?.mapNotNull { TodayItem.fromKey(it.trim()) }?.distinct()?.takeIf { it.isNotEmpty() }
            val chosen = placements.orEmpty().split(',').mapNotNull { pair ->
                val (itemKey, placementKey) = pair.split(':').takeIf { it.size == 2 } ?: return@mapNotNull null
                val item = TodayItem.fromKey(itemKey.trim()) ?: return@mapNotNull null
                val placement = TodayPlacement.fromKey(placementKey.trim()) ?: return@mapNotNull null
                // A choice equal to the default, or one the part may not take, is not a choice.
                if (placement !in item.allowed || placement == item.defaultPlacement) null else item to placement
            }.toMap()
            return TodayLayout(items, chosen)
        }
    }
}
