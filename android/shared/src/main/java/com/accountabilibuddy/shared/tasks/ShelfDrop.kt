package com.accountabilibuddy.shared.tasks

/** What letting go of a lifted row (or stack) does on the phone's Tasks page ([ShelfDrag.resolve]). */
sealed interface ShelfDrop {
    /** Let go without moving: the row is selected (select mode starts, or it joins the selection). */
    data object Select : ShelfDrop

    /** Into Today › Queued (the Today strip); never This session. */
    data object ToQueued : ShelfDrop

    /** Onto the page with this [key]: its bin, its label (replaces the old one) or its tag (added). */
    data class ToPage(val key: String) : ShelfDrop

    /** A new place on its own page: [insertAt] of the other rows above it. */
    data class Reorder(val insertAt: Int) : ShelfDrop

    /** The red Cancel: nothing moves and the selection is kept. */
    data object Cancel : ShelfDrop

    /** Nothing: the row goes back where it was (also a place it can't be reordered into); the selection is kept. */
    data object Nothing : ShelfDrop
}
