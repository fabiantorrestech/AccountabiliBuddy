package com.accountabilibuddy.shared.tasks

/**
 * What is under the finger while a row (or a stack) is lifted on the phone's Tasks page (round 2 R7.3b; boards Tasks-Phone-Drag,
 * -Drag-Bin, -Drag-Pick). The page works it out from where its parts are on the screen; [ShelfDrag.resolve] turns it into what a
 * release there does.
 */
sealed interface ShelfDropSpot {
    /** The Today strip. */
    data object Strip : ShelfDropSpot

    /** The drop zone over the card's left edge (the page on the left). */
    data object Left : ShelfDropSpot

    /** The drop zone over the card's right edge (the page on the right). */
    data object Right : ShelfDropSpot

    /** The list of the page showing, with [insertAt] of its other rows above the finger. */
    data class Card(val insertAt: Int) : ShelfDropSpot

    /** The bottom bar's left zone ("Hold to pick a bin"), or the open picker around its tiles. */
    data object Bar : ShelfDropSpot

    /** The bottom bar's red Cancel. */
    data object Cancel : ShelfDropSpot

    /** A tile of the open picker: the page with this [key]. */
    data class Tile(val key: String) : ShelfDropSpot

    /** Anywhere else. */
    data object Elsewhere : ShelfDropSpot
}
