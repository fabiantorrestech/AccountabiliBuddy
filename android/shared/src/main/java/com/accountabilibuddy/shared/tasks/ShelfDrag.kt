package com.accountabilibuddy.shared.tasks

/**
 * The rules of dragging on the phone's Tasks page (round 2 R7.3b; change note §2, §4c, §4c-1; boards Tasks-Phone-Drag, -Drag-Bin,
 * -Label-Drag, -Drag-Pick): what a hold lifts, what a release does where, what the selection is afterwards, and the words the drag
 * shows. Pure, so the checks can call it; the page only measures where its parts are.
 *
 * The drop targets are the Today strip (Today › Queued, never This session), the pages left and right of the one showing (the
 * zones over the card's edges; an end has none unless wrap around is on), the page's own list (a new place), every page through
 * the bottom bar's picker, and the red Cancel. Holding a zone turns the page ([PAGE_TURN_MS]), so a drag can also carry a task to a
 * page further away and drop it on that page's list.
 */
object ShelfDrag {
    /** How long a hold takes to lift a row. */
    const val LIFT_MS = 450L

    /** How long a held drag waits on the bottom bar before the picker opens. */
    const val PICKER_HOLD_MS = 300L

    /** How long a held drag waits on a zone before the page turns (and between turns), as the PC's page buttons do. */
    const val PAGE_TURN_MS = 600L

    /** A drop zone's width (dp). */
    const val ZONE_DP = 58

    /** How far outside a zone the finger still counts as over it (dp; the prototype's 18). */
    const val ZONE_REACH_DP = 18

    /** The dragged card's width (dp). */
    const val GHOST_WIDTH_DP = 250

    /** How many cards show behind the dragged one for a stack, whatever the count. */
    const val MAX_BACK_CARDS = 2

    /** What a hold on [held] lifts: the whole selection when it is one of the selected rows, else just that row. */
    fun lifted(held: String, selecting: Boolean, selection: List<String>): List<String> =
        if (selecting && selection.any { it.equals(held, ignoreCase = true) }) selection else listOf(held)

    /** The key of the page a zone drops on: [step] -1 for the left one, 1 for the right; null when there is none. */
    fun zoneTarget(keys: List<String>, shown: Int, step: Int, wrap: Boolean): String? =
        ShelfPaging.neighbour(shown, keys.size, step, wrap)?.let { keys[it] }

    /**
     * What a release at [spot] does. [keys] are the pages, [shown] the one showing now, [origin] the key of the page the drag began
     * on. A release before the finger [travelled] selects the row. On the page's own list the rows take a new place when the view
     * is in "Your order" ([canReorder]); on the list of another page (the drag turned the page) they drop on that page.
     */
    fun resolve(spot: ShelfDropSpot, keys: List<String>, shown: Int, origin: String?, wrap: Boolean, travelled: Boolean, canReorder: Boolean): ShelfDrop {
        if (!travelled) return ShelfDrop.Select
        val shownKey = keys.getOrNull(shown)
        return when (spot) {
            ShelfDropSpot.Strip -> ShelfDrop.ToQueued
            ShelfDropSpot.Left -> zoneTarget(keys, shown, -1, wrap)?.let { ShelfDrop.ToPage(it) } ?: ShelfDrop.Nothing
            ShelfDropSpot.Right -> zoneTarget(keys, shown, 1, wrap)?.let { ShelfDrop.ToPage(it) } ?: ShelfDrop.Nothing
            is ShelfDropSpot.Card -> when {
                shownKey == null -> ShelfDrop.Nothing
                origin != null && shownKey != origin -> ShelfDrop.ToPage(shownKey)
                canReorder -> ShelfDrop.Reorder(spot.insertAt)
                else -> ShelfDrop.Nothing
            }
            // "You are here": the page showing is dimmed in the picker and takes no drop.
            is ShelfDropSpot.Tile -> if (spot.key == shownKey) ShelfDrop.Nothing else ShelfDrop.ToPage(spot.key)
            ShelfDropSpot.Cancel -> ShelfDrop.Cancel
            ShelfDropSpot.Bar, ShelfDropSpot.Elsewhere -> ShelfDrop.Nothing
        }
    }

    /**
     * Select mode and the selection after a release: a hold let go without moving selects [lifted] (starting select mode); Cancel
     * and a release that moved nothing keep the selection; a drop ends select mode, as the prototype does.
     */
    fun selectionAfter(drop: ShelfDrop, lifted: List<String>, selecting: Boolean, selection: List<String>): Pair<Boolean, List<String>> = when (drop) {
        ShelfDrop.Select -> true to (selection + lifted.filter { id -> selection.none { it.equals(id, ignoreCase = true) } })
        ShelfDrop.Cancel, ShelfDrop.Nothing -> selecting to selection
        ShelfDrop.ToQueued, is ShelfDrop.ToPage, is ShelfDrop.Reorder -> false to emptyList()
    }

    /** The selection with [id] added, or taken out when it was in it. */
    fun toggled(selection: List<String>, id: String): List<String> =
        if (selection.any { it.equals(id, ignoreCase = true) }) selection.filterNot { it.equals(id, ignoreCase = true) } else selection + id

    /** The selection without tasks that are gone (deleted or finished meanwhile, here or on another device). */
    fun pruned(selection: List<String>, live: Collection<String>): List<String> {
        val known = live.map { it.lowercase() }.toSet()
        return selection.filter { it.lowercase() in known }
    }

    /** The selection after "Select all": every row of the page showing; when they all are selected already, none of them. */
    fun selectAll(selection: List<String>, visible: List<String>): List<String> {
        val chosen = selection.map { it.lowercase() }.toSet()
        return if (visible.isNotEmpty() && visible.all { it.lowercase() in chosen }) {
            val shown = visible.map { it.lowercase() }.toSet()
            selection.filter { it.lowercase() !in shown }
        } else {
            selection + visible.filter { it.lowercase() !in chosen }
        }
    }

    /** "2 selected". */
    fun selectedText(count: Int): String = "$count selected"

    /** "Select all 4"; "Unselect all" when every row of the page is selected. */
    fun selectAllText(selection: List<String>, visible: List<String>): String {
        val chosen = selection.map { it.lowercase() }.toSet()
        return if (visible.isNotEmpty() && visible.all { it.lowercase() in chosen }) "Unselect all" else "Select all ${visible.size}"
    }

    /** A stack's name: "3 tasks". */
    fun stackText(count: Int): String = if (count == 1) "1 task" else "$count tasks"

    /**
     * The dragged card's detail line. One task: its label and the row's own parts ("ePT · #Release · #launch-prep"). Over a label's
     * zone or tile ([toLabel]) it says the change: "ePT → mPT · #Release" (the [tags] only). A stack names the first task's label
     * and the rest: "AI · + 1 more", "ePT → SDA · + 1 more".
     */
    fun ghostLine(label: String?, parts: List<String>, tags: String?, count: Int, toLabel: String? = null): String {
        val head = if (toLabel != null) "${label ?: "No label"} → $toLabel" else label
        return when {
            count > 1 -> listOfNotNull(head, "+ ${count - 1} more").joinToString(" · ")
            toLabel != null -> listOfNotNull(head, tags?.takeIf { it.isNotBlank() }).joinToString(" · ")
            else -> (listOfNotNull(head) + parts).joinToString(" · ")
        }
    }

    /** The lit Today strip's words before "Today · Queued": "Drop to add to " ("Or drop to add to " while selecting). */
    fun stripLead(selecting: Boolean): String = if (selecting) "Or drop to add to " else "Drop to add to "
}
