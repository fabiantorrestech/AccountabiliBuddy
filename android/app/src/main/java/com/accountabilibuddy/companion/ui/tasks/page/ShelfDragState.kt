package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.runtime.Stable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import com.accountabilibuddy.shared.tasks.ShelfDropSpot

/**
 * A drag on the phone's Tasks page while it lasts (round 2 R7.3b): which tasks are lifted, where the finger is and what is under it.
 * The page's parts report where they are on the screen (root coordinates) so the one gesture on the page can tell them apart; the
 * rules of what a release does are [com.accountabilibuddy.shared.tasks.ShelfDrag]'s.
 *
 * The measured places are plain fields (only the gesture reads them); what the page draws from is Compose state. [pointer] is read
 * only where things are placed (the dragged card), so a moving finger redraws nothing else.
 */
@Stable
class ShelfDragState {
    /** The page's top left corner on the screen: the gesture's own positions are relative to it. */
    var host: Offset = Offset.Zero

    /** The Today strip. */
    var strip: Rect? = null

    /** The row holding the card and its peeks: the drop zones lie over its left and right ends. */
    var pager: Rect? = null

    /** Each shown page's list, by the page's key. */
    val lists = HashMap<String, Rect>()

    /** The rows a hold can lift, by task id (an open task and a finished one aren't in it). */
    val rows = HashMap<String, Rect>()

    /** The bottom bar's two zones and, while it is open, the picker and its tiles (by page key). */
    var barPick: Rect? = null
    var barCancel: Rect? = null
    var picker: Rect? = null
    val tiles = HashMap<String, Rect>()

    /** What lies over the rows and takes its own touches (the "+" button, the toast, select mode's bar): a hold there lifts nothing. */
    val blockers = HashMap<String, Rect>()

    /** The lifted tasks, in the rows' order; empty when nothing is dragged. */
    var ids by mutableStateOf<List<String>>(emptyList())
        private set

    /** The task under the finger when the hold lifted. */
    var lead by mutableStateOf<String?>(null)
        private set

    /** The key of the page the drag began on. */
    var origin by mutableStateOf<String?>(null)
        private set

    /** Where the finger is on the screen. */
    var pointer by mutableStateOf(Offset.Zero)

    /** Whether the finger has moved since the lift (a release before that selects the row). */
    var travelled by mutableStateOf(false)

    /** What is under the finger. */
    var spot by mutableStateOf<ShelfDropSpot>(ShelfDropSpot.Elsewhere)

    /** Whether the bottom bar's picker is open. */
    var pickerOpen by mutableStateOf(false)

    /**
     * The sides (-1 left, 1 right) whose zone the hold began in: a row held near the card's edge is under a zone at once, so that
     * zone only counts after the finger has left it once.
     */
    var blockedSides: Set<Int> = emptySet()

    val active: Boolean get() = ids.isNotEmpty()

    fun lift(lifted: List<String>, held: String, page: String?, at: Offset) {
        pointer = at
        travelled = false
        spot = ShelfDropSpot.Elsewhere
        pickerOpen = false
        origin = page
        lead = held
        ids = lifted
    }

    fun end() {
        ids = emptyList()
        lead = null
        origin = null
        pickerOpen = false
        travelled = false
        spot = ShelfDropSpot.Elsewhere
        blockedSides = emptySet()
        tiles.clear()
        picker = null
        barPick = null
        barCancel = null
    }
}
